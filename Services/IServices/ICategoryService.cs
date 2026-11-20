using InventoryAPI.Models;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices
{
    public interface ICategoryService : IWorkContainer<Category>
    {
        Task<Category[]> GetVisibleCategoriesAsync(int companyId, int userId, bool isDeveloper);
        Task<Category?> GetVisibleCategoryByIdAsync(int id, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> CreateMasterCategoryAsync(Category category, int companyId, int userId, bool isDeveloper, bool allowSimilar);
        Task<ServiceResult> UpdateMasterCategoryAsync(int id, Category category, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> DeleteMasterCategoryAsync(int id, int companyId, int userId, bool isDeveloper);
        Task<(bool Success, string Message)> CreateCategoryAsync(Category category);
        Task<(bool Success, string Message)> UpdateCategoryAsync(int id, Category category);
        Task<(bool Success, string Message)> DeleteCategoryAsync(int id, string deletedBy = "Sistema");
    }
}