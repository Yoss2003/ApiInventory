using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Security;
using InventoryAPI.Services;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class InventoriesController(
    IInventoryService inventoryService,
    InventoryAccessPolicy permissions) : BaseApiController
{
    private bool IsManager => EsDesarrollador || EsAdministrador;

    [HttpGet]
    public async Task<IActionResult> GetInventories()
    {
        if (!TryObtenerAmbito(out var company, out var user)) return Unauthorized();
        var viewMaster = await permissions.HasAsync(User, InventoryPermissionCodes.ViewMaster);
        return Ok(await inventoryService.GetVisibleInventoriesAsync(company, user, EsDesarrollador, viewMaster));
    }

    [HttpGet("company-operational")]
    public async Task<IActionResult> CompanyOperational()
    {
        if (!TryObtenerAmbito(out var company, out _)) return Unauthorized();
        if (!await permissions.HasAsync(User, InventoryPermissionCodes.ManageInventoryAccess)) return Forbid();
        return Ok(await inventoryService.GetCompanyOperationalAsync(company));
    }

    [HttpGet("masters")]
    public async Task<IActionResult> GetMasterInventories()
    {
        if (!TryObtenerAmbito(out var company, out _)) return Unauthorized();
        if (!EsDesarrollador) return Forbid();
        return Ok(await inventoryService.GetMasterInventoriesAsync(company, EsDesarrollador));
    }

    [HttpGet("masters/{masterId:int}/catalogs")]
    public async Task<IActionResult> GetMasterCatalogs(int masterId)
    {
        if (!TryObtenerAmbito(out var companyId, out _)) return Unauthorized();
        if (!EsDesarrollador) return Forbid();
        return FromServiceResult(await inventoryService.GetMasterCatalogsAsync(masterId, companyId, EsDesarrollador));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetInventory(int id)
    {
        if (!TryObtenerAmbito(out var company, out var user)) return Unauthorized();
        var viewMaster = await permissions.HasAsync(User, InventoryPermissionCodes.ViewMaster);
        var item = await inventoryService.GetVisibleInventoryByIdAsync(id, company, user, EsDesarrollador, viewMaster);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> PostInventory([FromBody] Inventory inventory)
    {
        if (!TryObtenerAmbito(out var company, out var user)) return Unauthorized();
        if (!await permissions.HasAsync(User, InventoryPermissionCodes.CreateInventory)) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await inventoryService.CreatePersonalInventoryAsync(inventory, company, user);
        if (result.Status == ServiceStatus.Created && result.Payload is Inventory created)
            return CreatedAtAction(nameof(GetInventory), new { id = created.Id }, created);

        return FromServiceResult(result);
    }

    public sealed record AssignedInventoryInput(int OwnerUserId, string InventoryName, string? Alias);

    [HttpPost("assign")]
    public async Task<IActionResult> AssignInventory([FromBody] AssignedInventoryInput request)
    {
        if (!TryObtenerAmbito(out var company, out _)) return Unauthorized();
        if (!IsManager) return Forbid();
        if (!await permissions.HasAsync(User, InventoryPermissionCodes.CreateInventory) ||
            !await permissions.HasAsync(User, InventoryPermissionCodes.ManageInventoryAccess))
            return Forbid();

        var result = await inventoryService.AssignInventoryAsync(request.OwnerUserId, request.InventoryName, request.Alias, company);
        return FromServiceResult(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutInventory(int id, [FromBody] Inventory update)
    {
        if (!TryObtenerAmbito(out var company, out var user)) return Unauthorized();
        if (!await permissions.HasAsync(User, InventoryPermissionCodes.CreateInventory)) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        return FromServiceResult(await inventoryService.UpdateInventoryAsync(id, update, company, user, IsManager));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteInventory(int id)
    {
        if (!TryObtenerAmbito(out var company, out var user)) return Unauthorized();
        if (!await permissions.HasAsync(User, InventoryPermissionCodes.CreateInventory)) return Forbid();

        return FromServiceResult(await inventoryService.DeactivateInventoryAsync(id, company, user, IsManager));
    }

    [HttpGet("{inventoryId:int}/share-recipients")]
    public async Task<IActionResult> GetShareRecipients(int inventoryId)
    {
        if (!TryObtenerAmbito(out var company, out var user)) return Unauthorized();
        if (!await permissions.HasAsync(User, InventoryPermissionCodes.ManageInventoryAccess)) return Forbid();

        return FromServiceResult(await inventoryService.GetShareRecipientsAuthorizedAsync(inventoryId, company, user, IsManager));
    }

    [HttpPost("Share")]
    public async Task<IActionResult> ShareInventory([FromBody] ShareRequestDTO request)
    {
        if (!TryObtenerAmbito(out var company, out var user)) return Unauthorized();
        if (!await permissions.HasAsync(User, InventoryPermissionCodes.ManageInventoryAccess)) return Forbid();

        return FromServiceResult(await inventoryService.ShareInventoryAuthorizedAsync(request, company, user, IsManager));
    }

    [HttpGet("{inventoryId:int}/Shared")]
    public async Task<IActionResult> GetSharedInventories(int inventoryId)
    {
        if (!TryObtenerAmbito(out var company, out var user)) return Unauthorized();
        if (!await permissions.HasAsync(User, InventoryPermissionCodes.ManageInventoryAccess)) return Forbid();

        return FromServiceResult(await inventoryService.GetSharedInventoriesAuthorizedAsync(inventoryId, company, user, IsManager));
    }

    public sealed record UpdateSharedAccessRequest(int AccessLevel);

    [HttpPut("Share/{sharedInventoryId:int}/Access")]
    public async Task<IActionResult> UpdateShareAccess(int sharedInventoryId, [FromBody] UpdateSharedAccessRequest request)
    {
        if (!TryObtenerAmbito(out var company, out var user)) return Unauthorized();
        if (!await permissions.HasAsync(User, InventoryPermissionCodes.ManageInventoryAccess)) return Forbid();

        return FromServiceResult(await inventoryService.UpdateShareAccessAsync(sharedInventoryId, request.AccessLevel, company, user, IsManager));
    }

    [HttpDelete("Revoke/{sharedInventoryId:int}")]
    public async Task<IActionResult> RevokeAccess(int sharedInventoryId)
    {
        if (!TryObtenerAmbito(out var company, out var user)) return Unauthorized();
        if (!await permissions.HasAsync(User, InventoryPermissionCodes.ManageInventoryAccess)) return Forbid();

        return FromServiceResult(await inventoryService.RevokeAccessAuthorizedAsync(sharedInventoryId, company, user, IsManager));
    }
}