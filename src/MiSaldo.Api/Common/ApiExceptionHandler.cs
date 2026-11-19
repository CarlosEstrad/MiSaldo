using MiSaldo.Application.Common;
using MiSaldo.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MiSaldo.Api.Common;

/// <summary>
/// Turns exceptions that escape a request into Problem Details (RNF-MAN-06).
/// A broken business rule is a 4xx, never a 500 (RNF-USA-05). Unexpected
/// errors get a generic 500 that does not reveal internals.
/// </summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public ApiExceptionHandler(IProblemDetailsService problemDetails) => _problemDetails = problemDetails;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer.
            return true;
        }

        var problem = ToProblem(exception);
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    internal static ProblemDetails ToProblem(Exception exception) => exception switch
    {
        DomainException domain => Create(
            StatusCodes.Status422UnprocessableEntity,
            "A business rule rejected the request.",
            domain.Message,
            "domain." + ErrorCodes.FromTypeName(domain.GetType().Name)),

        // The constraint name stays in the logs; the caller only learns that it already exists.
        DuplicateKeyException => Create(
            StatusCodes.Status409Conflict,
            "The resource already exists.",
            null,
            "conflict.duplicate"),

        ConcurrencyConflictException => Create(
            StatusCodes.Status409Conflict,
            "The resource was changed by another request.",
            "Reload the data and try again.",
            "conflict.concurrency"),

        BadHttpRequestException badRequest => Create(
            badRequest.StatusCode,
            "The request is not valid.",
            null,
            "request.invalid"),

        _ => Create(
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred.",
            null,
            "server.error"),
    };

    private static ProblemDetails Create(int status, string title, string? detail, string code)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        return problem;
    }
}

internal static class ErrorCodes
{
    /// <summary>"NonPositiveAmountException" becomes "non_positive_amount".</summary>
    public static string FromTypeName(string typeName)
    {
        var name = typeName.EndsWith("Exception", StringComparison.Ordinal)
            ? typeName[..^"Exception".Length]
            : typeName;

        var builder = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
