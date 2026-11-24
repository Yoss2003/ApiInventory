namespace InventoryAPI.Models.DTO;

/// <summary>Usuario con acceso concedido a un inventario, junto con su permiso de lectura o edición.</summary>
public sealed record SharedInventoryAccessDto(
    int Id,
    int InventoryId,
    int UserId,
    string? Username,
    string? FirstName,
    string? LastName,
    SharedInventory.AccessMode AccessLevel,
    DateTime SharedDate);
