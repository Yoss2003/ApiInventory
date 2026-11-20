using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services;

public sealed class CompanyLogoService(AppDbContext db) : ICompanyLogoService
{
    private const int MaxBytes = 2 * 1024 * 1024;

    public async Task<(byte[] Content, string ContentType)?> GetActiveLogoAsync(int companyId, CancellationToken ct)
    {
        if (companyId <= 0) return null;
        var logo = await db.CompanyLogos.AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                db.Companies.Any(c => c.Id == companyId && c.IsActive))
            .Select(x => new { x.Content, x.ContentType })
            .FirstOrDefaultAsync(ct);

        return logo is null ? null : (logo.Content, logo.ContentType);
    }

    public async Task<ServiceResult> UploadLogoAsync(int companyId, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0 || file.Length > MaxBytes)
            return ServiceResult.BadRequest("Selecciona una imagen PNG, JPG o WebP de hasta 2 MB.");

        var company = await db.Companies.FirstOrDefaultAsync(x => x.Id == companyId, ct);
        if (company is null) return ServiceResult.NotFound();
        if (!company.IsActive)
            return ServiceResult.Conflict("No se permite cambiar el logo de una empresa inactiva.");

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        var bytes = stream.ToArray();
        var detected = DetectImage(bytes);
        if (detected is null || !string.Equals(file.ContentType, detected, StringComparison.OrdinalIgnoreCase))
            return ServiceResult.BadRequest("El archivo no contiene una imagen PNG, JPG o WebP válida.");

        var logo = await db.CompanyLogos.SingleOrDefaultAsync(x => x.CompanyId == companyId, ct);
        if (logo is null)
        {
            logo = new CompanyLogo { CompanyId = companyId };
            db.CompanyLogos.Add(logo);
        }
        logo.Content = bytes;
        logo.ContentType = detected;
        logo.UpdatedAt = DateTime.UtcNow;
        company.LogoUrl = $"/api/company-logos/{companyId}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        await db.SaveChangesAsync(ct);
        return ServiceResult.Ok(new { logoUrl = company.LogoUrl });
    }

    private static string? DetectImage(byte[] data)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 &&
            data[2] == 0x4E && data[3] == 0x47 && data[4] == 0x0D &&
            data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            return "image/png";
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 &&
            data[2] == 0xFF && data[^2] == 0xFF && data[^1] == 0xD9)
            return "image/jpeg";
        if (data.Length >= 12 && data[0] == (byte)'R' && data[1] == (byte)'I' &&
            data[2] == (byte)'F' && data[3] == (byte)'F' &&
            data[8] == (byte)'W' && data[9] == (byte)'E' &&
            data[10] == (byte)'B' && data[11] == (byte)'P')
            return "image/webp";
        return null;
    }
}