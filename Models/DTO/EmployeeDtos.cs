using System.ComponentModel.DataAnnotations;

namespace InventoryAPI.Models.DTO;

public sealed record EmployeeReadDto(
    int Id,
    int? UserId,
    string? FirstName,
    string? LastName,
    string? DNI,
    int? Age,
    string? BirthDate,
    int? JobPositionId,
    int? AreaId,
    string? HireDate,
    int? ContractTypeId,
    int StatusId,
    int CompanyId,
    string? PictureUrl,
    bool IsActive,
    string Initial);

public sealed class EmployeeWriteDto
{
    public int Id { get; set; }
    public int? UserId { get; set; }

    [StringLength(150)]
    public string? FirstName { get; set; }

    [StringLength(150)]
    public string? LastName { get; set; }

    [StringLength(20)]
    public string? DNI { get; set; }

    public int? Age { get; set; }

    [StringLength(50)]
    public string? BirthDate { get; set; }

    public int? JobPositionId { get; set; }
    public int? AreaId { get; set; }

    [StringLength(50)]
    public string? HireDate { get; set; }

    public int? ContractTypeId { get; set; }
    public int StatusId { get; set; }
}