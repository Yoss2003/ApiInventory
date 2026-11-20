using InventoryAPI.Models;
using InventoryAPI.Security;
using InventoryAPI.Services;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class BrandsController(
    IBrandService brandService,
    InventoryAccessPolicy access) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetBrands()
    {
        if (!await access.HasAsync(User, InventoryPermissionCodes.ViewMaster)) return Forbid();
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return Ok(await brandService.GetVisibleBrandsAsync(companyId, userId, EsDesarrollador));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetBrand(int id)
    {
        if (!await access.HasAsync(User, InventoryPermissionCodes.ViewMaster)) return Forbid();
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        var brand = await brandService.GetVisibleBrandByIdAsync(id, companyId, userId, EsDesarrollador);
        return brand is null ? NotFound() : Ok(brand);
    }

    [HttpPost]
    public async Task<IActionResult> PostBrand([FromBody] Brand brand, [FromQuery] bool allowSimilar = false)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();

        var result = await brandService.CreateBrandAsync(brand, companyId, userId, EsDesarrollador, allowSimilar);
        if (result.Status == ServiceStatus.Created && result.Payload is Brand created)
            return CreatedAtAction(nameof(GetBrand), new { id = created.Id }, created);

        return FromServiceResult(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutBrand(int id, [FromBody] Brand brand)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();

        var result = await brandService.UpdateBrandAsync(id, brand, companyId, userId, EsDesarrollador);
        return FromServiceResult(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteBrand(int id)
    {
        if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();

        var result = await brandService.DeleteBrandAsync(id, companyId, userId, EsDesarrollador);
        return FromServiceResult(result);
    }
}