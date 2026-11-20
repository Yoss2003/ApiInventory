using InventoryAPI.Models;
using InventoryAPI.Security;
using InventoryAPI.Services;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class ParametersController(
    IParametersService parametersService,
    InventoryAccessPolicy access) : BaseApiController
{
    [HttpPut("GlobalToken")]
    public async Task<IActionResult> UpdateGlobalToken([FromBody] string newToken)
    {
        var companyId = ObtenerEmpresaSegura();
        if (companyId != 1 || !EsDesarrollador) return Forbid();
        if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();

        var result = await parametersService.UpdateGlobalTokenAsync(companyId, newToken);
        return FromServiceResult(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetParameters()
    {
        if (!await access.HasAsync(User, InventoryPermissionCodes.ViewMaster)) return Forbid();
        return Ok(await parametersService.GetPublicParametersAsync(ObtenerEmpresaSegura()));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetParameter(int id)
    {
        if (!await access.HasAsync(User, InventoryPermissionCodes.ViewMaster)) return Forbid();
        var item = await parametersService.GetPublicParameterByIdAsync(id, ObtenerEmpresaSegura());
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> PostParameter([FromBody] Parameters input)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();

        var result = await parametersService.CreateParameterAsync(input, companyId, userId, EsDesarrollador);
        if (result.Status == ServiceStatus.Created && result.Payload is Parameters created)
            return CreatedAtAction(nameof(GetParameter), new { id = created.Id }, created);

        return FromServiceResult(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutParameter(int id, [FromBody] Parameters input)
    {
        if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await parametersService.UpdateParameterAsync(id, input, ObtenerEmpresaSegura());
        return FromServiceResult(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteParameter(int id)
    {
        if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
        var result = await parametersService.DeleteParameterAsync(id, ObtenerEmpresaSegura());
        return FromServiceResult(result);
    }
}