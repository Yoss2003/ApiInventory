using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices
{
    public interface ICustomerService : IWorkContainer<Customer>
    {
        Task<List<CustomerReadDto>> GetCustomersByCompanyAsync(int companyId);
        Task<CustomerReadDto?> GetCustomerByIdAsync(int id, int companyId);
        Task<ServiceResult> CreateCustomerAsync(CustomerWriteDto dto, int companyId);
        Task<ServiceResult> UpdateCustomerAsync(int id, CustomerWriteDto dto, int companyId);
        Task<ServiceResult> DeleteCustomerAsync(int id, int companyId, string deletedBy);
        Task<(bool IsSuccess, string DataOrError)> ConsultarDniExternoAsync(string dni);
    }
}