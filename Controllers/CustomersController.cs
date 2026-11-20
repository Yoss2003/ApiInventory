using InventoryAPI.Models.DTO;
using InventoryAPI.Security;
using InventoryAPI.Services;
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

            return Ok(await customerService.GetCustomersByCompanyAsync(companyId));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetCustomer(int id)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ViewMaster)) return Forbid();
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            var customer = await customerService.GetCustomerByIdAsync(id, companyId);
            return customer is null ? NotFound() : Ok(customer);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> PutCustomer(int id, [FromBody] CustomerWriteDto customer)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            return FromServiceResult(await customerService.UpdateCustomerAsync(id, customer, companyId));
        }

        [HttpPost]
        public async Task<IActionResult> PostCustomer([FromBody] CustomerWriteDto customer)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            var result = await customerService.CreateCustomerAsync(customer, companyId);
            if (result.Status == ServiceStatus.Created && result.Payload is CustomerReadDto created)
                return CreatedAtAction(nameof(GetCustomer), new { id = created.Id }, created);

            return FromServiceResult(result);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteCustomer(int id)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            return FromServiceResult(await customerService.DeleteCustomerAsync(id, companyId));
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