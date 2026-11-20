using InventoryAPI.Security;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class AccessController(
    InventoryAccessPolicy permissions,
    IInventoryScopeService inventoryScope) : BaseApiController
{
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();

        var codes = await permissions.GrantedAsync(User);
        return Ok(new
        {
            userId,
            companyId,
            roleId = ObtenerRolSeguro(),
            isDeveloper = EsDesarrollador,
            isAdministrator = EsAdministrador,
            permissions = codes,
            masterInventoryId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId, ct)
        });
    }
}