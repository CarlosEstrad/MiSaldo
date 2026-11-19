using System.ComponentModel.DataAnnotations;
using MiSaldo.Api.Auth;
using MiSaldo.Api.Common;
using MiSaldo.Application.Common;
using MiSaldo.Application.Dashboard;
using MiSaldo.Application.Movements;
using MiSaldo.Application.Periods;
using MiSaldo.Domain.Expenses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace MiSaldo.Api.Movements;

/// <summary>Body of <c>POST /incomes</c>. <c>value</c> is a decimal string such as "1500000" or "12.50".</summary>
public sealed record CreateIncomeRequest(
    [Required] string Description,
    [Required] string Value,
    [Required] DateOnly? Date,
    [Required] string Source,
    bool Received,
    DateOnly? ReceivedDate);

/// <summary>Body of <c>PUT /incomes/{id}</c>.</summary>
public sealed record UpdateIncomeRequest([Required] string Description, [Required] string Value, [Required] DateOnly? Date, [Required] string Source);

/// <summary>Body of <c>POST /incomes/{id}/receive</c>; without a date, today is used.</summary>
public sealed record MarkReceivedRequest(DateOnly? ReceivedDate);

/// <summary>Body of <c>POST /expenses</c> and <c>PUT /expenses/{id}</c>.</summary>
public sealed record ExpenseRequest(
    [Required] string Name,
    [Required] Guid? CategoryId,
    [Required] string Budgeted,
    [Required] DateOnly? Date,
    [Required] ExpenseType? Type);

/// <summary>Body of <c>POST /expenses/{id}/payments</c>.</summary>
public sealed record PaymentRequest([Required] string Value, [Required] DateOnly? Date);

/// <summary>Resolves the date range of a listing: a period, an explicit range, or the period of today.</summary>
internal static class MovementRange
{
    public static async Task<Result<(DateOnly From, DateOnly To)>> ResolveAsync(
        PeriodHandlers periods, Guid? periodId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (from is { } start && to is { } end)
        {
            return Result.Success((start, end));
        }

        if (from is not null || to is not null)
        {
            return Result.Failure<(DateOnly, DateOnly)>(Error.Validation(
                PeriodHandlers.InvalidRangeCode, "Send both from and to, a periodId, or nothing for the current period."));
        }

        var period = periodId is { } id
            ? await periods.GetAsync(id, cancellationToken)
            : await periods.GetForDateAsync(null, cancellationToken);
        return period.IsSuccess
            ? Result.Success((period.Value.StartDate, period.Value.EndDate))
            : Result.Failure<(DateOnly, DateOnly)>(period.Error!);
    }
}

/// <summary>The user's incomes (RF-ING-01 to RF-ING-05). A financial route: needs a verified email.</summary>
[Authorize(Policy = AuthPolicies.VerifiedEmail)]
[Route($"{ApiRoutes.V1}/incomes")]
public sealed class IncomesController : ApiControllerBase
{
    /// <summary>Incomes of a period (<c>periodId</c>), of a range (<c>from</c> and <c>to</c>), or of the current period.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<IncomeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromServices] IncomeHandlers handlers,
        [FromServices] PeriodHandlers periods,
        CancellationToken cancellationToken,
        [FromQuery] Guid? periodId = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        var range = await MovementRange.ResolveAsync(periods, periodId, from, to, cancellationToken);
        if (range.IsFailure)
        {
            return Failure(range.Error!);
        }

        var result = await handlers.ListAsync(range.Value.From, range.Value.To, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<IncomeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, [FromServices] IncomeHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.GetAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPost]
    [ProducesResponseType<IncomeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        CreateIncomeRequest request, [FromServices] IncomeHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.CreateAsync(
            new CreateIncomeCommand(request.Description, request.Value, request.Date!.Value, request.Source, request.Received, request.ReceivedDate),
            cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value)
            : Failure(result.Error!);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<IncomeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, UpdateIncomeRequest request, [FromServices] IncomeHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.UpdateAsync(
            new UpdateIncomeCommand(id, request.Description, request.Value, request.Date!.Value, request.Source), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    /// <summary>Marks the income as received (RF-ING-03).</summary>
    [HttpPost("{id:guid}/receive")]
    [ProducesResponseType<IncomeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> MarkReceived(
        Guid id, MarkReceivedRequest? request, [FromServices] IncomeHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.MarkReceivedAsync(id, request?.ReceivedDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(Guid id, [FromServices] IncomeHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.DeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }
}

/// <summary>The user's expenses and their payments (RF-GAS-01 to RF-GAS-06). A financial route: needs a verified email.</summary>
[Authorize(Policy = AuthPolicies.VerifiedEmail)]
[Route($"{ApiRoutes.V1}/expenses")]
public sealed class ExpensesController : ApiControllerBase
{
    /// <summary>Header that makes a payment request safe to retry (RNF-DAT-03).</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Response header set when a payment was not created again because its key was already used.</summary>
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>Expenses of a period (<c>periodId</c>), of a range (<c>from</c> and <c>to</c>), or of the current period.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ExpenseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromServices] ExpenseHandlers handlers,
        [FromServices] PeriodHandlers periods,
        CancellationToken cancellationToken,
        [FromQuery] Guid? periodId = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        var range = await MovementRange.ResolveAsync(periods, periodId, from, to, cancellationToken);
        if (range.IsFailure)
        {
            return Failure(range.Error!);
        }

        var result = await handlers.ListAsync(range.Value.From, range.Value.To, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ExpenseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, [FromServices] ExpenseHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.GetAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPost]
    [ProducesResponseType<ExpenseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(ExpenseRequest request, [FromServices] ExpenseHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.CreateAsync(
            new CreateExpenseCommand(request.Name, request.CategoryId!.Value, request.Budgeted, request.Date!.Value, request.Type!.Value),
            cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value)
            : Failure(result.Error!);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ExpenseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, ExpenseRequest request, [FromServices] ExpenseHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.UpdateAsync(
            new UpdateExpenseCommand(id, request.Name, request.CategoryId!.Value, request.Budgeted, request.Date!.Value, request.Type!.Value),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    /// <summary>Deletes an expense without payments; one with payments answers 409 (RN-M08).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, [FromServices] ExpenseHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.DeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    /// <summary>
    /// Registers a payment (RF-GAS-02). With an <c>Idempotency-Key</c> header, a
    /// repeated request returns the first payment with 200 instead of a new one (CP-12).
    /// </summary>
    [HttpPost("{id:guid}/payments")]
    [ProducesResponseType<PaymentResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<PaymentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddPayment(
        Guid id,
        PaymentRequest request,
        [FromServices] ExpenseHandlers handlers,
        CancellationToken cancellationToken,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey = null)
    {
        var result = await handlers.AddPaymentAsync(
            new AddPaymentCommand(id, request.Value, request.Date!.Value, idempotencyKey), cancellationToken);
        if (result.IsFailure)
        {
            return Failure(result.Error!);
        }

        if (result.Value.Replayed)
        {
            Response.Headers[ReplayedHeader] = "true";
            return Ok(result.Value);
        }

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }
}

/// <summary>The indicators of a period (RF-DSH-02, R13). A financial route: needs a verified email.</summary>
[Authorize(Policy = AuthPolicies.VerifiedEmail)]
[Route($"{ApiRoutes.V1}/dashboard")]
public sealed class DashboardController : ApiControllerBase
{
    /// <summary>The dashboard of <c>periodId</c>, or of the period containing today.</summary>
    [HttpGet]
    [ProducesResponseType<DashboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromServices] GetDashboardHandler handler, CancellationToken cancellationToken, [FromQuery] Guid? periodId = null)
    {
        var result = await handler.HandleAsync(periodId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
}
