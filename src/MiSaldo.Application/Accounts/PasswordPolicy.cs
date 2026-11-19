using MiSaldo.Application.Common;

namespace MiSaldo.Application.Accounts;

/// <summary>
/// The password rules enforced on the server (RNF-SEG-09). The only rule at
/// launch is a minimum length of 10 characters.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 10;

    public const string TooShortCode = "password.too_short";

    public static Result Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
        {
            return Result.Failure(Error.Validation(
                TooShortCode,
                $"The password must have at least {MinimumLength} characters."));
        }

        return Result.Success();
    }
}
