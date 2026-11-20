using System.ComponentModel.DataAnnotations;

namespace InventoryAPI.Models.DTO;

public sealed class SaleCreateDto
{
    public DateTime? SaleDate { get; set; }
    public PaymentType PaymentType { get; set; }
    public int SalesModeId { get; set; }

    [StringLength(255)]
    public string? CustomerName { get; set; }

    [Range(0, double.MaxValue)]
    public decimal TotalAmount { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public decimal? AmountReceived { get; set; }
    public decimal? ChangeGiven { get; set; }
    public string? PaymentGroup { get; set; }
    public string? DigitalWallet { get; set; }
    public SaleInstallmentPlanRequest? InstallmentPlan { get; set; }
    public List<SaleDetailCreateDto> SaleDetails { get; set; } = [];
}

public sealed class SaleDetailCreateDto
{
    public int ArticleId { get; set; }

    [Range(0.0001, double.MaxValue)]
    public decimal Quantity { get; set; }

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }
}