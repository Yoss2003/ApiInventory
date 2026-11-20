using InventoryAPI.Security;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class SharedInventoriesController(
    ISharedInventoryService service,
    InventoryAccessPolicy access) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (!TryObtenerAmbito(out var companyId, out _)) return Unauthorized();
        if (!await access.HasAsync(User, InventoryPermissionCodes.ManageInventoryAccess)) return Forbid();

        return Ok(await service.GetAllByCompanyIdAsync(companyId));
    }

    [HttpGet("inventory/{inventoryId:int}")]
    public async Task<IActionResult> GetByInventory(int inventoryId)
    {
        if (!TryObtenerAmbito(out var companyId, out _)) return Unauthorized();
        if (!await access.HasAsync(User, InventoryPermissionCodes.ManageInventoryAccess)) return Forbid();

        return Ok(await service.GetSharedWithUsersAsync(inventoryId, companyId));
    }
}