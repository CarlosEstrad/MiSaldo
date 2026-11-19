using MiSaldo.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace MiSaldo.Api.Common;

/// <summary>
/// Base for the v1 controllers: turns a failed use-case <see cref="Result"/>
/// into a Problem Details response with the status of its <see cref="ErrorType"/>
/// and the stable error code, so clients can branch on <c>code</c>.
/// </summary>
// No [Produces] here: it would overwrite the content type of every result and
// turn errors into application/json instead of application/problem+json.
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = ErrorStatus.For(error.Type);
        var factory = HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(HttpContext, statusCode: status, detail: error.Message);
        problem.Extensions["code"] = error.Code;

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }
}

/// <summary>The HTTP status for each kind of use-case failure (design: error mapping).</summary>
public static class ErrorStatus
{
    public static int For(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown error type."),
    };
}
