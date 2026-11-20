using InventoryAPI.Models;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class ActionItemsController(IActionItemService actionItemService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetActions() =>
        Ok(await actionItemService.GetAllAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetActionItem(int id)
    {
        var actionItem = await actionItemService.GetByIdAsync(id);
        return actionItem is null ? NotFound() : Ok(actionItem);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutActionItem(int id, [FromBody] ActionItem actionItem)
    {
        if (!EsDesarrollador) return Forbid();
        if (id != actionItem.Id) return BadRequest(new { error = "El ID no coincide." });
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var existing = await actionItemService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        actionItem.Name = actionItem.Name.Trim();
        var updated = await actionItemService.UpdateAsync(actionItem);
        return updated ? NoContent() : BadRequest(new { error = "No se pudo actualizar el catálogo de acciones." });
    }

    [HttpPost]
    public async Task<IActionResult> PostActionItem([FromBody] ActionItem actionItem)
    {
        if (!EsDesarrollador) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        actionItem.Id = 0;
        actionItem.Name = actionItem.Name.Trim();
        var created = await actionItemService.CreateAsync(actionItem);
        if (!created) return BadRequest(new { error = "No se pudo crear el catálogo de acciones." });

        return CreatedAtAction(nameof(GetActionItem), new { id = actionItem.Id }, actionItem);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteActionItem(int id)
    {
        if (!EsDesarrollador) return Forbid();
        var existing = await actionItemService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        var deleted = await actionItemService.DeleteAsync(id, ObtenerUsuarioSeguro().ToString());
        return deleted ? NoContent() : BadRequest(new { error = "No se pudo eliminar el catálogo de acciones." });
    }
}