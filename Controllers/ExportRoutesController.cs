using InventoryAPI.Models;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class ExportRoutesController(IExportRouteService exportRouteService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetExportRoutes()
    {
        var userId = ObtenerUsuarioSeguro();
        if (userId <= 0) return Unauthorized();
        return Ok(await exportRouteService.GetAllForUserAsync(userId));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetExportRoute(int id)
    {
        var userId = ObtenerUsuarioSeguro();
        if (userId <= 0) return Unauthorized();

        var route = await exportRouteService.GetByIdForUserAsync(id, userId);
        return route is null ? NotFound() : Ok(route);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutExportRoute(int id, [FromBody] ExportRoute exportRoute)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var userId = ObtenerUsuarioSeguro();
        if (userId <= 0) return Unauthorized();
        if (id != exportRoute.Id) return BadRequest(new { error = "El ID de la ruta no coincide." });

        var updated = await exportRouteService.UpdateForUserAsync(id, exportRoute, userId);
        return updated ? NoContent() : NotFound();
    }

    [HttpPost]
    public async Task<IActionResult> PostExportRoute([FromBody] ExportRoute exportRoute)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var userId = ObtenerUsuarioSeguro();
        if (userId <= 0) return Unauthorized();

        var created = await exportRouteService.CreateForUserAsync(exportRoute, userId);
        if (!created) return BadRequest(new { error = "No se pudo crear la ruta de exportación." });

        return CreatedAtAction(nameof(GetExportRoute), new { id = exportRoute.Id }, exportRoute);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteExportRoute(int id)
    {
        var userId = ObtenerUsuarioSeguro();
        if (userId <= 0) return Unauthorized();

        var deleted = await exportRouteService.DeleteForUserAsync(id, userId, userId.ToString());
        return deleted ? NoContent() : NotFound();
    }
}