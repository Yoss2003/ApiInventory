using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services
{
    public class EmployeePermissionService(IWorkFlow workFlow) : WorkContainer<EmployeePermission>(workFlow), IEmployeePermissionService
    {
        public async Task<EmployeePermission?> GetByUserIdAndCompanyAsync(int userId, int companyId)
        {
            return await _workFlow.Repository<EmployeePermission>()
                .Query(asNoTracking: true)
                .FirstOrDefaultAsync(p => p.UserId == userId && p.CompanyId == companyId);
        }

        public async Task<ServiceResult> UpdateAuthorizedAsync(int id, EmployeePermission permissions, int companyId)
        {
            if (id != permissions.Id) return ServiceResult.BadRequest("El ID no coincide.");

            permissions.CompanyId = companyId;
            var existingPerms = await GetByIdAsync(id);
            if (existingPerms is null || existingPerms.CompanyId != companyId) return ServiceResult.NotFound();

            var validTarget = existingPerms.UserId == permissions.UserId &&
                await _workFlow.Repository<User>()
                    .Query(asNoTracking: true)
                    .AnyAsync(u => u.Id == permissions.UserId && u.CompanyId == companyId && u.RoleId > 2);
            if (!validTarget) return ServiceResult.Forbid();

            return await UpdateAsync(permissions)
                ? ServiceResult.NoContent()
                : ServiceResult.BadRequest("No se pudieron actualizar los permisos.");
        }
    }
}