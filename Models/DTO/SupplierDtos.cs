using System.ComponentModel.DataAnnotations;

namespace InventoryAPI.Models.DTO;

public sealed class SupplierWriteDto
{
    public int InventoryId { get; set; }

    [Required]
    [StringLength(20)]
    public string Ruc { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string BusinessName { get; set; } = string.Empty;

    [StringLength(150)]
    public string? ContactName { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [EmailAddress]
    [StringLength(255)]
    public string? Email { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(50)]
    public string? Estado { get; set; }

    [StringLength(50)]
    public string? Condicion { get; set; }

    [StringLength(100)]
    public string? Distrito { get; set; }

    [StringLength(100)]
    public string? Provincia { get; set; }

    [StringLength(100)]
    public string? Departamento { get; set; }

    public int StatusId { get; set; } = 1;

    [StringLength(500)]
    public string? RelationshipDescription { get; set; }

    public string? FiscalDataJson { get; set; }
}