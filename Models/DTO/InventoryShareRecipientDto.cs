namespace InventoryAPI.Models.DTO;

/// <summary>Cuenta activa y vinculada a un empleado, elegible para recibir acceso a un inventario.</summary>
public sealed record InventoryShareRecipientDto(
    int UserId,
    string Username,
    string? Email,
    string? FirstName,
    string? LastName,
    string? DNI,
    string? PictureUrl);
