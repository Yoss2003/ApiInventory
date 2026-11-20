using InventoryAPI.Models;
using InventoryAPI.Security;
using InventoryAPI.Services;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers
{
    public class SuppliersController(
        ISupplierService supplierService,
        InventoryAccessPolicy access) : BaseApiController
    {
        [HttpGet("ruc/{ruc}")]
        public async Task<IActionResult> ConsultarRuc(string ruc)
        {
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();
            var result = await supplierService.LookupRucWithCacheAsync(ruc, companyId);
            return FromServiceResult(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetSuppliers()
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ViewMaster)) return Forbid();
            return Ok(await supplierService.GetAllByCompanyIdAsync(ObtenerEmpresaSegura()));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetSupplier(int id)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ViewMaster)) return Forbid();
            var companyId = ObtenerEmpresaSegura();
            var supplier = await supplierService.GetByIdAsync(id);
            if (supplier == null || supplier.CompanyId != companyId) return NotFound();
            return Ok(supplier);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> PutSupplier(int id, [FromBody] Supplier supplier)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            var result = await supplierService.UpdateSupplierAsync(id, supplier, companyId);
            return FromServiceResult(result);
        }

        [HttpPost]
        public async Task<IActionResult> PostSupplier([FromBody] Supplier supplier)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
            if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();

            var result = await supplierService.CreateSupplierAsync(supplier, companyId, userId, EsDesarrollador);
            if (result.Status == ServiceStatus.Created && result.Payload is Supplier created)
                return CreatedAtAction(nameof(GetSupplier), new { id = created.Id }, created);

            return FromServiceResult(result);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteSupplier(int id)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            var result = await supplierService.DeleteSupplierAsync(id, companyId);
            return FromServiceResult(result);
        }
    }
}