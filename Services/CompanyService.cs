using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services
{
    public class CompanyService(IWorkFlow workFlow) : WorkContainer<Company>(workFlow), ICompanyService
    {
        public async Task<IEnumerable<CompanyPublicDTO>> GetActiveCompaniesPublicAsync()
        {
            return await _workFlow.Repository<Company>()
                .Query(asNoTracking: true)
                .Where(c => c.IsActive)
                .Select(c => new CompanyPublicDTO
                {
                    Id = c.Id,
                    BusinessName = c.BusinessName,
                    LogoUrl = c.LogoUrl,
                    PrimaryColorHex = c.PrimaryColorHex
                })
                .ToListAsync();
        }
    }
}