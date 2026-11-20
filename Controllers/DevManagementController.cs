using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

[Route("api/dev")]
public sealed class DevManagementController(IDevCompanyService devCompanyService) : BaseApiController
{
    private bool IsValidDeveloper => EsDesarrollador && ObtenerUsuarioSeguro() > 0;

    [HttpGet("companies")]
    public async Task<IActionResult> List()
    {
        if (!IsValidDeveloper) return Forbid();
        return Ok(await devCompanyService.ListCompaniesAsync());
    }

    [HttpPost("companies")]
    public async Task<IActionResult> Create([FromBody] CompanyInput input)
    {
        if (!IsValidDeveloper) return Forbid();
        return FromServiceResult(await devCompanyService.CreateCompanyAsync(input));
    }

    [HttpPut("companies/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CompanyInput input)
    {
        if (!IsValidDeveloper) return Forbid();
        return FromServiceResult(await devCompanyService.UpdateCompanyAsync(id, input));
    }

    [HttpPost("companies/{id:int}/renew-license")]
    public async Task<IActionResult> RenewLicense(int id, [FromBody] RenewLicenseInput input)
    {
        if (!IsValidDeveloper) return Forbid();
        return FromServiceResult(await devCompanyService.RenewLicenseAsync(id, input));
    }

    [HttpPost("companies/{id:int}/reactivate")]
    public async Task<IActionResult> Reactivate(int id)
    {
        if (!IsValidDeveloper) return Forbid();
        return FromServiceResult(await devCompanyService.ReactivateCompanyAsync(id));
    }

    [HttpDelete("companies/{id:int}")]
    public async Task<IActionResult> Deactivate(int id)
    {
        if (!IsValidDeveloper) return Forbid();
        return FromServiceResult(await devCompanyService.DeactivateCompanyAsync(id, User.FindFirst("UserId")?.Value));
    }
}