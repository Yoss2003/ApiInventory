using InventoryAPI.Models;
using InventoryAPI.Security;
using InventoryAPI.Services;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class CategoriesController(
    ICategoryService categoryService,
    InventoryAccessPolicy access) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetCategories()
    {
        if (!await access.HasAsync(User, InventoryPermissionCodes.ViewMaster)) return Forbid();
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return Ok(await categoryService.GetVisibleCategoriesAsync(companyId, userId, EsDesarrollador));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCategory(int id)
    {
        if (!await access.HasAsync(User, InventoryPermissionCodes.ViewMaster)) return Forbid();
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        var item = await categoryService.GetVisibleCategoryByIdAsync(id, companyId, userId, EsDesarrollador);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> PostCategory([FromBody] Category category, [FromQuery] bool allowSimilar = false)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();

        var result = await categoryService.CreateMasterCategoryAsync(category, companyId, userId, EsDesarrollador, allowSimilar);
        if (result.Status == ServiceStatus.Created && result.Payload is Category created)
            return CreatedAtAction(nameof(GetCategory), new { id = created.Id }, created);

        return FromServiceResult(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutCategory(int id, [FromBody] Category category)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();

        var result = await categoryService.UpdateMasterCategoryAsync(id, category, companyId, userId, EsDesarrollador);
        return FromServiceResult(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();

        var result = await categoryService.DeleteMasterCategoryAsync(id, companyId, userId, EsDesarrollador);
        return FromServiceResult(result);
    }
}