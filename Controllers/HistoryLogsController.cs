using InventoryAPI.Models;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class HistoryLogsController(IHistoryLogService historyLogService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetHistoryLogs()
    {
        var companyId = ObtenerEmpresaSegura();
        if (companyId <= 0) return Unauthorized();
        return Ok(await historyLogService.GetAllByCompanyIdAsync(companyId));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetHistoryLog(int id)
    {
        var companyId = ObtenerEmpresaSegura();
        if (companyId <= 0) return Unauthorized();

        var historyLog = await historyLogService.GetByIdInCompanyAsync(id, companyId);
        return historyLog is null ? NotFound() : Ok(historyLog);
    }

    [HttpPost]
    public async Task<IActionResult> PostHistoryLog([FromBody] HistoryLog historyLog)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();

        var success = await historyLogService.CreateInCompanyAsync(historyLog, companyId, userId, EsDesarrollador);
        if (!success) return BadRequest(new { error = "No se pudo registrar el evento de historial." });

        return CreatedAtAction(nameof(GetHistoryLog), new { id = historyLog.Id }, historyLog);
    }
}