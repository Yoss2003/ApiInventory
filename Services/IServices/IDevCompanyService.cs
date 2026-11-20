using InventoryAPI.Models.DTO;
using System.ComponentModel.DataAnnotations;

namespace InventoryAPI.Services.IServices;

public sealed record CompanyInput(
    [Required, MaxLength(255)] string BusinessName,
    [Required, RegularExpression(@"^\d{11}$")] string Ruc,
    string Plan,
    int MaxUsers,
    DateTime? ExpiresAt,
    bool LicenseEnabled,
    string? PrimaryColorHex,
    string? LogoUrl,
    string? FiscalOwnerName = null,
    string? ContactEmail = null);

public sealed record RenewLicenseInput([Required] string Plan, int? MaxUsers = null);

public interface IDevCompanyService
{
    Task<object> ListCompaniesAsync();
    Task<ServiceResult> CreateCompanyAsync(CompanyInput input);
    Task<ServiceResult> UpdateCompanyAsync(int id, CompanyInput input);
    Task<ServiceResult> RenewLicenseAsync(int id, RenewLicenseInput input);
    Task<ServiceResult> ReactivateCompanyAsync(int id);
    Task<ServiceResult> DeactivateCompanyAsync(int id, string? actorUserId);
    Task<ServiceResult> LookupRucAsync(string ruc, int adminCompanyId, CancellationToken ct);
    Task<ServiceResult> CreateFirstAdminAsync(int companyId, ManagedUserInput input);
}