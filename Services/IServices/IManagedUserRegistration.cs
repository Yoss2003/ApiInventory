using InventoryAPI.Models.DTO;

namespace InventoryAPI.Services.IServices;

public interface IManagedUserRegistration
{
    Task<(bool Success, string? Error, object? Created)> RegisterAsync(
        int companyId, ManagedUserInput input, bool firstAdministrator);
}