using InventoryAPI.Models;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices
{
    public interface ISupplierService : IWorkContainer<Supplier>
    {
        Task<(bool Success, Supplier? Data, string Message)> ConsultarRucAsync(string ruc);
        Task<ServiceResult> LookupRucWithCacheAsync(string ruc, int companyId);
        Task<ServiceResult> CreateSupplierAsync(Supplier supplier, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> UpdateSupplierAsync(int id, Supplier supplier, int companyId);
        Task<ServiceResult> DeleteSupplierAsync(int id, int companyId);
    }
}