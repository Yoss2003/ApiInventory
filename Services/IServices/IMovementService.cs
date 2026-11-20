using InventoryAPI.Models;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices
{
    public interface IMovementService : IWorkContainer<Movement>
    {
        Task<ServiceResult> GetMasterHistoryAsync(
            int companyId,
            int? inventoryId,
            int? userId,
            int? actorUserId,
            int? actionId,
            DateTimeOffset? fromDate,
            DateTimeOffset? toDate,
            int page,
            int pageSize);
        Task<List<Movement>> GetVisibleMovementsAsync(int companyId, int userId, bool isDeveloper);
        Task<Movement?> GetVisibleMovementByIdAsync(int id, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> CreateAuthorizedMovementAsync(Movement movement, int companyId, int userId, bool isDeveloper);
        Task<object> GetVisibleCancellationsAsync(int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> CancelAuthorizedMovementAsync(int id, int companyId, int userId, bool isDeveloper, string reason);
        Task<(bool Success, string Error)> CancelStockMovementAsync(int originalId, int companyId, int userId, string reason);
    }
}