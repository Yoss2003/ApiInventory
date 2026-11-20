using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices
{
    public interface IEmployeeService : IWorkContainer<Employee>
    {
        Task<List<EmployeeReadDto>> GetEmployeesByCompanyAsync(int companyId);
        Task<EmployeeReadDto?> GetByIdInCompanyAsync(int id, int companyId);
        Task<ServiceResult> CreateInCompanyAsync(EmployeeWriteDto dto, int companyId);
        Task<ServiceResult> UpdateInCompanyAsync(int id, EmployeeWriteDto dto, int companyId);
        Task<ServiceResult> UpdateEmployeeWithUserInCompanyAsync(int id, EmployeeUpdateDTO dto, int companyId);
        Task<ServiceResult> DeleteInCompanyAsync(int id, int companyId);
        Task<(bool Success, string Message)> UpdateEmployeeWithUserAsync(int id, EmployeeUpdateDTO dto);
    }
}