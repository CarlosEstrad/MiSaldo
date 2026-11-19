using MiSaldo.Api.Auth;
using MiSaldo.Api.Common;
using MiSaldo.Application.Periods;
using MiSaldo.Domain.Periods;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace MiSaldo.Api.Periods;

/// <summary>
/// Body of <c>POST /periods</c>. Every field is optional: without dates, the
/// range comes from the settings around <c>anchorDate</c> (today by default).
/// </summary>
public sealed record CreatePeriodRequest(PeriodType? Type, DateOnly? AnchorDate, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>The user's financial periods (RF-PRD-01 to RF-PRD-06b). A financial route: needs a verified email.</summary>
[Authorize(Policy = AuthPolicies.VerifiedEmail)]
[Route($"{ApiRoutes.V1}/periods")]
public sealed class PeriodsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PeriodDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromServices] PeriodHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.ListAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    /// <summary>The period containing <paramref name="date"/> (today by default). Never creates one (CP-20).</summary>
    [HttpGet("current")]
    [ProducesResponseType<PeriodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Current(
        [FromServices] PeriodHandlers handlers, CancellationToken cancellationToken, [FromQuery] DateOnly? date = null)
    {
        var result = await handlers.GetForDateAsync(date, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    /// <summary>The range a new period would have, so the user can review it before creating it.</summary>
    [HttpGet("proposal")]
    [ProducesResponseType<PeriodProposalDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Proposal(
        [FromServices] PeriodHandlers handlers,
        CancellationToken cancellationToken,
        [FromQuery] PeriodType? type = null,
        [FromQuery] DateOnly? date = null)
    {
        var result = await handlers.ProposeAsync(type, date, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PeriodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, [FromServices] PeriodHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.GetAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPost]
    [ProducesResponseType<PeriodDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        CreatePeriodRequest request, [FromServices] PeriodHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.CreateAsync(
            new CreatePeriodCommand(request.Type, request.AnchorDate, request.StartDate, request.EndDate), cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value)
            : Failure(result.Error!);
    }

    /// <summary>Closes the period, freezing its final balance and applying the carry-over (RF-PRD-06).</summary>
    [HttpPost("{id:guid}/close")]
    [ProducesResponseType<PeriodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Close(Guid id, [FromServices] PeriodHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.CloseAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
}
