using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices
{
    public interface IInventoryService : IWorkContainer<Inventory>
    {
        Task<List<Inventory>> GetVisibleInventoriesAsync(int companyId, int userId, bool isDeveloper, bool viewMaster);
        Task<List<Inventory>> GetCompanyOperationalAsync(int companyId);
        Task<object> GetMasterInventoriesAsync(int companyId, bool isDeveloper);
        Task<ServiceResult> GetMasterCatalogsAsync(int masterId, int companyId, bool isDeveloper);
        Task<Inventory?> GetVisibleInventoryByIdAsync(int id, int companyId, int userId, bool isDeveloper, bool viewMaster);
        Task<ServiceResult> CreatePersonalInventoryAsync(Inventory inventory, int companyId, int userId);
        Task<ServiceResult> AssignInventoryAsync(int ownerUserId, string inventoryName, string? alias, int companyId);
        Task<ServiceResult> UpdateInventoryAsync(int id, Inventory update, int companyId, int userId, bool isManager);
        Task<ServiceResult> DeactivateInventoryAsync(int id, int companyId, int userId, bool isManager);
        Task<ServiceResult> GetShareRecipientsAuthorizedAsync(int inventoryId, int companyId, int userId, bool isManager);
        Task<ServiceResult> ShareInventoryAuthorizedAsync(ShareRequestDTO request, int companyId, int userId, bool isManager);
        Task<ServiceResult> GetSharedInventoriesAuthorizedAsync(int inventoryId, int companyId, int userId, bool isManager);
        Task<ServiceResult> UpdateShareAccessAsync(int sharedInventoryId, int accessLevel, int companyId, int userId, bool isManager);
        Task<ServiceResult> RevokeAccessAuthorizedAsync(int sharedInventoryId, int companyId, int userId, bool isManager);
        Task<(bool Success, string Message)> ShareInventoryAsync(ShareRequestDTO request);
        Task<IEnumerable<SharedInventory>> GetSharedInventoriesAsync(int inventoryId);
        Task<bool> RevokeAccessAsync(int sharedInventoryId);
    }
}