namespace InventoryAPI.Services.IServices;

public interface ICompanyLogoService
{
    Task<(byte[] Content, string ContentType)?> GetActiveLogoAsync(int companyId, CancellationToken ct);
    Task<ServiceResult> UploadLogoAsync(int companyId, IFormFile? file, CancellationToken ct);
}