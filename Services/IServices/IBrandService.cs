using InventoryAPI.Models;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices
{
    public interface IBrandService : IWorkContainer<Brand>
    {
        Task<Brand[]> GetVisibleBrandsAsync(int companyId, int userId, bool isDeveloper);
        Task<Brand?> GetVisibleBrandByIdAsync(int id, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> CreateBrandAsync(Brand brand, int companyId, int userId, bool isDeveloper, bool allowSimilar);
        Task<ServiceResult> UpdateBrandAsync(int id, Brand brand, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> DeleteBrandAsync(int id, int companyId, int userId, bool isDeveloper);
    }
}