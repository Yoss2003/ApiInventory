using InventoryAPI.Models;
using InventoryAPI.Services;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class MovementsController(IMovementService movementService) : BaseApiController
{
    [HttpGet("master/history")]
    public async Task<IActionResult> MasterHistory(
        [FromQuery] int? inventoryId = null,
        [FromQuery] int? userId = null,
        [FromQuery] int? actorUserId = null,
        [FromQuery] int? actionId = null,
        [FromQuery(Name = "from")] DateTimeOffset? fromDate = null,
        [FromQuery(Name = "to")] DateTimeOffset? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (!EsDesarrollador && !EsAdministrador) return Forbid();
        var companyId = ObtenerEmpresaSegura();
        if (companyId <= 0) return Unauthorized();

        var result = await movementService.GetMasterHistoryAsync(
            companyId, inventoryId, userId, actorUserId, actionId, fromDate, toDate, page, pageSize);
        return FromServiceResult(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetMovements()
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return Ok(await movementService.GetVisibleMovementsAsync(companyId, userId, EsDesarrollador));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetMovement(int id)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        var movement = await movementService.GetVisibleMovementByIdAsync(id, companyId, userId, EsDesarrollador);
        return movement is null ? NotFound() : Ok(movement);
    }

    [HttpPost]
    public async Task<IActionResult> PostMovement([FromBody] Movement movement)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();

        var result = await movementService.CreateAuthorizedMovementAsync(movement, companyId, userId, EsDesarrollador);
        if (result.Status == ServiceStatus.Created && result.Payload is Movement created)
            return CreatedAtAction(nameof(GetMovement), new { id = created.Id }, created);

        return FromServiceResult(result);
    }

    [HttpGet("cancellations")]
    public async Task<IActionResult> GetCancellations()
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return Ok(await movementService.GetVisibleCancellationsAsync(companyId, userId, EsDesarrollador));
    }

    public sealed record CancelMovementRequest(string Reason);

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelMovementRequest body)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await movementService.CancelAuthorizedMovementAsync(id, companyId, userId, EsDesarrollador, body.Reason));
    }
}