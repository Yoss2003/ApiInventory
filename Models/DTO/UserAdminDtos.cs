using System.ComponentModel.DataAnnotations;

namespace InventoryAPI.Models.DTO;

public sealed class UserCreateAdminDto
{
    [Required]
    [StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(12)]
    public string Password { get; set; } = string.Empty;

    public int RoleId { get; set; }
    public int? EmployeeId { get; set; }
}

public sealed class UserUpdateAdminDto
{
    [StringLength(100)]
    public string? Username { get; set; }

    [EmailAddress]
    [StringLength(255)]
    public string? Email { get; set; }

    public int RoleId { get; set; }
    public bool? IsActive { get; set; }
}