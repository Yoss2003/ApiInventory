using InventoryAPI.Models;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices
{
    public interface IParametersService : IWorkContainer<Parameters>
    {
        new Task<IEnumerable<Parameters>> GetAllByCompanyIdAsync(int companyId);
        Task<List<Parameters>> GetPublicParametersAsync(int companyId);
        Task<Parameters?> GetPublicParameterByIdAsync(int id, int companyId);
        Task<ServiceResult> UpdateGlobalTokenAsync(int companyId, string newToken);
        Task<ServiceResult> CreateParameterAsync(Parameters input, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> UpdateParameterAsync(int id, Parameters input, int companyId);
        Task<ServiceResult> DeleteParameterAsync(int id, int companyId);
    }
}