using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

[Route("api/dev/companies")]
public sealed class DevCompanyRucController(IDevCompanyService devCompanyService) : BaseApiController
{
    [HttpGet("lookup-ruc/{ruc}")]
    public async Task<IActionResult> LookupRuc(string ruc, CancellationToken cancellationToken)
    {
        if (!EsDesarrollador || ObtenerUsuarioSeguro() <= 0) return Forbid();
        return FromServiceResult(await devCompanyService.LookupRucAsync(ruc, ObtenerEmpresaSegura(), cancellationToken));
    }
}