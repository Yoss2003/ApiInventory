using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices
{
    public interface IUserService : IWorkContainer<User>
    {
        Task<UserDTO?> GetUserDtoByIdAsync(int id, int companyId);
        Task<IEnumerable<UserDTO>> GetUsersDtoByCompanyAsync(int companyId);
        Task<(bool Success, string Message)> UpdateUserAsync(int id, User userActualizado, int companyId);
        Task<(bool Success, object Data, string Message)> CreateUserAsync(User user, int companyId, string contentRootPath);
        Task<(bool Success, User? User, string Message, bool Requires2FA, bool RequirePasswordChange, bool AccountPending)> LoginAsync(LoginRequestDTO request);
        Task<(bool Success, User? User, string Message)> ChangeInitialPasswordAsync(int userId, int companyId, string newPassword);
        Task<(bool Success, string Url, string Message)> UpdatePhotoAsync(int id, int companyId, string base64Image, string contentRootPath);
        Task<(bool Success, string Secret, string QrUri)> Generate2FAAsync(int id, int companyId);
        Task<(bool Success, string Message)> ApproveEmployeeAsync(int id, int companyId);
        Task<(bool Success, string Message)> TestEmailConnectionAsync(string email, string password);
        Task<(bool Success, string Message)> ResendWelcomeEmailAsync(int id, int companyId);
        Task<bool> Enable2FAAsync(int id, int companyId, string code);
        Task<bool> Disable2FAAsync(int id, int companyId);
        Task<bool> UpdateFcmTokenAsync(int userId, int companyId, string fcmToken);
        Task<int> CountActiveAdminsAsync(int companyId);
        Task<string> CreateForcedPasswordResetTokenAsync(int userId, int companyId);
        Task<bool> UserBelongsToCompanyAsync(int userId, int companyId);
        Task<bool> DeleteUserInCompanyAsync(int id, int companyId, string deletedBy = "Sistema");
        Task<int?> GetUserRoleIdInCompanyAsync(int userId, int companyId);
    }
}