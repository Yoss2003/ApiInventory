using InventoryAPI.Models;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class SalesController(ISaleService saleService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetSales()
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return Ok(await saleService.GetVisibleSalesAsync(companyId, userId, EsDesarrollador));
    }

    [HttpPost]
    public async Task<IActionResult> CreateSale([FromBody] Sale sale)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await saleService.CreateAuthorizedSaleAsync(sale, companyId, userId, EsDesarrollador));
    }

    [HttpGet("cancellations")]
    public async Task<IActionResult> Cancellations()
    {
        var companyId = ObtenerEmpresaSegura();
        if (companyId <= 0) return Unauthorized();
        return Ok(await saleService.GetCancellationsAsync(companyId));
    }

    [HttpGet("payment-methods")]
    public IActionResult PaymentMethods() => Ok(saleService.GetPaymentMethods());

    [HttpPost("installments/simulate")]
    public IActionResult SimulateInstallments([FromBody] InstallmentSimulationRequest request) =>
        FromServiceResult(saleService.SimulateInstallments(request));

    [HttpGet("{id:int}/installments")]
    public async Task<IActionResult> GetSaleInstallments(int id)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await saleService.GetSaleInstallmentsAsync(id, companyId, userId, EsDesarrollador));
    }

    [HttpPost("{id:int}/installments/{installmentId:int}/payments")]
    public async Task<IActionResult> PaySaleInstallment(int id, int installmentId, [FromBody] InstallmentReceiptRequest request)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await saleService.PaySaleInstallmentAsync(id, installmentId, request, companyId, userId, EsDesarrollador));
    }

    [HttpPost("quick")]
    public async Task<IActionResult> Quick([FromBody] QuickSaleRequest request)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await saleService.QuickSaleAsync(request, companyId, userId, EsDesarrollador));
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelSaleRequest request)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await saleService.CancelSaleAsync(id, request, companyId, userId, EsDesarrollador));
    }
}