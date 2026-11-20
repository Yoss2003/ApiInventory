using InventoryAPI.Models.DTO;
using InventoryAPI.Security;
using InventoryAPI.Services;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers
{
    public class EmployeesController(
        IEmployeeService employeeService,
        InventoryAccessPolicy access) : BaseApiController
    {
        [HttpGet]
        public async Task<IActionResult> GetEmployees()
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ViewUsers)) return Forbid();
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            return Ok(await employeeService.GetEmployeesByCompanyAsync(companyId));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetEmployee(int id)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ViewUsers)) return Forbid();
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            var employee = await employeeService.GetByIdInCompanyAsync(id, companyId);
            return employee is null ? NotFound() : Ok(employee);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> PutEmployee(int id, [FromBody] EmployeeWriteDto employee)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            return FromServiceResult(await employeeService.UpdateInCompanyAsync(id, employee, companyId));
        }

        [HttpPut("{id:int}/UpdateWithUser")]
        public async Task<IActionResult> UpdateEmployeeWithUser(int id, [FromBody] EmployeeUpdateDTO dto)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            return FromServiceResult(await employeeService.UpdateEmployeeWithUserInCompanyAsync(id, dto, companyId));
        }

        [HttpPost]
        public async Task<IActionResult> PostEmployee([FromBody] EmployeeWriteDto employee)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            var result = await employeeService.CreateInCompanyAsync(employee, companyId);
            if (result.Status == ServiceStatus.Created && result.Payload is EmployeeReadDto created)
                return CreatedAtAction(nameof(GetEmployee), new { id = created.Id }, created);

            return FromServiceResult(result);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteEmployee(int id)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();
            if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();

            return FromServiceResult(await employeeService.DeleteInCompanyAsync(id, companyId, userId.ToString()));
        }
    }
}