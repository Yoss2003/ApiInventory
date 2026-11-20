using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InventoryAPI.Services
{
    public class BrandService(
        IWorkFlow workFlow,
        AppDbContext db,
        IInventoryScopeService inventoryScope) : WorkContainer<Brand>(workFlow), IBrandService
    {
        public async Task<Brand[]> GetVisibleBrandsAsync(int companyId, int userId, bool isDeveloper)
        {
            var allowed = await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false).ToListAsync();
            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            var brands = await GetAllByCompanyIdAsync(companyId);
            return [.. brands.Where(b => allowed.Contains(b.InventoryId) || (masterId.HasValue && b.InventoryId == masterId.Value))];
        }

        public async Task<Brand?> GetVisibleBrandByIdAsync(int id, int companyId, int userId, bool isDeveloper)
        {
            var brand = await GetByIdAsync(id);
            if (brand is null || brand.CompanyId != companyId) return null;

            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (brand.InventoryId == masterId) return brand;

            var hasAccess = await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false)
                .ContainsAsync(brand.InventoryId);
            return hasAccess ? brand : null;
        }

        public async Task<ServiceResult> CreateBrandAsync(Brand brand, int companyId, int userId, bool isDeveloper, bool allowSimilar)
        {
            if (string.IsNullOrWhiteSpace(brand.Name))
                return ServiceResult.BadRequest("Nombre de marca obligatorio.");

            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (!masterId.HasValue)
                return ServiceResult.Conflict("La empresa no tiene inventario maestro activo.");

            var writable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true);
            if (brand.InventoryId != masterId.Value && !await writable.ContainsAsync(brand.InventoryId))
                return ServiceResult.Forbid();
            if (brand.InventoryId == masterId.Value && !await writable.AnyAsync())
                return ServiceResult.Forbid();

            var validCategory = await db.Categories.AnyAsync(c =>
                c.Id == brand.CategoryId && c.InventoryId == masterId.Value &&
                c.CompanyId == companyId && c.IsActive);
            if (!validCategory)
                return ServiceResult.BadRequest("La categoría no pertenece al catálogo maestro.");

            brand.Id = 0;
            brand.CompanyId = companyId;
            brand.InventoryId = masterId.Value;
            brand.IsActive = true;
            brand.Name = brand.Name.Trim();
            brand.NameKey = CatalogNames.Key(brand.Name);
            if (brand.NameKey.Length == 0)
                return ServiceResult.BadRequest("Nombre inválido.");

            var candidates = await db.Brands.AsNoTracking()
                .Where(b => b.CompanyId == companyId && b.CategoryId == brand.CategoryId && b.IsActive)
                .Select(b => new { b.Id, b.Name, b.NameKey })
                .ToListAsync();

            var exact = candidates.FirstOrDefault(b => b.NameKey == brand.NameKey);
            if (exact is not null)
            {
                var previous = await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == exact.Id);
                if (previous is not null)
                {
                    previous.Reused = true;
                    return ServiceResult.Ok(previous);
                }
            }

            var similar = candidates.Where(b => CatalogNames.Similar(b.Name, brand.Name))
                .Take(5).Select(b => new { b.Id, b.Name }).ToList();
            if (similar.Count > 0 && !allowSimilar)
            {
                return ServiceResult.Conflict(
                    "Existe una marca de nombre similar.",
                    new { code = "similar_name", error = "Existe una marca de nombre similar.", matches = similar });
            }

            try
            {
                db.Brands.Add(brand);
                await db.SaveChangesAsync();
                return ServiceResult.Created(brand);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
                pg.SqlState == PostgresErrorCodes.UniqueViolation &&
                pg.ConstraintName == "IX_Brands_Company_Category_NameKey")
            {
                db.Entry(brand).State = EntityState.Detached;
                var existing = await db.Brands.AsNoTracking().FirstOrDefaultAsync(b =>
                    b.CompanyId == companyId && b.CategoryId == brand.CategoryId &&
                    b.NameKey == brand.NameKey && b.IsActive);
                if (existing is null)
                    return ServiceResult.Conflict("Marca duplicada. Recarga el catálogo.");
                existing.Reused = true;
                return ServiceResult.Ok(existing);
            }
        }

        public async Task<ServiceResult> UpdateBrandAsync(int id, Brand brand, int companyId, int userId, bool isDeveloper)
        {
            if (id != brand.Id) return ServiceResult.BadRequest("El ID no coincide.");

            var existing = await GetByIdAsync(id);
            if (existing is null || existing.CompanyId != companyId) return ServiceResult.NotFound();

            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (existing.InventoryId != masterId &&
                !await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true).ContainsAsync(existing.InventoryId))
                return ServiceResult.Forbid();

            if (string.IsNullOrWhiteSpace(brand.Name))
                return ServiceResult.BadRequest("Nombre obligatorio.");

            existing.Name = brand.Name.Trim();
            existing.NameKey = CatalogNames.Key(existing.Name);

            var duplicate = await db.Brands.AnyAsync(b =>
                b.Id != id && b.CompanyId == companyId &&
                b.CategoryId == existing.CategoryId && b.IsActive && b.NameKey == existing.NameKey);
            if (duplicate)
                return ServiceResult.Conflict("Esta marca ya existe.");

            return await UpdateAsync(existing)
                ? ServiceResult.NoContent()
                : ServiceResult.BadRequest("No se pudo actualizar la marca.");
        }

        public async Task<ServiceResult> DeleteBrandAsync(int id, int companyId, int userId, bool isDeveloper)
        {
            var existing = await GetByIdAsync(id);
            if (existing is null || existing.CompanyId != companyId) return ServiceResult.NotFound();

            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (existing.InventoryId != masterId &&
                !await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true).ContainsAsync(existing.InventoryId))
                return ServiceResult.Forbid();

            if (await db.Articles.AnyAsync(a => a.BrandId == id))
                return ServiceResult.Conflict("Esta marca está vinculada a artículos. No puede desactivarse.");

            existing.IsActive = false;
            return await UpdateAsync(existing)
                ? ServiceResult.NoContent()
                : ServiceResult.BadRequest("No se pudo desactivar la marca.");
        }
    }
}