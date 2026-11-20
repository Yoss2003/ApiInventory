using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace InventoryAPI.Services
{
    public class InstallmentPaymentService(
        IWorkFlow workFlow,
        AppDbContext db,
        IInventoryScopeService inventoryScope) : WorkContainer<InstallmentPayment>(workFlow), IInstallmentPaymentService
    {
        public async Task<List<InstallmentPayment>> GetVisiblePaymentsAsync(int companyId, int userId, bool isDeveloper)
        {
            var readable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false, excludeMaster: false);
            var allowedAccounts = db.AccountsReceivables
                .Where(a => a.CompanyId == companyId &&
                    db.Movements.Any(m => m.Id == a.MovementId && m.CompanyId == companyId &&
                        db.Articles.Any(article => article.Id == m.ArticleId && readable.Contains(article.InventoryId))))
                .Select(a => a.Id);

            return await db.InstallmentPayments.AsNoTracking()
                .Where(p => p.CompanyId == companyId && allowedAccounts.Contains(p.InstallmentId))
                .OrderByDescending(p => p.Id)
                .ToListAsync();
        }

        public async Task<ServiceResult> GetVisiblePaymentByIdAsync(int id, int companyId, int userId, bool isDeveloper)
        {
            var payment = await db.InstallmentPayments.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);
            if (payment is null) return ServiceResult.NotFound();

            var allowed = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false, excludeMaster: false);
            var permitted = await db.AccountsReceivables.AnyAsync(a => a.Id == payment.InstallmentId && a.CompanyId == companyId &&
                db.Movements.Any(m => m.Id == a.MovementId && m.CompanyId == companyId &&
                    db.Articles.Any(article => article.Id == m.ArticleId && allowed.Contains(article.InventoryId))));

            return permitted ? ServiceResult.Ok(payment) : ServiceResult.Forbid();
        }

        public async Task<ServiceResult> GetHistoricalSaleInstallmentsAsync(int saleId, int companyId, int userId, bool isDeveloper)
        {
            var sale = await db.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == saleId && s.CompanyId == companyId);
            if (sale is null) return ServiceResult.NotFound();

            if (await db.SaleInstallments.AnyAsync(i => i.CompanyId == companyId && i.SaleId == saleId))
                return ServiceResult.Conflict("El crédito utiliza el nuevo registro. Consulta GET /api/Sales/{id}/installments.");

            var readable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false, excludeMaster: false);
            if (!await db.SaleDetails.AnyAsync(d => d.SaleId == saleId && d.CompanyId == companyId &&
                db.Articles.Any(a => a.Id == d.ArticleId && readable.Contains(a.InventoryId))))
                return ServiceResult.Forbid();

            var movementIds = await db.Movements.AsNoTracking()
                .Where(m => m.CompanyId == companyId && m.DocumentNumber == $"VENTA-{saleId}")
                .Select(m => m.Id)
                .ToListAsync();

            var installments = await db.AccountsReceivables.AsNoTracking()
                .Where(a => a.CompanyId == companyId && movementIds.Contains(a.MovementId))
                .OrderBy(a => a.InstallmentNumber)
                .ToListAsync();

            var ids = installments.Select(i => i.Id).ToArray();
            var payments = await db.InstallmentPayments.AsNoTracking()
                .Where(p => p.CompanyId == companyId && ids.Contains(p.InstallmentId))
                .OrderBy(p => p.Id)
                .ToListAsync();

            return ServiceResult.Ok(new
            {
                saleId,
                sale.TotalAmount,
                sale.AmountReceived,
                balance = sale.TotalAmount - sale.AmountReceived.GetValueOrDefault(),
                installments = installments.Select(i => new
                {
                    i.Id,
                    i.InstallmentNumber,
                    i.InstallmentAmount,
                    i.PaidAmount,
                    balance = i.InstallmentAmount - i.PaidAmount,
                    i.DueDate,
                    i.Status,
                    i.Frequency,
                    payments = payments.Where(p => p.InstallmentId == i.Id)
                }).ToArray()
            });
        }

        public async Task<ServiceResult> RecordHistoricalPaymentAsync(RecordPaymentRequest request, int companyId, int userId, bool isDeveloper)
        {
            if (request.InstallmentId <= 0 || request.PaymentAmount <= 0 ||
                decimal.Round(request.PaymentAmount, 2) != request.PaymentAmount)
                return ServiceResult.BadRequest("Ingresa una cuota y un abono positivo con hasta 2 decimales.");
            if (request.Observation?.Length > 2000)
                return ServiceResult.BadRequest("La observación no puede superar 2000 caracteres.");

            var original = await db.AccountsReceivables.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == request.InstallmentId && a.CompanyId == companyId);
            if (original is null) return ServiceResult.NotFound();

            var movement = await db.Movements.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == original.MovementId && m.CompanyId == companyId);
            if (movement is null || movement.DocumentNumber is null ||
                !movement.DocumentNumber.StartsWith("VENTA-", StringComparison.Ordinal) ||
                !int.TryParse(movement.DocumentNumber.AsSpan("VENTA-".Length), out var saleId))
                return ServiceResult.Conflict("La cuota no está vinculada a una venta válida.");

            var writable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true, excludeMaster: false);
            if (!await db.Articles.AnyAsync(a => a.Id == movement.ArticleId && a.CompanyId == companyId && writable.Contains(a.InventoryId)))
                return ServiceResult.Forbid();

            await using var tx = await db.Database.BeginTransactionAsync();
            var sale = await db.Sales.FromSqlInterpolated(
                $"SELECT * FROM \"Sales\" WHERE \"Id\" = {saleId} AND \"CompanyId\" = {companyId} FOR UPDATE")
                .SingleOrDefaultAsync();
            if (sale is null || sale.PaymentType != PaymentType.Cuotas)
                return ServiceResult.Conflict("La cuota no corresponde a una venta a crédito.");

            if (await db.SaleInstallments.AnyAsync(i => i.SaleId == sale.Id && i.CompanyId == companyId))
                return ServiceResult.Conflict("Esta venta utiliza el nuevo sistema. Registra el abono mediante /api/Sales/{id}/installments/{installmentId}/payments.");

            if (movement.ActionId != 2 ||
                !await db.SaleDetails.AnyAsync(d => d.SaleId == sale.Id && d.CompanyId == companyId && d.ArticleId == movement.ArticleId))
                return ServiceResult.Conflict("El movimiento de la cuota no corresponde al detalle de la venta.");

            if (await db.SaleCancellations.AnyAsync(c => c.SaleId == saleId && c.CompanyId == companyId))
                return ServiceResult.Conflict("La venta fue anulada y no admite abonos.");

            var installment = await db.AccountsReceivables.FromSqlInterpolated(
                $"SELECT * FROM \"AccountsReceivables\" WHERE \"Id\" = {request.InstallmentId} AND \"CompanyId\" = {companyId} FOR UPDATE")
                .SingleOrDefaultAsync();
            if (installment is null || installment.MovementId != movement.Id ||
                installment.Status is "Cancelled" or "Canceled")
                return ServiceResult.Conflict("La cuota fue cancelada o no está disponible.");

            var recorded = await db.InstallmentPayments
                .Where(p => p.InstallmentId == installment.Id && p.CompanyId == companyId)
                .SumAsync(p => (decimal?)p.PaymentAmount) ?? 0m;
            if (recorded != installment.PaidAmount)
                return ServiceResult.Conflict("Los abonos de esta cuota no cuadran con el saldo; requiere revisión.");

            var balance = installment.InstallmentAmount - installment.PaidAmount;
            if (request.PaymentAmount > balance || balance <= 0)
                return ServiceResult.Conflict("El abono supera el saldo pendiente de la cuota.");

            var collected = sale.AmountReceived.GetValueOrDefault();
            if (collected + request.PaymentAmount > sale.TotalAmount)
                return ServiceResult.Conflict("El cobro superaría el total de la venta.");

            var now = DateTime.UtcNow;
            installment.PaidAmount += request.PaymentAmount;
            installment.Status = installment.PaidAmount == installment.InstallmentAmount ? "Paid" : "Pending";
            if (installment.Status == "Paid")
                installment.PaymentDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            sale.AmountReceived = collected + request.PaymentAmount;

            var payment = new InstallmentPayment
            {
                CompanyId = companyId,
                InstallmentId = installment.Id,
                PaymentAmount = request.PaymentAmount,
                PaymentDate = now.ToString("O", CultureInfo.InvariantCulture),
                Observation = request.Observation?.Trim()
            };
            db.InstallmentPayments.Add(payment);
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            return ServiceResult.Created(new RecordPaymentResponse(
                payment.Id,
                payment.InstallmentId,
                payment.PaymentAmount,
                payment.PaymentDate,
                installment.PaidAmount,
                installment.InstallmentAmount - installment.PaidAmount,
                sale.TotalAmount - sale.AmountReceived.GetValueOrDefault()));
        }
    }
}