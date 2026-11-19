using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Common;
using MiSaldo.Domain.Accounts;
using MiSaldo.Domain.Common;
using MiSaldo.Domain.Periods;

namespace MiSaldo.Application.Accounts;

/// <summary>The data a visitor sends to create an account (RF-AUT-01).</summary>
public sealed record RegisterUserCommand(
    string Email,
    string Password,
    string FullName,
    DateOnly BirthDate,
    string Country,
    string? Gender,
    bool AcceptsPrivacyPolicy,
    bool AcceptsTerms);

public sealed record RegisterUserResult(Guid UserId);

/// <summary>Checks the shape of a registration before anything touches the database (RNF-SEG-06).</summary>
public static class RegistrationValidator
{
    public const int MaxEmailLength = 320;
    public const int MaxNameLength = 256;
    public const int MaxGenderLength = 32;

    public const string InvalidEmailCode = "registration.invalid_email";
    public const string InvalidNameCode = "registration.invalid_name";
    public const string InvalidCountryCode = "registration.invalid_country";
    public const string InvalidGenderCode = "registration.invalid_gender";
    public const string ConsentRequiredCode = "registration.consent_required";

    public static Result Validate(RegisterUserCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!IsPlausibleEmail(command.Email))
        {
            return Result.Failure(Error.Validation(InvalidEmailCode, "The email address is not valid."));
        }

        if (string.IsNullOrWhiteSpace(command.FullName) || command.FullName.Trim().Length > MaxNameLength)
        {
            return Result.Failure(Error.Validation(
                InvalidNameCode, $"The full name is required and must have at most {MaxNameLength} characters."));
        }

        var country = command.Country?.Trim() ?? string.Empty;
        if (country.Length != 2 || !country.All(char.IsAsciiLetter))
        {
            return Result.Failure(Error.Validation(
                InvalidCountryCode, "The country must be a two-letter ISO code."));
        }

        if (command.Gender is { } gender && gender.Trim().Length > MaxGenderLength)
        {
            return Result.Failure(Error.Validation(
                InvalidGenderCode, $"The gender must have at most {MaxGenderLength} characters."));
        }

        if (!command.AcceptsPrivacyPolicy || !command.AcceptsTerms)
        {
            return Result.Failure(Error.Validation(
                ConsentRequiredCode, "The privacy policy and the terms of use must be accepted."));
        }

        return PasswordPolicy.Validate(command.Password);
    }

    /// <summary>Emails are stored trimmed and lower-case, so lookups are case-insensitive.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    // A deliberately simple check: one '@', something on both sides, a dot in
    // the domain and no whitespace. Proof that the address works comes from the
    // verification step, not from parsing it.
    private static bool IsPlausibleEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var trimmed = email.Trim();
        if (trimmed.Length > MaxEmailLength || trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var at = trimmed.IndexOf('@');
        if (at <= 0 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1)
        {
            return false;
        }

        var domain = trimmed[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        return dot > 0 && dot < domain.Length - 1;
    }
}

/// <summary>
/// Creates an account: user, profile, settings, consent, credentials, the
/// starting categories and an email verification token, all in one
/// transaction (RF-AUT-01 to RF-AUT-03, RF-CAT-01, RF-PRV-01, RNF-DAT-01).
/// </summary>
public sealed class RegisterUserHandler
{
    public const string UnderageCode = "registration.underage";
    public const string UnavailableCode = "registration.unavailable";
    public const string AlreadyAuthenticatedCode = "registration.already_authenticated";

    private readonly IUserRepository _users;
    private readonly IAuthRepository _auth;
    private readonly INewUserDataSeeder _seeder;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;
    private readonly IAccountNotifier _notifier;
    private readonly RegistrationOptions _options;
    private readonly ICurrentUserAccessor _currentUser;

    public RegisterUserHandler(
        IUserRepository users,
        IAuthRepository auth,
        INewUserDataSeeder seeder,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ITokenService tokens,
        IClock clock,
        IAccountNotifier notifier,
        RegistrationOptions options,
        ICurrentUserAccessor currentUser)
    {
        _users = users;
        _auth = auth;
        _seeder = seeder;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokens = tokens;
        _clock = clock;
        _notifier = notifier;
        _options = options;
        _currentUser = currentUser;
    }

    public async Task<Result<RegisterUserResult>> HandleAsync(
        RegisterUserCommand command, CancellationToken cancellationToken = default)
    {
        // Someone already signed in has an account; creating another from the
        // same session is a mistake, not a registration.
        if (_currentUser.UserId is not null)
        {
            return Result.Failure<RegisterUserResult>(Error.Forbidden(
                AlreadyAuthenticatedCode, "Sign out before creating a new account."));
        }

        var validation = RegistrationValidator.Validate(command);
        if (validation.IsFailure)
        {
            return Result.Failure<RegisterUserResult>(validation.Error!);
        }

        var now = _clock.UtcNow;
        var userId = Guid.NewGuid();

        // CP-11: the age rule is checked before anything is stored, so an
        // under-18 visitor leaves no data behind (RF-AUT-02).
        User user;
        try
        {
            user = User.Register(userId, $"local:{userId}", command.BirthDate, _clock.Today(_options.TimeZone));
        }
        catch (UnderageUserException)
        {
            return Result.Failure<RegisterUserResult>(Error.Unprocessable(
                UnderageCode, $"MiSaldo is only available to people aged {User.MinimumAgeYears} or older."));
        }

        var email = RegistrationValidator.NormalizeEmail(command.Email);

        // The message does not say the email is taken, so the endpoint cannot be
        // used to confirm which addresses have accounts (RNF-SEG-05).
        var unavailable = Result.Failure<RegisterUserResult>(Error.Conflict(
            UnavailableCode, "The account could not be created with these details."));

        if (await _users.EmailExistsAsync(email, cancellationToken))
        {
            return unavailable;
        }

        // Hashing is slow on purpose; do it before opening the transaction.
        var passwordHash = _passwordHasher.Hash(command.Password);
        var verificationToken = _tokens.CreateOpaqueToken();

        try
        {
            await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                _users.Add(user);
                _users.Add(new UserProfile(
                    Guid.NewGuid(),
                    userId,
                    command.FullName.Trim(),
                    email,
                    command.Country.Trim().ToUpperInvariant(),
                    string.IsNullOrWhiteSpace(command.Gender) ? null : command.Gender.Trim()));
                _users.Add(new UserSettings(
                    Guid.NewGuid(),
                    userId,
                    _options.DefaultCurrency,
                    _options.DefaultLanguage,
                    ThemePreference.System,
                    PeriodType.Monthly,
                    new PeriodSettings()));
                _users.Add(new ConsentRecord(
                    Guid.NewGuid(), userId, _options.PrivacyPolicyVersion, _options.TermsVersion, now));

                _auth.Add(new UserCredential(Guid.NewGuid(), userId, passwordHash));
                _auth.Add(new OneTimeToken(
                    Guid.NewGuid(),
                    userId,
                    OneTimeTokenPurpose.EmailVerification,
                    verificationToken.Hash,
                    now,
                    now + _options.EmailVerificationTokenLifetime));

                await _unitOfWork.SaveChangesAsync(ct);
                await _seeder.SeedCategoriesAsync(userId, DefaultCategories.Create(), ct);
            }, cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            // Another registration with the same email committed first.
            return unavailable;
        }

        await _notifier.SendEmailVerificationAsync(
            new AccountTokenMessage(userId, email, verificationToken.Value), cancellationToken);

        return Result.Success(new RegisterUserResult(userId));
    }
}
