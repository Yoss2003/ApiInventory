using System.ComponentModel.DataAnnotations;

namespace InventoryAPI.Services.IServices;

public sealed record UserDetailsUpdate(
    [Required, MinLength(3), MaxLength(80)] string Username,
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MaxLength(100)] string FirstName,
    [Required, MaxLength(100)] string LastName,
    [RegularExpression(@"^\d{8}$")] string? Dni);

public sealed record UserPermissionUpdate(
    bool CanApplyDiscounts,
    bool CanViewPurchaseCosts,
    bool CanCancelSales,
    bool CanEditProducts,
    bool CanViewProfits);

public sealed record ResendCredentialsInput([EmailAddress] string? CorrectedEmail);

public sealed record UserAccessUpdate(bool IsActive, int? RoleId);

public interface IUserManagementService
{
    Task<object> ListUsersAsync(int companyId);
    Task<object> GetAssignableRolesAsync();
    Task<ServiceResult> UpdateDetailsAsync(int id, UserDetailsUpdate input, int companyId, bool isDeveloper);
    Task<ServiceResult> GetUserPermissionsAsync(int id, int companyId);
    Task<ServiceResult> UpdateUserPermissionsAsync(int id, UserPermissionUpdate input, int companyId, int actorUserId);
    Task<ServiceResult> ResendCredentialsAsync(int id, ResendCredentialsInput input, int companyId, int actorUserId, bool isDeveloper);
    Task<ServiceResult> UpdateAccessAsync(int id, UserAccessUpdate input, int companyId, int actorUserId, Func<int, Task<bool>> canAssignRoleAsync);
}