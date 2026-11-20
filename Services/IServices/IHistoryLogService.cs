using InventoryAPI.Models;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices;

public interface IHistoryLogService : IWorkContainer<HistoryLog>
{
    Task<HistoryLog?> GetByIdInCompanyAsync(int id, int companyId);
    Task<bool> CreateInCompanyAsync(HistoryLog log, int companyId, int userId);
}