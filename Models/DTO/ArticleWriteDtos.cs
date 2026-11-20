using System.ComponentModel.DataAnnotations;

namespace InventoryAPI.Models.DTO;

public sealed class ArticleWriteDto
{
    [Required]
    public int InventoryId { get; set; }

    [Required]
    public int CategoryId { get; set; }

    [Required]
    public int? BrandId { get; set; }

    [Required]
    [StringLength(100)]
    public string Code { get; set; } = string.Empty;

    [StringLength(150)]
    public string? Barcode { get; set; }

    [Required]
    [StringLength(250)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(150)]
    public string Model { get; set; } = string.Empty;

    [StringLength(150)]
    public string? SerialNumber { get; set; }

    [Required]
    public TrackingMode Tracking { get; set; }

    public string? Presentation { get; set; }
    public int? LocationId { get; set; }
    public int? StatusId { get; set; }
    public int? ConditionId { get; set; }

    [Required]
    public decimal Stock { get; set; }

    [Required]
    [StringLength(50)]
    public string MeasurementUnit { get; set; } = "Unidades";

    [StringLength(50)]
    public string? AcquisitionUnit { get; set; }

    [StringLength(50)]
    public string? SaleUnit { get; set; }

    public decimal? ConversionFactor { get; set; }
    public decimal? AcquisitionPrice { get; set; }

    [StringLength(10)]
    public string? AcquisitionCurrency { get; set; }

    public decimal? SalePrice { get; set; }
    public string? SaleCurrency { get; set; }
    public int? SupplierId { get; set; }
    public int? CurrentEmployeeId { get; set; }
    public int? PreviousEmployeeId { get; set; }

    [StringLength(100)]
    public string? FixedAsset { get; set; }

    public int? UsefulLifeMonths { get; set; }
    public DateTime? WarrantyEndDate { get; set; }
    public DateTime? AcquisitionDate { get; set; }
    public DateTime? DepartureDate { get; set; }
    public DateTime? DecommissionDate { get; set; }

    [Required]
    public int ActionId { get; set; }

    public string? Characteristics { get; set; }
    public string? Observation { get; set; }
    public string? MainPhotoPath { get; set; }
    public string? MainVoucherPath { get; set; }
}

public sealed class ArticleDetailWriteDto
{
    [Required]
    public int ArticleId { get; set; }

    [Required]
    [StringLength(150)]
    public string SerialNumber { get; set; } = string.Empty;

    public string? Attr1 { get; set; }
    public string? Attr2 { get; set; }
    public string? Attr3 { get; set; }
    public string? Attr4 { get; set; }
    public string? Attr5 { get; set; }
    public string? Attr6 { get; set; }
    public int? StatusId { get; set; }
}