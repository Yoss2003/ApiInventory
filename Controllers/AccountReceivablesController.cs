using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class AccountReceivablesController(
    IAccountReceivableService accountReceivableService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetAccountsReceivables()
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return Ok(await accountReceivableService.GetVisibleByCompanyAsync(companyId, userId, EsDesarrollador));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetAccountReceivable(int id)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await accountReceivableService.GetVisibleByIdAsync(id, companyId, userId, EsDesarrollador));
    }

    [HttpPost]
    public IActionResult PostAccountReceivable() =>
        Conflict(new { error = "Las cuotas se crean con la venta; no se permite generar cuentas por cobrar arbitrarias." });

    [HttpPut("{id:int}")]
    public IActionResult PutAccountReceivable(int id) =>
        Conflict(new { error = "No se permiten modificaciones directas de cuotas. Registra abonos mediante el flujo de cobros." });

    [HttpDelete("{id:int}")]
    public IActionResult DeleteAccountReceivable(int id) =>
        Conflict(new { error = "No se permite borrar cuentas por cobrar. Utiliza el proceso de anulación auditada." });
}