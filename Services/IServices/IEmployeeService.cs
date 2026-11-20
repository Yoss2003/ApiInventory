using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices
{
    public interface IEmployeeService : IWorkContainer<Employee>
    {
        Task<Employee?> GetByIdInCompanyAsync(int id, int companyId);
        Task<ServiceResult> CreateInCompanyAsync(Employee employee, int companyId);
        Task<ServiceResult> UpdateInCompanyAsync(int id, Employee employee, int companyId);
        Task<ServiceResult> UpdateEmployeeWithUserInCompanyAsync(int id, EmployeeUpdateDTO dto, int companyId);
        Task<ServiceResult> DeleteInCompanyAsync(int id, int companyId, string deletedBy);
        Task<(bool Success, string Message)> UpdateEmployeeWithUserAsync(int id, EmployeeUpdateDTO dto);
    }
}