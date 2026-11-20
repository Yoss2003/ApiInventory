using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

[Route("api/company-logos")]
public sealed class CompanyLogosController(ICompanyLogoService logoService) : BaseApiController
{
    private const int MaxBytes = 2 * 1024 * 1024;

    [HttpGet("{companyId:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(int companyId, CancellationToken ct)
    {
        var logo = await logoService.GetActiveLogoAsync(companyId, ct);
        if (logo is null) return NotFound();
        Response.Headers.CacheControl = "public, max-age=300";
        return File(logo.Value.Content, logo.Value.ContentType);
    }

    [HttpPost("{companyId:int}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxBytes + 1024 * 128)]
    public async Task<IActionResult> Upload(int companyId, IFormFile? file, CancellationToken ct)
    {
        if (!EsDesarrollador || ObtenerUsuarioSeguro() <= 0) return Forbid();
        return FromServiceResult(await logoService.UploadLogoAsync(companyId, file, ct));
    }
}