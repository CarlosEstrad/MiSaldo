namespace MiSaldo.Application.Common;

/// <summary>
/// The kind of failure a use case can report. The API layer maps each kind to an
/// HTTP status; the application layer itself knows nothing about HTTP.
/// </summary>
public enum ErrorType
{
    /// <summary>The input is invalid (maps to 400).</summary>
    Validation,

    /// <summary>The caller is not authenticated or credentials are wrong (maps to 401).</summary>
    Unauthorized,

    /// <summary>The caller is authenticated but not allowed, e.g. unverified email (maps to 403).</summary>
    Forbidden,

    /// <summary>The resource does not exist or belongs to someone else (maps to 404).</summary>
    NotFound,

    /// <summary>The request conflicts with current state, e.g. duplicate name (maps to 409).</summary>
    Conflict,

    /// <summary>A business rule rejected the request (maps to 422).</summary>
    Unprocessable,
}

/// <summary>A failure reported by a use case: a stable code, a message and a kind.</summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Unprocessable(string code, string message) => new(code, message, ErrorType.Unprocessable);
}

/// <summary>The outcome of a use case that returns no value.</summary>
public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);
}

/// <summary>The outcome of a use case that returns a value on success.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error)
        : base(error) => _value = value;

    /// <summary>The value. Reading it from a failed result is a programming error.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    public static Result<T> Success(T value) => new(value, null);

    public static new Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error);
    }
}
