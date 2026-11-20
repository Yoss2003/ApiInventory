using InventoryAPI.Models;
using InventoryAPI.Security;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers
{
    public class EmployeePermissionsController(
        IEmployeePermissionService service,
        InventoryAccessPolicy policy) : BaseApiController
    {
        [HttpGet("user/{userId:int}")]
        public async Task<IActionResult> GetPermissionsByUser(int userId)
        {
            int companyId = ObtenerEmpresaSegura();
            if (userId != ObtenerUsuarioSeguro() &&
                !await policy.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();

            var permissions = await service.GetByUserIdAndCompanyAsync(userId, companyId);
            return permissions is null ? NotFound() : Ok(permissions);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> PutPermissions(int id, [FromBody] EmployeePermission permissions)
        {
            if (!InventoryAccessPolicy.IsManager(User) ||
                !await policy.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();

            return FromServiceResult(await service.UpdateAuthorizedAsync(id, permissions, ObtenerEmpresaSegura()));
        }
    }
}