using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

[Route("api/serial-units")]
public sealed class SerialUnitsController(ISerialUnitService serialUnitService) : BaseApiController
{
    private string CurrentUsername => User.Identity?.Name ?? User.FindFirst("Username")?.Value ?? "Usuario";

    private int? CurrentEmployeeId =>
        int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employee) && employee > 0
            ? employee
            : null;

    [HttpGet("{articleId:int}/available")]
    public async Task<IActionResult> AvailableUnits(int articleId)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await serialUnitService.GetAvailableUnitsAsync(articleId, companyId, userId, EsDesarrollador));
    }

    [HttpGet("assignment/{assignmentId:int}/pending")]
    public async Task<IActionResult> PendingAssignmentUnits(int assignmentId)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await serialUnitService.GetPendingAssignmentUnitsAsync(assignmentId, companyId, userId, EsDesarrollador));
    }

    [HttpPost("{articleId:int}")]
    public async Task<IActionResult> AddUnit(int articleId, [FromBody] RegisterUnitRequest input)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await serialUnitService.AddUnitAsync(
            articleId, input, companyId, userId, EsDesarrollador, CurrentUsername));
    }

    [HttpPost("{articleId:int}/batch")]
    public async Task<IActionResult> AddUnitsBatch(int articleId, [FromBody] RegisterUnitBatchRequest input)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await serialUnitService.AddUnitsBatchAsync(
            articleId, input, companyId, userId, EsDesarrollador, CurrentEmployeeId, CurrentUsername));
    }

    [HttpPost("{articleId:int}/decommission")]
    public async Task<IActionResult> DecommissionUnit(int articleId, [FromBody] DecommissionUnitRequest input)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await serialUnitService.DecommissionUnitAsync(
            articleId, input, companyId, userId, EsDesarrollador, CurrentEmployeeId, CurrentUsername));
    }
}