using InventoryAPI.Models;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices
{
    public interface IArticleService : IWorkContainer<Article>
    {
        Task<List<Article>> GetVisibleArticlesAsync(int companyId, int userId, bool isDeveloper);
        Task<Article?> GetVisibleArticleByIdAsync(int id, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> CreateArticleAsync(Article article, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> UpdateArticleAsync(int id, Article update, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> DeactivateArticleAsync(int id, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> GetInventoryStockCountAsync(int inventoryId, int companyId, int userId, bool isDeveloper);
        Task<Article?> GetVisibleArticleByBarcodeAsync(string barcode, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> AddDetailAuthorizedAsync(ArticleDetails detail, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> UpdateDetailAuthorizedAsync(int id, ArticleDetails detail, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> DeleteDetailAuthorizedAsync(int id, int companyId, int userId, bool isDeveloper);
        Task<int> GetArticleCountByInventoryIdAsync(int inventoryId);
        Task<Article?> GetArticleByBarcodeAsync(string barcode);
        Task<(bool Success, string ErrorMessage)> AddDetailAsync(ArticleDetails detail);
        Task<bool> UpdateDetailAsync(ArticleDetails detail);
        Task<bool> DeleteDetailAsync(int id);
        Task<bool> UpdateArticleWithAuditAsync(int id, Article updatedArticle, int companyId);
    }
}