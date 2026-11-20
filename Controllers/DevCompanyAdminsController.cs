using InventoryAPI.Models.DTO;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

[Route("api/dev/companies")]
public sealed class DevCompanyAdminsController(IDevCompanyService devCompanyService) : BaseApiController
{
    [HttpPost("{companyId:int}/first-admin")]
    public async Task<IActionResult> CreateFirstAdmin(int companyId, [FromBody] ManagedUserInput input)
    {
        if (!EsDesarrollador) return Forbid();
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return FromServiceResult(await devCompanyService.CreateFirstAdminAsync(companyId, input));
    }
}