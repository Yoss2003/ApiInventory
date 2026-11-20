using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Security;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services
{
    public class ProfileService(IWorkFlow workFlow, ISecretProtector secretProtector) : WorkContainer<Profile>(workFlow), IProfileService
    {
        private static ProfileReadDto ToReadDto(Profile p) => new(
            p.Id,
            p.CompanyId,
            p.Username,
            p.LanguageId,
            p.ThemeId,
            p.NotificationId,
            p.DateFormatId,
            p.CurrencyId,
            p.MeasurementUnitId,
            p.TimeZoneId,
            p.SalesModeId,
            p.UseAuthentication,
            p.SharedActivity,
            p.UseBarcodes,
            p.CalculateDevaluation,
            p.GenerateCodes,
            p.ApplyLateFee,
            p.LateFeePercentage,
            p.GraceDays,
            p.SmtpEmail,
            !string.IsNullOrWhiteSpace(p.SmtpPassword),
            p.SmtpApproverEmail,
            p.QrBilletera);

        public async Task<List<ProfileReadDto>> GetProfilesByCompanyAsync(int companyId)
        {
            var profiles = await _workFlow.Repository<Profile>()
                .Query(asNoTracking: true)
                .Where(p => p.CompanyId == companyId)
                .ToListAsync();

            return [.. profiles.Select(ToReadDto)];
        }

        public async Task<ProfileReadDto?> GetProfileByIdAsync(int id, int companyId)
        {
            var profile = await _workFlow.Repository<Profile>()
                .Query(asNoTracking: true)
                .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);

            return profile is null ? null : ToReadDto(profile);
        }

        public async Task<ProfileReadDto?> GetProfileByUsernameAsync(string username, int companyId)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            var normalized = username.Trim().ToLowerInvariant();

            var profile = await _workFlow.Repository<Profile>()
                .Query(asNoTracking: true)
                .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Username != null && p.Username.ToLower() == normalized);

            return profile is null ? null : ToReadDto(profile);
        }

        public async Task<ServiceResult> CreateProfileAsync(ProfileWriteDto dto, int companyId)
        {
            var profile = new Profile
            {
                CompanyId = companyId,
                Username = dto.Username?.Trim() ?? string.Empty,
                LanguageId = dto.LanguageId,
                ThemeId = dto.ThemeId,
                NotificationId = dto.NotificationId,
                DateFormatId = dto.DateFormatId,
                CurrencyId = dto.CurrencyId,
                MeasurementUnitId = dto.MeasurementUnitId,
                TimeZoneId = dto.TimeZoneId,
                SalesModeId = dto.SalesModeId,
                UseAuthentication = dto.UseAuthentication,
                SharedActivity = dto.SharedActivity,
                UseBarcodes = dto.UseBarcodes,
                CalculateDevaluation = dto.CalculateDevaluation,
                GenerateCodes = dto.GenerateCodes,
                ApplyLateFee = dto.ApplyLateFee,
                LateFeePercentage = dto.LateFeePercentage,
                GraceDays = dto.GraceDays,
                SmtpEmail = dto.SmtpEmail?.Trim(),
                SmtpPassword = string.IsNullOrWhiteSpace(dto.SmtpPassword) ? null : secretProtector.Protect(dto.SmtpPassword),
                SmtpApproverEmail = dto.SmtpApproverEmail?.Trim(),
                QrBilletera = dto.QrBilletera
            };

            if (!await CreateAsync(profile))
                return ServiceResult.BadRequest("No se pudo crear el perfil.");

            return ServiceResult.Created(ToReadDto(profile));
        }

        public async Task<ServiceResult> UpdateProfileAsync(int id, ProfileWriteDto dto, int companyId)
        {
            if (id != dto.Id)
                return ServiceResult.BadRequest("El ID no coincide.");

            var existingProfile = await _workFlow.Repository<Profile>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);

            if (existingProfile is null)
                return ServiceResult.NotFound("Registro no encontrado o no pertenece a tu sucursal.");

            existingProfile.LanguageId = dto.LanguageId;
            existingProfile.ThemeId = dto.ThemeId;
            existingProfile.NotificationId = dto.NotificationId;
            existingProfile.DateFormatId = dto.DateFormatId;
            existingProfile.CurrencyId = dto.CurrencyId;
            existingProfile.MeasurementUnitId = dto.MeasurementUnitId;
            existingProfile.TimeZoneId = dto.TimeZoneId;
            existingProfile.SalesModeId = dto.SalesModeId;
            existingProfile.UseAuthentication = dto.UseAuthentication;
            existingProfile.SharedActivity = dto.SharedActivity;
            existingProfile.UseBarcodes = dto.UseBarcodes;
            existingProfile.CalculateDevaluation = dto.CalculateDevaluation;
            existingProfile.GenerateCodes = dto.GenerateCodes;
            existingProfile.ApplyLateFee = dto.ApplyLateFee;
            existingProfile.LateFeePercentage = dto.LateFeePercentage;
            existingProfile.GraceDays = dto.GraceDays;
            existingProfile.SmtpEmail = dto.SmtpEmail?.Trim();
            existingProfile.SmtpApproverEmail = dto.SmtpApproverEmail?.Trim();
            existingProfile.QrBilletera = dto.QrBilletera;

            if (!string.IsNullOrWhiteSpace(dto.SmtpPassword))
                existingProfile.SmtpPassword = secretProtector.Protect(dto.SmtpPassword);

            _workFlow.Repository<Profile>().Update(existingProfile);
            await _workFlow.CompleteAsync();
            return ServiceResult.NoContent();
        }
    }
}