using System.ComponentModel.DataAnnotations;
using MiSaldo.Api.Common;
using MiSaldo.Application.Profile;
using MiSaldo.Application.Settings;
using MiSaldo.Domain.Accounts;
using MiSaldo.Domain.Periods;
using Microsoft.AspNetCore.Mvc;
namespace MiSaldo.Api.Account;

/// <summary>Body of <c>PUT /profile</c>. Email and birth date cannot be changed here (RF-PER-02).</summary>
public sealed record UpdateProfileRequest([Required] string FullName, [Required] string Country, string? Gender);

/// <summary>Body of <c>PUT /settings</c>: the full set of preferences.</summary>
public sealed record UpdateSettingsRequest(
    [Required] string PrimaryCurrency,
    [Required] string Language,
    [Required] ThemePreference? Theme,
    [Required] PeriodType? DefaultPeriodType,
    [Required] bool? CarryOverEnabled,
    [Required] DayOfWeek? WeekStart,
    [Required] int? BiweeklySecondCycleStartDay,
    [Required] int? MonthlyStartDay);

/// <summary>The signed-in user's profile. Needs a session, not a verified email.</summary>
[Route($"{ApiRoutes.V1}/profile")]
public sealed class ProfileController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ProfileDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromServices] GetProfileHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPut]
    [ProducesResponseType<ProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        UpdateProfileRequest request, [FromServices] UpdateProfileHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new UpdateProfileCommand(request.FullName, request.Country, request.Gender), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
}

/// <summary>The signed-in user's preferences, kept on the server (RF-CFG-02).</summary>
[Route($"{ApiRoutes.V1}/settings")]
public sealed class SettingsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<SettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromServices] GetSettingsHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPut]
    [ProducesResponseType<SettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        UpdateSettingsRequest request, [FromServices] UpdateSettingsHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new UpdateSettingsCommand(
                request.PrimaryCurrency,
                request.Language,
                request.Theme!.Value,
                request.DefaultPeriodType!.Value,
                request.CarryOverEnabled!.Value,
                request.WeekStart!.Value,
                request.BiweeklySecondCycleStartDay!.Value,
                request.MonthlyStartDay!.Value),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
}
