using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

[Route("api/Articles")]
public sealed class ArticleMediaController(IArticleMediaService mediaService) : BaseApiController
{
    [HttpPost("{id:int}/media")]
    [RequestSizeLimit(22 * 1024 * 1024)]
    public async Task<IActionResult> Upload(int id, IFormFile? photo, IFormFile? voucher, CancellationToken ct)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await mediaService.UploadAsync(id, photo, voucher, companyId, userId, EsDesarrollador, ct));
    }

    [HttpDelete("{id:int}/media/{kind}")]
    public async Task<IActionResult> Delete(int id, string kind, CancellationToken ct)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await mediaService.DeleteAsync(id, kind, companyId, userId, EsDesarrollador, ct));
    }

    [HttpGet("{id:int}/media/{kind}")]
    public async Task<IActionResult> Download(int id, string kind, CancellationToken ct)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        var result = await mediaService.DownloadAsync(id, kind, companyId, userId, EsDesarrollador, ct);
        if (result.Error is not null) return FromServiceResult(result.Error);

        Response.Headers.CacheControl = "private, no-store";
        if (result.Bytes is not null)
            return File(result.Bytes, result.ContentType!, enableRangeProcessing: true);

        return PhysicalFile(result.PhysicalPath!, result.ContentType!, enableRangeProcessing: true);
    }
}