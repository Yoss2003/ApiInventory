using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InventoryAPI.Services
{
    public class CategoryService(
        IWorkFlow workFlow,
        AppDbContext db,
        IInventoryScopeService inventoryScope) : WorkContainer<Category>(workFlow), ICategoryService
    {
        public override async Task<IEnumerable<Category>> GetAllAsync()
        {
            var categories = await _workFlow.Repository<Category>()
                .Query(asNoTracking: true)
                .Include(c => c.CategoryMeasurementUnits)
                .ToListAsync();

            foreach (var cat in categories)
            {
                if (cat.CategoryMeasurementUnits != null)
                {
                    cat.SelectedUnitIds = [.. cat.CategoryMeasurementUnits.Select(cmu => cmu.MeasurementUnitId)];
                }
            }

            return categories;
        }

        public override async Task<IEnumerable<Category>> GetAllByCompanyIdAsync(int companyId)
        {
            var categories = await _workFlow.Repository<Category>()
                .Query(asNoTracking: true)
                .Include(c => c.CategoryMeasurementUnits)
                .Where(c => c.CompanyId == companyId)
                .ToListAsync();

            foreach (var cat in categories)
            {
                if (cat.CategoryMeasurementUnits != null)
                {
                    cat.SelectedUnitIds = [.. cat.CategoryMeasurementUnits.Select(cmu => cmu.MeasurementUnitId)];
                }
            }

            return categories;
        }

        public async Task<Category[]> GetVisibleCategoriesAsync(int companyId, int userId, bool isDeveloper)
        {
            var ids = await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false).ToListAsync();
            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            var items = await GetAllByCompanyIdAsync(companyId);
            return [.. items.Where(c => ids.Contains(c.InventoryId) || (masterId.HasValue && c.InventoryId == masterId.Value))];
        }

        public async Task<Category?> GetVisibleCategoryByIdAsync(int id, int companyId, int userId, bool isDeveloper)
        {
            var item = await GetByIdAsync(id);
            if (item is null || item.CompanyId != companyId) return null;

            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (item.InventoryId == masterId) return item;

            var allowed = await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false)
                .ContainsAsync(item.InventoryId);
            return allowed ? item : null;
        }

        private async Task<bool> IsParentValidAsync(Category category, int companyId) =>
            category.ParentCategoryId is null ||
            await db.Categories.AnyAsync(c =>
                c.Id == category.ParentCategoryId &&
                c.CompanyId == companyId &&
                c.InventoryId == category.InventoryId &&
                c.ParentCategoryId == null &&
                c.IsActive);

        public async Task<ServiceResult> CreateMasterCategoryAsync(
            Category category, int companyId, int userId, bool isDeveloper, bool allowSimilar)
        {
            if (string.IsNullOrWhiteSpace(category.Name))
                return ServiceResult.BadRequest("El nombre de la categoría es obligatorio.");

            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (!masterId.HasValue)
                return ServiceResult.Conflict("La empresa no tiene inventario maestro activo.");

            var writable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true);
            if (category.InventoryId != masterId.Value && !await writable.ContainsAsync(category.InventoryId))
                return ServiceResult.Forbid();
            if (category.InventoryId == masterId.Value && !await writable.AnyAsync())
                return ServiceResult.Forbid();

            category.InventoryId = masterId.Value;
            if (!await IsParentValidAsync(category, companyId))
                return ServiceResult.BadRequest("La categoría padre debe pertenecer al catálogo maestro.");

            category.Name = category.Name.Trim();
            category.NameKey = CatalogNames.Key(category.Name);
            if (category.NameKey.Length == 0)
                return ServiceResult.BadRequest("Nombre inválido.");

            category.Id = 0;
            category.CompanyId = companyId;
            category.IsActive = true;

            var candidates = await db.Categories.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.IsActive && c.ParentCategoryId == category.ParentCategoryId)
                .Select(c => new { c.Id, c.Name, c.NameKey })
                .ToListAsync();

            var exact = candidates.FirstOrDefault(c => c.NameKey == category.NameKey);
            if (exact != null)
            {
                var previous = await GetByIdAsync(exact.Id);
                if (previous != null)
                {
                    previous.Reused = true;
                    return ServiceResult.Ok(previous);
                }
            }

            var similar = candidates.Where(c => CatalogNames.Similar(c.Name, category.Name))
                .Take(5).Select(c => new { c.Id, c.Name }).ToList();
            if (similar.Count > 0 && !allowSimilar)
            {
                return ServiceResult.Conflict(
                    "Existe una categoría de nombre similar.",
                    new { code = "similar_name", error = "Existe una categoría de nombre similar.", matches = similar });
            }

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                category.CategoryMeasurementUnits = null;
                db.Categories.Add(category);
                await db.SaveChangesAsync();

                if (category.SelectedUnitIds is { Count: > 0 })
                {
                    foreach (var unitId in category.SelectedUnitIds.Distinct())
                    {
                        db.CategoryMeasurementUnits.Add(new CategoryMeasurementUnit
                        {
                            CategoryId = category.Id,
                            MeasurementUnitId = unitId,
                            CompanyId = companyId
                        });
                    }
                    await db.SaveChangesAsync();
                }

                await tx.CommitAsync();
                return ServiceResult.Created(category);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
                pg.SqlState == PostgresErrorCodes.UniqueViolation &&
                (pg.ConstraintName == "IX_Categories_MasterRoot_NameKey" ||
                 pg.ConstraintName == "IX_Categories_MasterChild_NameKey"))
            {
                await tx.RollbackAsync();
                db.ChangeTracker.Clear();
                var existing = await db.Categories.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.IsActive &&
                        c.ParentCategoryId == category.ParentCategoryId && c.NameKey == category.NameKey);
                if (existing is null)
                    return ServiceResult.Conflict("Registro duplicado. Recarga el catálogo.");
                existing.Reused = true;
                return ServiceResult.Ok(existing);
            }
        }

        public async Task<ServiceResult> UpdateMasterCategoryAsync(
            int id, Category category, int companyId, int userId, bool isDeveloper)
        {
            if (id != category.Id) return ServiceResult.BadRequest("El ID no coincide.");

            var old = await GetByIdAsync(id);
            if (old is null || old.CompanyId != companyId) return ServiceResult.NotFound();

            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (old.InventoryId != masterId &&
                !await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true).ContainsAsync(old.InventoryId))
                return ServiceResult.Forbid();

            if (old.InventoryId != category.InventoryId)
                return ServiceResult.Conflict("No se puede mover una categoría de inventario.");

            if (!await IsParentValidAsync(category, companyId))
                return ServiceResult.BadRequest("Grupo padre no válido para este inventario.");

            category.CompanyId = companyId;
            category.Name = category.Name.Trim();
            category.NameKey = CatalogNames.Key(category.Name);
            if (category.NameKey.Length == 0)
                return ServiceResult.BadRequest("Nombre inválido.");

            if (await db.Categories.AnyAsync(c => c.Id != id && c.CompanyId == companyId &&
                c.IsActive && c.ParentCategoryId == category.ParentCategoryId && c.NameKey == category.NameKey))
                return ServiceResult.Conflict("La categoría ya existe en este nivel.");

            var (success, message) = await UpdateCategoryAsync(id, category);
            return success ? ServiceResult.NoContent() : ServiceResult.BadRequest(message);
        }

        public async Task<ServiceResult> DeleteMasterCategoryAsync(
            int id, int companyId, int userId, bool isDeveloper)
        {
            var item = await GetByIdAsync(id);
            if (item is null || item.CompanyId != companyId) return ServiceResult.NotFound();

            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (item.InventoryId != masterId &&
                !await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true).ContainsAsync(item.InventoryId))
                return ServiceResult.Forbid();

            if (await db.Articles.AnyAsync(a => a.CategoryId == id) ||
                await db.Categories.AnyAsync(c => c.ParentCategoryId == id))
                return ServiceResult.Conflict("La categoría tiene artículos o subcategorías vinculadas. No puede desactivarse.");

            var (success, message) = await DeleteCategoryAsync(id, userId.ToString());
            return success ? ServiceResult.NoContent() : ServiceResult.BadRequest(message);
        }

        public async Task<(bool Success, string Message)> CreateCategoryAsync(Category category)
        {
            await _workFlow.BeginTransactionAsync();
            try
            {
                category.CategoryMeasurementUnits = null;

                await _workFlow.Repository<Category>().AddAsync(category);
                await _workFlow.CompleteAsync();

                if (category.SelectedUnitIds != null && category.SelectedUnitIds.Count != 0)
                {
                    foreach (var unitId in category.SelectedUnitIds)
                    {
                        var newUnit = new CategoryMeasurementUnit
                        {
                            CategoryId = category.Id,
                            MeasurementUnitId = unitId,
                            CompanyId = category.CompanyId
                        };
                        await _workFlow.Repository<CategoryMeasurementUnit>().AddAsync(newUnit);
                    }
                    await _workFlow.CompleteAsync();
                }

                await _workFlow.CommitTransactionAsync();
                return (true, "Categoría creada con éxito.");
            }
            catch (Exception ex)
            {
                await _workFlow.RollbackTransactionAsync();
                string detalleReal = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return (false, $"Error de BD: {detalleReal}");
            }
        }

        public async Task<(bool Success, string Message)> UpdateCategoryAsync(int id, Category category)
        {
            var categoriaExistente = await _workFlow.Repository<Category>().GetByIdAsync(id);
            if (categoriaExistente == null) return (false, "Categoría no encontrada.");

            await _workFlow.BeginTransactionAsync();
            try
            {
                category.CategoryMeasurementUnits = null;

                categoriaExistente.Name = category.Name;
                categoriaExistente.NameKey = category.NameKey;
                categoriaExistente.Description = category.Description;
                categoriaExistente.ParentCategoryId = category.ParentCategoryId;
                categoriaExistente.TrackingMode = category.TrackingMode;
                categoriaExistente.NamingMethod = category.NamingMethod;
                categoriaExistente.IsReturnable = category.IsReturnable;
                categoriaExistente.IsActive = category.IsActive;

                categoriaExistente.Label1 = category.Label1;
                categoriaExistente.Label2 = category.Label2;
                categoriaExistente.Label3 = category.Label3;
                categoriaExistente.Label4 = category.Label4;
                categoriaExistente.Label5 = category.Label5;
                categoriaExistente.Label6 = category.Label6;

                categoriaExistente.IsUnique1 = category.IsUnique1;
                categoriaExistente.IsUnique2 = category.IsUnique2;
                categoriaExistente.IsUnique3 = category.IsUnique3;
                categoriaExistente.IsUnique4 = category.IsUnique4;
                categoriaExistente.IsUnique5 = category.IsUnique5;
                categoriaExistente.IsUnique6 = category.IsUnique6;

                categoriaExistente.ModificationDate = DateTime.UtcNow;
                categoriaExistente.ModificationUser = category.ModificationUser ?? "Admin";

                _workFlow.Repository<Category>().Update(categoriaExistente);

                if (category.SelectedUnitIds != null)
                {
                    var unidadesViejas = await _workFlow.Repository<CategoryMeasurementUnit>()
                        .FindAsync(cmu => cmu.CategoryId == id);

                    foreach (var vieja in unidadesViejas)
                    {
                        _workFlow.Repository<CategoryMeasurementUnit>().Delete(vieja);
                    }

                    foreach (var unitId in category.SelectedUnitIds)
                    {
                        var nuevaUnidad = new CategoryMeasurementUnit
                        {
                            CategoryId = id,
                            MeasurementUnitId = unitId,
                            CompanyId = categoriaExistente.CompanyId
                        };
                        await _workFlow.Repository<CategoryMeasurementUnit>().AddAsync(nuevaUnidad);
                    }
                }

                await _workFlow.CompleteAsync();
                await _workFlow.CommitTransactionAsync();
                return (true, "Categoría actualizada con éxito.");
            }
            catch (Exception ex)
            {
                await _workFlow.RollbackTransactionAsync();
                return (false, $"Error crítico al actualizar en BD: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> DeleteCategoryAsync(int id, string deletedBy = "Sistema")
        {
            var category = await _workFlow.Repository<Category>().GetByIdAsync(id);
            if (category == null) return (false, "Categoría no encontrada.");

            var subcategories = await _workFlow.Repository<Category>()
                .FindAsync(c => c.ParentCategoryId == id && c.IsActive);
            if (subcategories.Any())
            {
                return (false, "No puedes eliminar una categoría padre que aún contiene subcategorías.");
            }

            var articles = await _workFlow.Repository<Article>()
                .FindAsync(a => a.CategoryId == id);
            if (articles.Any())
            {
                return (false, "No puedes eliminar esta categoría porque existen artículos registrados en ella.");
            }

            category.IsActive = false;
            category.DeletionDate = DateTime.UtcNow;
            category.DeletionUser = deletedBy;

            _workFlow.Repository<Category>().Update(category);
            await _workFlow.CompleteAsync();

            return (true, "Categoría eliminada con éxito.");
        }
    }
}