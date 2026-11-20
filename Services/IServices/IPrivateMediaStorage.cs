namespace InventoryAPI.Services.IServices;

public interface IPrivateMediaStorage
{
    bool IsConfigured { get; }
    Task<string> UploadAsync(string key, IFormFile file, string contentType, CancellationToken ct);
    Task<(byte[] Content, string ContentType)?> DownloadAsync(string reference, CancellationToken ct);
    Task DeleteAsync(string reference, CancellationToken ct);
}