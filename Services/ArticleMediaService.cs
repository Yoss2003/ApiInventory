using InventoryAPI.Data;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services;

public sealed class ArticleMediaService(
    AppDbContext db,
    IWebHostEnvironment environment,
    IPrivateMediaStorage storage,
    IInventoryScopeService inventoryScope,
    ILogger<ArticleMediaService> logger) : IArticleMediaService
{
    private static async Task<bool> ValidSignatureAsync(IFormFile file, bool allowPdf)
    {
        if (file.Length == 0 || file.Length > 10 * 1024 * 1024) return false;
        await using var s = file.OpenReadStream();
        var magic = new byte[12];
        var read = await s.ReadAsync(magic);
        bool jpg = read >= 3 && magic[0] == 0xFF && magic[1] == 0xD8 && magic[2] == 0xFF;
        bool png = read >= 8 && magic.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        bool webp = read >= 12 && System.Text.Encoding.ASCII.GetString(magic, 0, 4) == "RIFF" &&
            System.Text.Encoding.ASCII.GetString(magic, 8, 4) == "WEBP";
        bool pdf = read >= 5 && System.Text.Encoding.ASCII.GetString(magic, 0, 5) == "%PDF-";
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        return (jpg && (ext is ".jpg" or ".jpeg")) || (png && ext == ".png") ||
            (webp && ext == ".webp") || (allowPdf && pdf && ext == ".pdf");
    }

    private static string MediaType(string pathOrExtension)
    {
        var ext = pathOrExtension.StartsWith('.') ? pathOrExtension.ToLowerInvariant() :
            Path.GetExtension(pathOrExtension).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
    }

    public async Task<ServiceResult> UploadAsync(
        int articleId, IFormFile? photo, IFormFile? voucher,
        int companyId, int userId, bool isDeveloper, CancellationToken ct)
    {
        var article = await db.Articles.FirstOrDefaultAsync(a => a.Id == articleId && a.CompanyId == companyId, ct);
        if (article is null) return ServiceResult.NotFound();

        if (!await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true, excludeMaster: false).ContainsAsync(article.InventoryId, ct))
            return ServiceResult.Forbid();

        if (photo is null && voucher is null)
            return ServiceResult.BadRequest("Selecciona al menos un archivo.");
        if (photo is not null && !await ValidSignatureAsync(photo, false))
            return ServiceResult.BadRequest("Fotografía inválida. Usa JPG, PNG o WEBP (máx. 10 MB).");
        if (voucher is not null && !await ValidSignatureAsync(voucher, true))
            return ServiceResult.BadRequest("Comprobante inválido. Usa imagen o PDF (máx. 10 MB).");
        if (!storage.IsConfigured)
            return ServiceResult.ServiceUnavailable("Almacenamiento privado no configurado en la API.");

        var written = new List<string>();
        var oldPhoto = article.MainPhotoPath;
        var oldVoucher = article.MainVoucherPath;
        try
        {
            foreach (var (file, kind) in new[] { (photo, "photo"), (voucher, "voucher") })
            {
                if (file is null) continue;
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                var objectKey = PrivateMediaStorage.NewKey(companyId, "articles", articleId, kind, ext);
                var stored = await storage.UploadAsync(objectKey, file, MediaType(ext), ct);
                written.Add(stored);
                if (kind == "photo") article.MainPhotoPath = stored;
                else article.MainVoucherPath = stored;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo registrar el archivo privado del artículo {ArticleId}", articleId);
            foreach (var uploaded in written)
            {
                try { await storage.DeleteAsync(uploaded, CancellationToken.None); }
                catch (Exception cleanupError) { logger.LogWarning(cleanupError, "Objeto huérfano pendiente de limpiar"); }
            }
            return ServiceResult.BadGateway("No se pudo guardar el archivo en almacenamiento privado.");
        }

        foreach (var old in new[] { photo != null ? oldPhoto : null, voucher != null ? oldVoucher : null })
        {
            if (PrivateMediaStorage.IsCloudReference(old))
            {
                try { await storage.DeleteAsync(old!, CancellationToken.None); }
                catch (Exception ex) { logger.LogWarning(ex, "No se pudo limpiar un objeto anterior del artículo {ArticleId}", articleId); }
            }
        }

        return ServiceResult.Ok(new { article.MainPhotoPath, article.MainVoucherPath });
    }

    public async Task<ServiceResult> DeleteAsync(
        int articleId, string kind, int companyId, int userId, bool isDeveloper, CancellationToken ct)
    {
        if (kind is not ("photo" or "voucher"))
            return ServiceResult.BadRequest("Tipo desconocido.");

        var article = await db.Articles.FirstOrDefaultAsync(a => a.Id == articleId && a.CompanyId == companyId, ct);
        if (article is null) return ServiceResult.NotFound();

        if (!await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true, excludeMaster: false).ContainsAsync(article.InventoryId, ct))
            return ServiceResult.Forbid();

        var previous = kind == "photo" ? article.MainPhotoPath : article.MainVoucherPath;
        if (PrivateMediaStorage.IsCloudReference(previous) && !storage.IsConfigured)
            return ServiceResult.ServiceUnavailable("Almacenamiento privado no configurado en la API.");

        if (kind == "photo") article.MainPhotoPath = null;
        else article.MainVoucherPath = null;
        await db.SaveChangesAsync(ct);

        if (PrivateMediaStorage.IsCloudReference(previous))
        {
            try { await storage.DeleteAsync(previous!, CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "Objeto privado pendiente de limpieza tras borrar medio {ArticleId}", articleId); }
        }
        else if (!string.IsNullOrWhiteSpace(previous))
        {
            var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "PrivateFiles", "articles"));
            var file = Path.GetFullPath(Path.Combine(root, previous.Replace('/', Path.DirectorySeparatorChar)));
            if (file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(file))
                File.Delete(file);
        }

        return ServiceResult.NoContent();
    }

    public async Task<MediaFileResult> DownloadAsync(
        int articleId, string kind, int companyId, int userId, bool isDeveloper, CancellationToken ct)
    {
        if (kind is not ("photo" or "voucher"))
            return new MediaFileResult(ServiceResult.BadRequest("Tipo desconocido."));

        var article = await db.Articles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == articleId && a.CompanyId == companyId, ct);
        if (article is null) return new MediaFileResult(ServiceResult.NotFound());

        if (!await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false, excludeMaster: false).ContainsAsync(article.InventoryId, ct))
            return new MediaFileResult(ServiceResult.Forbid());

        var reference = kind == "photo" ? article.MainPhotoPath : article.MainVoucherPath;
        if (string.IsNullOrWhiteSpace(reference))
            return new MediaFileResult(ServiceResult.NotFound());

        if (PrivateMediaStorage.IsCloudReference(reference))
        {
            if (!storage.IsConfigured)
                return new MediaFileResult(ServiceResult.ServiceUnavailable("Almacenamiento privado no configurado en la API."));
            try
            {
                var remote = await storage.DownloadAsync(reference, ct);
                if (remote is null) return new MediaFileResult(ServiceResult.NotFound());
                return new MediaFileResult(null, Bytes: remote.Value.Content, ContentType: MediaType(Path.GetExtension(reference)));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error consultando medio privado del artículo {ArticleId}", articleId);
                return new MediaFileResult(ServiceResult.BadGateway("No se pudo recuperar el archivo del almacenamiento privado."));
            }
        }

        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "PrivateFiles", "articles"));
        var path = Path.GetFullPath(Path.Combine(root, reference.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path))
            return new MediaFileResult(ServiceResult.NotFound());

        return new MediaFileResult(null, PhysicalPath: path, ContentType: MediaType(Path.GetExtension(path)));
    }
}