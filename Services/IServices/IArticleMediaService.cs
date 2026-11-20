namespace InventoryAPI.Services.IServices;

public sealed record MediaFileResult(
    ServiceResult? Error,
    byte[]? Bytes = null,
    string? PhysicalPath = null,
    string? ContentType = null);

public interface IArticleMediaService
{
    Task<ServiceResult> UploadAsync(int articleId, IFormFile? photo, IFormFile? voucher, int companyId, int userId, bool isDeveloper, CancellationToken ct);
    Task<ServiceResult> DeleteAsync(int articleId, string kind, int companyId, int userId, bool isDeveloper, CancellationToken ct);
    Task<MediaFileResult> DownloadAsync(int articleId, string kind, int companyId, int userId, bool isDeveloper, CancellationToken ct);
}