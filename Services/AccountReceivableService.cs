using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services
{
    public class AccountReceivableService(
        IWorkFlow workFlow,
        AppDbContext db,
        IInventoryScopeService inventoryScope) : WorkContainer<AccountReceivable>(workFlow), IAccountReceivableService
    {
        private IQueryable<int> AllowedMovementIds(int companyId, int userId, bool isDeveloper)
        {
            var readable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false, excludeMaster: false);
            return db.Movements
                .Where(m => m.CompanyId == companyId &&
                    db.Articles.Any(a => a.CompanyId == companyId && a.Id == m.ArticleId && readable.Contains(a.InventoryId)))
                .Select(m => m.Id);
        }

        public async Task<List<AccountReceivable>> GetVisibleByCompanyAsync(int companyId, int userId, bool isDeveloper)
        {
            var ids = await AllowedMovementIds(companyId, userId, isDeveloper).ToArrayAsync();
            var accounts = await GetAllByCompanyIdAsync(companyId);
            return [.. accounts.Where(a => ids.Contains(a.MovementId))];
        }

        public async Task<ServiceResult> GetVisibleByIdAsync(int id, int companyId, int userId, bool isDeveloper)
        {
            var account = await GetByIdAsync(id);
            if (account is null || account.CompanyId != companyId) return ServiceResult.NotFound();

            var allowed = await AllowedMovementIds(companyId, userId, isDeveloper).ContainsAsync(account.MovementId);
            return allowed ? ServiceResult.Ok(account) : ServiceResult.Forbid();
        }

        public override async Task<IEnumerable<AccountReceivable>> GetAllByCompanyIdAsync(int companyId)
        {
            var cuentas = (await base.GetAllByCompanyIdAsync(companyId)).ToList();
            var perfil = await _workFlow.Repository<Profile>().GetByIdAsync(companyId);

            if (perfil == null || !perfil.ApplyLateFee)
            {
                return cuentas;
            }

            int diasGracia = perfil.GraceDays ?? 0;
            decimal porcentajeMoraDiaria = (decimal)(perfil.LateFeePercentage ?? 0f);
            DateTime hoy = DateTime.UtcNow.Date;

            foreach (var cuenta in cuentas.Where(c => c.Status == "Pending"))
            {
                if (DateTime.TryParse(cuenta.DueDate, out DateTime fechaVencimiento) && fechaVencimiento.Date < hoy)
                {
                    int diasAtrasoTotal = (hoy - fechaVencimiento.Date).Days;
                    int diasMoraAplicables = diasAtrasoTotal - diasGracia;

                    if (diasMoraAplicables > 0 && porcentajeMoraDiaria > 0)
                    {
                        cuenta.LateFeeAmount = cuenta.InstallmentAmount * (porcentajeMoraDiaria / 100m) * diasMoraAplicables;
                    }
                }
            }

            return cuentas;
        }
    }
}