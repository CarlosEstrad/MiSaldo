using System.ComponentModel.DataAnnotations;
using MiSaldo.Api.Auth;
using MiSaldo.Api.Common;
using MiSaldo.Application.Categories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace MiSaldo.Api.Categories;

/// <summary>Body of <c>POST /categories</c>.</summary>
public sealed record CreateCategoryRequest([Required] string Name);

/// <summary>Body of <c>PATCH /categories/{id}</c>: rename, reorder or (un)archive; omitted fields stay as they are.</summary>
public sealed record UpdateCategoryRequest(string? Name, int? SortOrder, bool? IsArchived);

/// <summary>Answer of <c>DELETE /categories/{id}</c>.</summary>
public sealed record RemoveCategoryResponse(CategoryRemoval Outcome);

/// <summary>The user's expense categories (RF-CAT-02 to RF-CAT-04). A financial route: needs a verified email.</summary>
[Authorize(Policy = AuthPolicies.VerifiedEmail)]
[Route($"{ApiRoutes.V1}/categories")]
public sealed class CategoriesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromServices] CategoryHandlers handlers, CancellationToken cancellationToken, [FromQuery] bool includeArchived = false)
    {
        var result = await handlers.ListAsync(includeArchived, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPost]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateCategoryRequest request, [FromServices] CategoryHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.CreateAsync(new CreateCategoryCommand(request.Name), cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : Failure(result.Error!);
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id, UpdateCategoryRequest request, [FromServices] CategoryHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.UpdateAsync(
            new UpdateCategoryCommand(id, request.Name, request.SortOrder, request.IsArchived), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    /// <summary>Deletes the category, or archives it when expenses use it (RF-CAT-03).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<RemoveCategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, [FromServices] CategoryHandlers handlers, CancellationToken cancellationToken)
    {
        var result = await handlers.RemoveAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(new RemoveCategoryResponse(result.Value)) : Failure(result.Error!);
    }
}
