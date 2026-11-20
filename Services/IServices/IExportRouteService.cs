using InventoryAPI.Models;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices;

public interface IExportRouteService : IWorkContainer<ExportRoute>
{
    Task<List<ExportRoute>> GetAllForUserAsync(int userId);
    Task<ExportRoute?> GetByIdForUserAsync(int id, int userId);
    Task<bool> CreateForUserAsync(ExportRoute route, int userId);
    Task<bool> UpdateForUserAsync(int id, ExportRoute route, int userId);
    Task<bool> DeleteForUserAsync(int id, int userId, string deletedBy);
}