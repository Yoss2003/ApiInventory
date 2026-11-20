using System.ComponentModel.DataAnnotations;

namespace InventoryAPI.Services.IServices;

public sealed record PasswordRecoveryRequestDto(int CompanyId, [Required] string UsernameOrEmail);
public sealed record PasswordRecoveryCompleteDto([Required] string Token, [Required] string NewPassword);

public interface IPasswordRecoveryService
{
    Task<object> RequestResetAsync(PasswordRecoveryRequestDto input);
    Task<object> GetPendingAsync();
    Task<ServiceResult> ApproveAsync(int id, int approverUserId);
    Task<ServiceResult> RejectAsync(int id);
    Task<ServiceResult> CompleteAsync(PasswordRecoveryCompleteDto input);
}