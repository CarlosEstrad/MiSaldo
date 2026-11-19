using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Accounts;
using MiSaldo.Application.Common;
namespace MiSaldo.Application.Profile;

/// <summary>The profile as the user sees it (RF-PER-01). The birth date is shown but never edited.</summary>
public sealed record ProfileDto(
    string FullName,
    string Email,
    DateOnly BirthDate,
    string Country,
    string? Gender);

/// <summary>The editable profile fields (RF-PER-02). Email and birth date are not here on purpose.</summary>
public sealed record UpdateProfileCommand(string FullName, string Country, string? Gender);

/// <summary>Reads the current user's profile.</summary>
public sealed class GetProfileHandler
{
    public const string NotFoundCode = "profile.not_found";

    private readonly IUserRepository _users;
    private readonly ICurrentUserAccessor _currentUser;

    public GetProfileHandler(IUserRepository users, ICurrentUserAccessor currentUser)
    {
        _users = users;
        _currentUser = currentUser;
    }

    public async Task<Result<ProfileDto>> HandleAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<ProfileDto>(CurrentUserErrors.NoUser);
        }

        var profile = await _users.GetProfileAsync(userId, cancellationToken);
        var user = await _users.GetByIdAsync(userId, cancellationToken);
        if (profile is null || user is null)
        {
            return Result.Failure<ProfileDto>(Error.NotFound(NotFoundCode, "The profile was not found."));
        }

        return Result.Success(new ProfileDto(profile.FullName, profile.Email, user.BirthDate, profile.Country, profile.Gender));
    }
}

/// <summary>Updates the editable profile fields with the same rules as registration (RF-PER-02).</summary>
public sealed class UpdateProfileHandler
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly GetProfileHandler _getProfile;

    public UpdateProfileHandler(
        IUserRepository users, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser, GetProfileHandler getProfile)
    {
        _users = users;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _getProfile = getProfile;
    }

    public async Task<Result<ProfileDto>> HandleAsync(UpdateProfileCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<ProfileDto>(CurrentUserErrors.NoUser);
        }

        if (string.IsNullOrWhiteSpace(command.FullName) || command.FullName.Trim().Length > RegistrationValidator.MaxNameLength)
        {
            return Result.Failure<ProfileDto>(Error.Validation(
                RegistrationValidator.InvalidNameCode,
                $"The full name is required and must have at most {RegistrationValidator.MaxNameLength} characters."));
        }

        var country = command.Country?.Trim() ?? string.Empty;
        if (country.Length != 2 || !country.All(char.IsAsciiLetter))
        {
            return Result.Failure<ProfileDto>(Error.Validation(
                RegistrationValidator.InvalidCountryCode, "The country must be a two-letter ISO code."));
        }

        var gender = string.IsNullOrWhiteSpace(command.Gender) ? null : command.Gender.Trim();
        if (gender is { Length: > RegistrationValidator.MaxGenderLength })
        {
            return Result.Failure<ProfileDto>(Error.Validation(
                RegistrationValidator.InvalidGenderCode,
                $"The gender must have at most {RegistrationValidator.MaxGenderLength} characters."));
        }

        var profile = await _users.GetProfileAsync(userId, cancellationToken);
        if (profile is null)
        {
            return Result.Failure<ProfileDto>(Error.NotFound(GetProfileHandler.NotFoundCode, "The profile was not found."));
        }

        profile.Update(command.FullName.Trim(), country.ToUpperInvariant(), gender);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await _getProfile.HandleAsync(cancellationToken);
    }
}
