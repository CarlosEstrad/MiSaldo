namespace MiSaldo.Application.Common;

/// <summary>
/// The failure returned by a private use case that runs without an
/// authenticated user. The API already rejects such requests with 401; this is
/// the second line of defence (RF-AUT-08).
/// </summary>
public static class CurrentUserErrors
{
    public const string NoUserCode = "auth.no_user";

    public static Error NoUser { get; } = Error.Unauthorized(NoUserCode, "Sign in to continue.");
}
