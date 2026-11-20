using System.ComponentModel.DataAnnotations;

namespace InventoryAPI.Models.DTO;

public sealed record CustomerReadDto(
    int Id,
    string? DocumentNumber,
    string Name,
    string? Phone,
    string? Email,
    string? Address,
    string? RegistrationDate,
    bool IsActive,
    int CompanyId,
    string Initial);

public sealed class CustomerWriteDto
{
    public int Id { get; set; }

    [StringLength(50)]
    public string? DocumentNumber { get; set; }

    [Required]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;

    [StringLength(50)]
    public string? Phone { get; set; }

    [EmailAddress]
    [StringLength(255)]
    public string? Email { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(255)]
    public string? RegistrationDate { get; set; }
}