namespace InventoryAPI.Models.DTO;

public sealed record ProfileReadDto(
    int Id,
    int CompanyId,
    string? Username,
    int? LanguageId,
    int? ThemeId,
    int? NotificationId,
    int? DateFormatId,
    int? CurrencyId,
    int? MeasurementUnitId,
    int? TimeZoneId,
    int? SalesModeId,
    bool UseAuthentication,
    bool SharedActivity,
    bool UseBarcodes,
    bool CalculateDevaluation,
    bool GenerateCodes,
    bool ApplyLateFee,
    float? LateFeePercentage,
    int? GraceDays,
    string? SmtpEmail,
    bool HasSmtpPassword,
    string? SmtpApproverEmail,
    string? QrBilletera);

public sealed class ProfileWriteDto
{
    public int Id { get; set; }
    public string? Username { get; set; }
    public int? LanguageId { get; set; }
    public int? ThemeId { get; set; }
    public int? NotificationId { get; set; }
    public int? DateFormatId { get; set; }
    public int? CurrencyId { get; set; }
    public int? MeasurementUnitId { get; set; }
    public int? TimeZoneId { get; set; }
    public int? SalesModeId { get; set; }
    public bool UseAuthentication { get; set; }
    public bool SharedActivity { get; set; }
    public bool UseBarcodes { get; set; }
    public bool CalculateDevaluation { get; set; }
    public bool GenerateCodes { get; set; }
    public bool ApplyLateFee { get; set; }
    public float? LateFeePercentage { get; set; }
    public int? GraceDays { get; set; }
    public string? SmtpEmail { get; set; }
    public string? SmtpPassword { get; set; }
    public string? SmtpApproverEmail { get; set; }
    public string? QrBilletera { get; set; }
}