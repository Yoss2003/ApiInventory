using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services
{
    public class ParametersService(
        IWorkFlow workFlow,
        AppDbContext db,
        IInventoryScopeService inventoryScope) : WorkContainer<Parameters>(workFlow), IParametersService
    {
        private static bool IsSecret(Parameters parameter)
        {
            var type = (parameter.ParameterType ?? "").ToUpperInvariant();
            var name = (parameter.Name ?? "").ToUpperInvariant();
            return new[] { "API_KEY", "TOKEN", "PASSWORD", "SMTP", "SECRET", "CREDENTIAL", "AUTH_KEY" }
                .Any(part => type.Contains(part, StringComparison.Ordinal) ||
                             name.Contains(part, StringComparison.Ordinal));
        }

        public new async Task<IEnumerable<Parameters>> GetAllByCompanyIdAsync(int companyId)
        {
            return await _workFlow.Parameters.FindAsync(p => p.CompanyId == companyId || p.CompanyId == 1);
        }

        public async Task<List<Parameters>> GetPublicParametersAsync(int companyId)
        {
            var all = await db.Parameters.AsNoTracking()
                .Where(p => p.CompanyId == companyId)
                .ToListAsync();
            return [.. all.Where(p => !IsSecret(p))];
        }

        public async Task<Parameters?> GetPublicParameterByIdAsync(int id, int companyId)
        {
            var item = await db.Parameters.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);
            return item is null || IsSecret(item) ? null : item;
        }

        public async Task<ServiceResult> UpdateGlobalTokenAsync(int companyId, string newToken)
        {
            if (string.IsNullOrWhiteSpace(newToken))
                return ServiceResult.BadRequest("Token vacío.");

            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (!masterId.HasValue)
                return ServiceResult.Conflict("Inventario maestro no encontrado.");

            var item = await db.Parameters.FirstOrDefaultAsync(p =>
                p.CompanyId == 1 && p.ParameterType == "API_KEY" && p.Name == "Decolecta_Token");

            if (item is null)
            {
                db.Parameters.Add(new Parameters
                {
                    CompanyId = 1,
                    InventoryId = masterId.Value,
                    ParameterType = "API_KEY",
                    Name = "Decolecta_Token",
                    Description = newToken
                });
            }
            else
            {
                item.Description = newToken;
            }

            await db.SaveChangesAsync();
            return ServiceResult.Ok(new { message = "Token maestro actualizado." });
        }

        public async Task<ServiceResult> CreateParameterAsync(Parameters input, int companyId, int userId, bool isDeveloper)
        {
            if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.ParameterType))
                return ServiceResult.BadRequest("Tipo y nombre de parámetro son obligatorios.");
            if (IsSecret(input))
                return ServiceResult.Forbid();

            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (!masterId.HasValue)
                return ServiceResult.Conflict("Inventario maestro no encontrado.");

            var writable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true);
            if (input.InventoryId != masterId.Value && input.InventoryId != 0 && !await writable.ContainsAsync(input.InventoryId))
                return ServiceResult.Forbid();
            if ((input.InventoryId == masterId.Value || input.InventoryId == 0) && !await writable.AnyAsync())
                return ServiceResult.Forbid();

            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(20261107, {companyId})");

            var existing = await db.Parameters.AsNoTracking()
                .Where(p => p.CompanyId == companyId && (p.InventoryId == 0 || p.InventoryId == masterId.Value))
                .ToListAsync();

            var original = existing.FirstOrDefault(p =>
                CatalogNames.Key(p.ParameterType) == CatalogNames.Key(input.ParameterType) &&
                CatalogNames.Key(p.Name) == CatalogNames.Key(input.Name));

            if (original is not null)
            {
                await tx.CommitAsync();
                return ServiceResult.Ok(new
                {
                    original.Id,
                    original.InventoryId,
                    original.CompanyId,
                    original.ParameterType,
                    original.Name,
                    original.Description,
                    reused = true
                });
            }

            input.Id = 0;
            input.CompanyId = companyId;
            input.InventoryId = masterId.Value;
            input.ParameterType = input.ParameterType.Trim();
            input.Name = input.Name.Trim();
            db.Parameters.Add(input);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return ServiceResult.Created(input);
        }

        public async Task<ServiceResult> UpdateParameterAsync(int id, Parameters input, int companyId)
        {
            if (id != input.Id || IsSecret(input))
                return ServiceResult.BadRequest("Parámetro inválido.");

            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (!masterId.HasValue)
                return ServiceResult.Conflict("Inventario maestro no encontrado.");

            var item = await db.Parameters.FirstOrDefaultAsync(p =>
                p.Id == id && p.CompanyId == companyId && (p.InventoryId == 0 || p.InventoryId == masterId.Value));
            if (item is null) return ServiceResult.NotFound();
            if (IsSecret(item)) return ServiceResult.Forbid();

            var similar = await db.Parameters.AsNoTracking()
                .Where(p => p.CompanyId == companyId && (p.InventoryId == 0 || p.InventoryId == masterId.Value) && p.Id != id)
                .ToListAsync();

            if (similar.Any(p => CatalogNames.Key(p.ParameterType) == CatalogNames.Key(input.ParameterType) &&
                CatalogNames.Key(p.Name) == CatalogNames.Key(input.Name)))
                return ServiceResult.Conflict("Ya existe ese parámetro en el maestro.");

            item.Name = input.Name.Trim();
            item.ParameterType = input.ParameterType.Trim();
            item.Description = input.Description;
            await db.SaveChangesAsync();
            return ServiceResult.NoContent();
        }

        public async Task<ServiceResult> DeleteParameterAsync(int id, int companyId)
        {
            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (!masterId.HasValue)
                return ServiceResult.Conflict("Inventario maestro no encontrado.");

            var item = await db.Parameters.FirstOrDefaultAsync(p =>
                p.Id == id && p.CompanyId == companyId && (p.InventoryId == 0 || p.InventoryId == masterId.Value));
            if (item is null) return ServiceResult.NotFound();
            if (IsSecret(item)) return ServiceResult.Forbid();

            db.Parameters.Remove(item);
            await db.SaveChangesAsync();
            return ServiceResult.NoContent();
        }
    }
}