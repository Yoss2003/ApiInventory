using InventoryAPI.Services;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class InstallmentPaymentsController(
    IInstallmentPaymentService installmentPaymentService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetInstallmentPayments()
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return Ok(await installmentPaymentService.GetVisiblePaymentsAsync(companyId, userId, EsDesarrollador));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetInstallmentPayment(int id)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await installmentPaymentService.GetVisiblePaymentByIdAsync(id, companyId, userId, EsDesarrollador));
    }

    [HttpGet("sale/{saleId:int}")]
    public async Task<IActionResult> GetSaleInstallments(int saleId)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await installmentPaymentService.GetHistoricalSaleInstallmentsAsync(saleId, companyId, userId, EsDesarrollador));
    }

    [HttpPost]
    public async Task<IActionResult> PostInstallmentPayment([FromBody] RecordPaymentRequest request)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        var result = await installmentPaymentService.RecordHistoricalPaymentAsync(request, companyId, userId, EsDesarrollador);
        if (result.Status == ServiceStatus.Created && result.Payload is RecordPaymentResponse created)
            return CreatedAtAction(nameof(GetInstallmentPayment), new { id = created.Id }, created);

        return FromServiceResult(result);
    }

    [HttpPut("{id:int}")]
    public IActionResult PutInstallmentPayment(int id) =>
        Conflict(new { error = "No se permite editar abonos directamente. Registra una reversión auditada." });

    [HttpDelete("{id:int}")]
    public IActionResult DeleteInstallmentPayment(int id) =>
        Conflict(new { error = "No se permite borrar abonos. Registra una reversión auditada." });
}