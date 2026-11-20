using InventoryAPI.Models;
using InventoryAPI.Security;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.RegularExpressions;

namespace InventoryAPI.Controllers
{
    public class CustomersController(
        ICustomerService customerService,
        InventoryAccessPolicy access) : BaseApiController
    {
        [HttpGet]
        public async Task<IActionResult> GetCustomers()
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ViewMaster)) return Forbid();
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            return Ok(await customerService.GetAllByCompanyIdAsync(companyId));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetCustomer(int id)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ViewMaster)) return Forbid();
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            var customer = await customerService.GetByIdAsync(id);
            if (customer is null || customer.CompanyId != companyId) return NotFound();

            return Ok(customer);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> PutCustomer(int id, [FromBody] Customer customer)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
            if (id != customer.Id) return BadRequest(new { error = "El ID no coincide." });
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            var existingCustomer = await customerService.GetByIdAsync(id);
            if (existingCustomer is null || existingCustomer.CompanyId != companyId) return NotFound();

            customer.CompanyId = companyId;
            var success = await customerService.UpdateAsync(customer);
            return success ? NoContent() : BadRequest(new { error = "No se pudo actualizar el cliente." });
        }

        [HttpPost]
        public async Task<ActionResult<Customer>> PostCustomer([FromBody] Customer customer)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            customer.Id = 0;
            customer.CompanyId = companyId;

            var success = await customerService.CreateAsync(customer);
            if (!success) return BadRequest(new { error = "No se pudo crear el cliente." });

            return CreatedAtAction(nameof(GetCustomer), new { id = customer.Id }, customer);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteCustomer(int id)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
            if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();

            var existingCustomer = await customerService.GetByIdAsync(id);
            if (existingCustomer is null || existingCustomer.CompanyId != companyId) return NotFound();

            var success = await customerService.DeleteAsync(id, userId.ToString());
            return success ? NoContent() : BadRequest(new { error = "No se pudo eliminar el cliente." });
        }

        [HttpGet("dni/{dni}")]
        [EnableRateLimiting("external")]
        public async Task<IActionResult> ConsultarDniExterno(string dni)
        {
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            var cleanDni = dni?.Trim() ?? string.Empty;
            if (!Regex.IsMatch(cleanDni, @"^\d{8}$"))
                return BadRequest(new { error = "El DNI debe tener exactamente 8 dígitos." });

            var (isSuccess, dataOrError) = await customerService.ConsultarDniExternoAsync(cleanDni);
            if (!isSuccess) return BadRequest(new { error = dataOrError });

            return Content(dataOrError, "application/json");
        }
    }
}