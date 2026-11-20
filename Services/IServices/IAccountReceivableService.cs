using InventoryAPI.Models;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices
{
    public interface IAccountReceivableService : IWorkContainer<AccountReceivable>
    {
        Task<List<AccountReceivable>> GetVisibleByCompanyAsync(int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> GetVisibleByIdAsync(int id, int companyId, int userId, bool isDeveloper);
    }
}