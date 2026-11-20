using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services;

public class SaleService(
    IWorkFlow workFlow,
    AppDbContext db,
    IPessimisticLockRepository locks,
    IInventoryScopeService inventoryScope) : WorkContainer<Sale>(workFlow), ISaleService
{
    public async Task<List<Sale>> GetVisibleSalesAsync(int companyId, int userId, bool isDeveloper)
    {
        var accessible = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false, excludeMaster: false);
        return await db.Sales.AsNoTracking()
            .Include(s => s.SaleDetails)
            .Where(s => s.CompanyId == companyId && s.SaleDetails.Any(d =>
                db.Articles.Any(a => a.Id == d.ArticleId && accessible.Contains(a.InventoryId))))
            .OrderByDescending(s => s.SaleDate)
            .ToListAsync();
    }

    public async Task<ServiceResult> CreateAuthorizedSaleAsync(Sale sale, int companyId, int userId, bool isDeveloper)
    {
        if (!SalePaymentResolver.TryResolve(sale.PaymentGroup, sale.DigitalWallet, sale.PaymentType,
            out var selectedPayment, out var paymentError))
            return ServiceResult.BadRequest(paymentError);

        sale.PaymentType = selectedPayment;
        sale.CompanyId = companyId;
        sale.UserId = userId;

        if (sale.SaleDetails == null || sale.SaleDetails.Count == 0)
            return ServiceResult.BadRequest("La venta no tiene artículos.");

        if (sale.PaymentType == PaymentType.Cuotas &&
            (string.IsNullOrWhiteSpace(sale.CustomerName) ||
             sale.CustomerName.Trim().Equals("Público general", StringComparison.OrdinalIgnoreCase)))
            return ServiceResult.BadRequest("Identifica al comprador para registrar una venta a cuotas.");

        var permitted = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true, excludeMaster: false);
        foreach (var d in sale.SaleDetails)
        {
            if (!await db.Articles.AnyAsync(a => a.Id == d.ArticleId && a.CompanyId == companyId && permitted.Contains(a.InventoryId)))
                return ServiceResult.Forbid();
            d.CompanyId = companyId;
        }

        var (ok, message) = await ProcessSaleAsync(sale);
        return ok
            ? ServiceResult.Ok(new { message, saleId = sale.Id })
            : ServiceResult.BadRequest(message);
    }

    public async Task<object> GetCancellationsAsync(int companyId)
    {
        return await db.SaleCancellations.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new { x.SaleId, x.CancelledAt, x.Reason })
            .ToListAsync();
    }

    public object[] GetPaymentMethods() =>
    [
        new { label = "Efectivo", value = "Efectivo" },
        new { label = "Tarjeta", value = "Tarjeta" },
        new { label = "Transferencia", value = "Transferencia" },
        new { label = "Billetera digital", value = "BilleteraDigital", wallets = new[] { "Yape", "Plin", "Bim" } },
        new { label = "Venta a Cuotas", value = "Cuotas", frequencies = new[] { "Diario", "Semanal", "Mensual" } }
    ];

    public ServiceResult SimulateInstallments(InstallmentSimulationRequest request)
    {
        if (!SaleInstallmentCalculator.TryBuild(request.TotalAmount, request.InstallmentPlan,
            SaleInstallmentCalculator.BusinessToday(), out var items, out var error))
            return ServiceResult.BadRequest(error);

        return ServiceResult.Ok(new
        {
            total = request.TotalAmount,
            downPayment = request.InstallmentPlan.DownPayment,
            financedAmount = request.TotalAmount - request.InstallmentPlan.DownPayment,
            installments = items.Select(i => new { i.InstallmentNumber, i.InstallmentAmount, i.DueDate, i.Frequency })
        });
    }

    public async Task<ServiceResult> GetSaleInstallmentsAsync(int id, int companyId, int userId, bool isDeveloper)
    {
        var sale = await db.Sales.AsNoTracking()
            .Include(x => x.SaleDetails)
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
        if (sale is null) return ServiceResult.NotFound();

        var readable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false, excludeMaster: false);
        var articleIds = sale.SaleDetails.Select(d => d.ArticleId).ToArray();
        var accessibleCount = await db.Articles.CountAsync(a => a.CompanyId == companyId &&
            articleIds.Contains(a.Id) && readable.Contains(a.InventoryId));
        if (accessibleCount != articleIds.Distinct().Count()) return ServiceResult.Forbid();

        if (sale.PaymentType != PaymentType.Cuotas)
            return ServiceResult.Conflict("La venta no corresponde a una venta a cuotas.");

        var items = await db.SaleInstallments.AsNoTracking()
            .Include(x => x.Receipts)
            .Where(x => x.SaleId == id && x.CompanyId == companyId)
            .OrderBy(x => x.InstallmentNumber)
            .ToListAsync();

        if (items.Count == 0)
            return ServiceResult.Conflict("No existe un cronograma en el nuevo sistema para esta venta. Comprueba si es un crédito histórico.");

        var cancelled = await db.SaleCancellations.AnyAsync(x => x.CompanyId == companyId && x.SaleId == id);
        var today = SaleInstallmentCalculator.BusinessToday();

        return ServiceResult.Ok(new
        {
            saleId = id,
            totalAmount = sale.TotalAmount,
            amountReceived = sale.AmountReceived,
            downPayment = items.Count == 0 ? (decimal?)null : sale.TotalAmount - items.Sum(x => x.InstallmentAmount),
            financedAmount = items.Sum(x => x.InstallmentAmount),
            balance = items.Sum(x => x.InstallmentAmount - x.PaidAmount),
            cancelled,
            installments = items.Select(x => new
            {
                x.Id,
                x.InstallmentNumber,
                x.InstallmentAmount,
                x.PaidAmount,
                balance = x.InstallmentAmount - x.PaidAmount,
                x.DueDate,
                x.Frequency,
                status = cancelled ? "Cancelled" :
                    x.PaidAmount >= x.InstallmentAmount ? "Paid" :
                    x.DueDate < today ? "Overdue" : x.PaidAmount > 0 ? "Partial" : "Pending",
                receipts = x.Receipts.OrderBy(p => p.PaidAt).Select(p => new
                {
                    p.Id,
                    p.Amount,
                    p.PaidAt,
                    paymentMethod = p.PaymentType.ToString(),
                    p.ReceivedByUserId,
                    p.Notes
                })
            })
        });
    }

    public async Task<ServiceResult> PaySaleInstallmentAsync(
        int id, int installmentId, InstallmentReceiptRequest request,
        int companyId, int userId, bool isDeveloper)
    {
        if (request.OperationKey == Guid.Empty || request.Amount <= 0 ||
            decimal.Round(request.Amount, 2) != request.Amount)
            return ServiceResult.BadRequest("El abono debe ser positivo, con máximo dos decimales y clave de operación.");

        if (string.IsNullOrWhiteSpace(request.PaymentMethod) ||
            !SalePaymentResolver.TryResolve(request.PaymentMethod, request.DigitalWallet, PaymentType.Efectivo,
                out var paymentType, out _))
            return ServiceResult.BadRequest("Selecciona un método de abono válido y su billetera cuando corresponda.");

        if (paymentType == PaymentType.Cuotas)
            return ServiceResult.BadRequest("Un abono debe utilizar un método de pago efectivo, tarjeta, transferencia o billetera.");

        if (request.Notes?.Length > 500)
            return ServiceResult.BadRequest("La observación del abono no puede superar 500 caracteres.");

        var sale = await db.Sales.AsNoTracking()
            .Include(s => s.SaleDetails)
            .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId);
        if (sale is null) return ServiceResult.NotFound();

        if (sale.PaymentType != PaymentType.Cuotas)
            return ServiceResult.Conflict("La venta no corresponde a un crédito.");

        var writable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true, excludeMaster: false);
        foreach (var detail in sale.SaleDetails)
        {
            if (!await db.Articles.AnyAsync(a => a.CompanyId == companyId &&
                a.Id == detail.ArticleId && writable.Contains(a.InventoryId)))
                return ServiceResult.Forbid();
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        var lockedSale = await locks.LockSaleAsync(id, companyId);

        var installment = await locks.LockSaleInstallmentAsync(installmentId, id, companyId);
        if (installment is null) return ServiceResult.NotFound();

        var existing = await db.SaleInstallmentReceipts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.OperationKey == request.OperationKey);
        if (existing != null)
        {
            if (existing.CompanyId != companyId || existing.SaleInstallmentId != installmentId ||
                existing.Amount != request.Amount || existing.PaymentType != paymentType)
                return ServiceResult.Conflict("La clave de operación ya se utilizó con otro pago.");

            return ServiceResult.Ok(new
            {
                receiptId = existing.Id,
                reused = true,
                balance = installment.InstallmentAmount - installment.PaidAmount
            });
        }

        if (await db.SaleCancellations.AnyAsync(x => x.CompanyId == companyId && x.SaleId == id))
            return ServiceResult.Conflict("No se pueden abonar cuotas de una venta anulada.");

        var received = lockedSale?.AmountReceived.GetValueOrDefault();
        if (installment.PaidAmount < 0 || installment.PaidAmount > installment.InstallmentAmount)
            return ServiceResult.Conflict("La cuota tiene un saldo incoherente. Requiere revisión.");

        var recorded = await db.SaleInstallmentReceipts.AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.SaleInstallmentId == installmentId)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;
        if (recorded != installment.PaidAmount)
            return ServiceResult.Conflict("Los abonos no cuadran con el saldo de la cuota. Requiere revisión.");

        if (request.Amount > installment.InstallmentAmount - installment.PaidAmount ||
            received + request.Amount > lockedSale?.TotalAmount)
            return ServiceResult.Conflict("El abono supera el saldo pendiente de la cuota o de la venta.");

        var receipt = new SaleInstallmentReceipt
        {
            SaleInstallmentId = installment.Id,
            CompanyId = companyId,
            Amount = request.Amount,
            PaidAt = DateTime.UtcNow,
            PaymentType = paymentType,
            ReceivedByUserId = userId,
            Notes = request.Notes?.Trim(),
            OperationKey = request.OperationKey
        };

        installment.PaidAmount += request.Amount;
        lockedSale?.AmountReceived = received + request.Amount;
        db.SaleInstallmentReceipts.Add(receipt);

        try
        {
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            return ServiceResult.Conflict("No se pudo confirmar el abono. Consulta la operación antes de reintentar.");
        }

        return ServiceResult.Ok(new
        {
            receiptId = receipt.Id,
            balance = installment.InstallmentAmount - installment.PaidAmount,
            paidAmount = installment.PaidAmount,
            totalReceived = lockedSale?.AmountReceived
        });
    }

    public async Task<ServiceResult> QuickSaleAsync(QuickSaleRequest request, int companyId, int userId, bool isDeveloper)
    {
        if (request.OperationKey == Guid.Empty || request.ArticleId <= 0 || request.Quantity <= 0 || request.UnitPrice < 0)
            return ServiceResult.BadRequest("Artículo, cantidad y precio válidos son obligatorios.");

        if (string.IsNullOrWhiteSpace(request.PaymentMethod) ||
            !SalePaymentResolver.TryResolve(request.PaymentMethod, request.DigitalWallet, PaymentType.Efectivo,
                out var payment, out _))
            return ServiceResult.BadRequest("Método de pago inválido o billetera sin seleccionar.");

        List<SaleInstallment> schedule = [];
        var credit = payment == PaymentType.Cuotas;
        var downPayment = request.DownPayment ?? 0m;

        if (credit)
        {
            if (string.IsNullOrWhiteSpace(request.CustomerName) ||
                request.CustomerName.Trim().Equals("Público general", StringComparison.OrdinalIgnoreCase))
                return ServiceResult.BadRequest("Identifica al cliente para registrar una venta a cuotas.");

            var creditRequest = new SaleInstallmentPlanRequest
            {
                InstallmentCount = request.InstallmentCount ?? 0,
                DownPayment = downPayment,
                Frequency = request.InstallmentFrequency ?? string.Empty,
                FirstDueDate = request.FirstDueDate
            };
            if (!SaleInstallmentCalculator.TryBuild(request.Quantity * request.UnitPrice, creditRequest,
                SaleInstallmentCalculator.BusinessToday(), out schedule, out var creditError))
                return ServiceResult.BadRequest(creditError);
        }
        else if (request.InstallmentCount != null || request.DownPayment != null ||
            request.FirstDueDate != null || !string.IsNullOrWhiteSpace(request.InstallmentFrequency))
        {
            return ServiceResult.BadRequest("La configuración de cuotas solamente corresponde a ventas financiadas.");
        }

        var prior = await db.SaleOperations.FirstOrDefaultAsync(x => x.OperationKey == request.OperationKey);
        if (prior != null)
            return prior.CompanyId == companyId
                ? ServiceResult.Ok(new { saleId = prior.SaleId, reused = true })
                : ServiceResult.Conflict("Operación duplicada.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var article = await locks.LockArticleAsync(request.ArticleId, companyId);
        if (article == null || !article.IsActive)
            return ServiceResult.NotFound("Artículo inactivo o no encontrado.");

        var committedPrior = await db.SaleOperations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OperationKey == request.OperationKey);
        if (committedPrior != null)
            return committedPrior.CompanyId == companyId
                ? ServiceResult.Ok(new { saleId = committedPrior.SaleId, reused = true })
                : ServiceResult.Conflict("La clave de operación pertenece a otra empresa.");

        var writable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true, excludeMaster: false);
        if (!await writable.ContainsAsync(article.InventoryId))
            return ServiceResult.Forbid();

        if (article.Stock < request.Quantity)
            return ServiceResult.Conflict("Stock insuficiente.");

        if (article.Tracking.ToString() == "Serialized" && request.Quantity != decimal.Truncate(request.Quantity))
            return ServiceResult.BadRequest("Para artículos serializados se requieren unidades enteras.");

        var detailIds = request.DetailIds ?? [];
        if (article.Tracking.ToString() == "Serialized")
        {
            if (detailIds.Count != (int)request.Quantity || detailIds.Distinct().Count() != detailIds.Count)
                return ServiceResult.BadRequest("Selecciona una serie distinta por cada unidad vendida.");

            var usable = await db.Set<ArticleDetails>().CountAsync(d => d.ArticleId == article.Id && d.IsActive &&
                detailIds.Contains(d.Id) && !db.ArticleUnitOperations.Any(op => op.ArticleDetailId == d.Id && !op.IsReversed));
            if (usable != detailIds.Count)
                return ServiceResult.Conflict("Alguna de las series no está disponible.");
        }
        else if (detailIds.Count > 0)
        {
            return ServiceResult.BadRequest("La selección de series solo aplica a artículos serializados.");
        }

        var modeId = credit
            ? await db.SalesModes.Where(x => x.SalesModeName == schedule[0].Frequency)
                .Select(x => x.Id).FirstOrDefaultAsync()
            : await db.SalesModes.Select(x => x.Id).OrderBy(x => x).FirstOrDefaultAsync();

        if (modeId == 0)
        {
            var mode = new SalesMode { SalesModeName = credit ? schedule[0].Frequency : "Venta mostrador" };
            db.SalesModes.Add(mode);
            await db.SaveChangesAsync();
            modeId = mode.Id;
        }

        var total = request.Quantity * request.UnitPrice;
        var sale = new Sale
        {
            UserId = userId,
            CompanyId = companyId,
            SalesModeId = modeId,
            SaleDate = DateTime.UtcNow,
            CustomerName = string.IsNullOrWhiteSpace(request.CustomerName) ? "Público general" : request.CustomerName.Trim(),
            PaymentType = payment,
            TotalAmount = total,
            Notes = request.Notes,
            AmountReceived = credit ? downPayment : total,
            ChangeGiven = 0,
            SaleDetails =
            [
                new SaleDetail
                {
                    ArticleId = article.Id,
                    CompanyId = companyId,
                    Quantity = request.Quantity,
                    UnitPrice = request.UnitPrice,
                    SubTotal = total
                }
            ]
        };

        db.Sales.Add(sale);
        article.Stock -= request.Quantity;
        article.ModificationDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var employeeId = await db.Employees
            .Where(e => e.CompanyId == companyId && e.UserId == userId)
            .Select(e => (int?)e.Id)
            .FirstOrDefaultAsync();

        db.Movements.Add(new Movement
        {
            ArticleId = article.Id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            ActionId = 2,
            MovementDate = DateTime.UtcNow,
            Amount = request.Quantity,
            Recipient = sale.CustomerName,
            PaymentMethod = payment.ToString(),
            SalePrice = request.UnitPrice,
            Observation = request.Notes,
            DocumentNumber = $"VENTA-{sale.Id}"
        });

        foreach (var detailId in detailIds)
        {
            db.ArticleUnitOperations.Add(new ArticleUnitOperation
            {
                CompanyId = companyId,
                ArticleId = article.Id,
                ArticleDetailId = detailId,
                SaleId = sale.Id,
                CreatedAt = DateTime.UtcNow
            });
        }

        db.SaleOperations.Add(new SaleOperation { OperationKey = request.OperationKey, SaleId = sale.Id, CompanyId = companyId });

        foreach (var installment in schedule)
        {
            installment.SaleId = sale.Id;
            installment.CompanyId = companyId;
        }
        db.SaleInstallments.AddRange(schedule);

        try
        {
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            return ServiceResult.Conflict("No se pudo confirmar la venta. Verifica si ya existe la clave de operación antes de reintentar.");
        }

        return ServiceResult.Ok(new
        {
            saleId = sale.Id,
            total,
            amountReceived = sale.AmountReceived,
            financedAmount = credit ? total - downPayment : 0m,
            installmentCount = schedule.Count
        });
    }

    public async Task<ServiceResult> CancelSaleAsync(int id, CancelSaleRequest request, int companyId, int userId, bool isDeveloper)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return ServiceResult.BadRequest("Escribe un motivo.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var lockedSale = await locks.LockSaleAsync(id, companyId);
        if (lockedSale is null) return ServiceResult.NotFound("Venta no encontrada.");

        var received = lockedSale?.AmountReceived.GetValueOrDefault();
        var sale = await db.Sales.Include(x => x.SaleDetails)
            .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId);
        if (sale is null) return ServiceResult.NotFound();

        var allowed = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true, excludeMaster: false);
        foreach (var detail in sale.SaleDetails)
        {
            if (!await db.Articles.AnyAsync(a => a.Id == detail.ArticleId && allowed.Contains(a.InventoryId)))
                return ServiceResult.Forbid();
        }

        if (await db.SaleCancellations.AnyAsync(c => c.SaleId == id))
            return ServiceResult.Conflict("Venta ya anulada.");

        if (sale.PaymentType == PaymentType.Cuotas)
        {
            var schedule = await db.SaleInstallments
                .Where(i => i.SaleId == id && i.CompanyId == companyId)
                .ToListAsync();
            if (schedule.Count == 0)
                return ServiceResult.Conflict("Crédito sin cronograma en el libro nuevo: requiere revisión antes de anular.");

            var installmentIds = schedule.Select(i => i.Id).ToArray();
            if (sale.AmountReceived.GetValueOrDefault() > 0 ||
                schedule.Any(i => i.PaidAmount > 0) ||
                await db.SaleInstallmentReceipts.AnyAsync(p => p.CompanyId == companyId &&
                    installmentIds.Contains(p.SaleInstallmentId)))
                return ServiceResult.Conflict("La venta a cuotas tiene cobros registrados. Gestiona los reembolsos antes de anularla.");
        }

        var keys = sale.SaleDetails.Select(d => d.ArticleId).Distinct().OrderBy(x => x).ToArray();
        foreach (var articleId in keys)
        {
            var article = await locks.LockArticleAsync(articleId, companyId);
            if (article is null)
                return ServiceResult.NotFound("Falta uno de los artículos de la venta.");

            var quantity = sale.SaleDetails.Where(d => d.ArticleId == articleId).Sum(d => d.Quantity);
            article.Stock += quantity;
            article.ModificationDate = DateTime.UtcNow;
            db.Movements.Add(new Movement
            {
                ArticleId = article.Id,
                CompanyId = companyId,
                ActionId = 1,
                MovementDate = DateTime.UtcNow,
                Amount = quantity,
                Observation = $"Anulación de venta #{id}: {request.Reason.Trim()}"
            });
        }

        var soldUnits = await db.ArticleUnitOperations.Where(u => u.SaleId == id && !u.IsReversed).ToListAsync();
        foreach (var soldUnit in soldUnits)
        {
            soldUnit.IsReversed = true;
            soldUnit.ReversedAt = DateTime.UtcNow;
        }

        db.SaleCancellations.Add(new SaleCancellation
        {
            SaleId = id,
            CompanyId = companyId,
            CancelledByUserId = userId,
            CancelledAt = DateTime.UtcNow,
            Reason = request.Reason.Trim()
        });

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ServiceResult.Ok(new { message = "Venta anulada, stock devuelto e historial conservado." });
    }

    public async Task<(bool Success, string Message)> ProcessSaleAsync(Sale sale)
    {
        if (sale.SaleDetails == null || sale.SaleDetails.Count == 0 || sale.CompanyId <= 0 || sale.UserId <= 0)
            return (false, "Venta sin usuario, empresa o artículos.");
        if (sale.SaleDetails.Any(d => d.ArticleId <= 0 || d.Quantity <= 0 || d.UnitPrice < 0))
            return (false, "Todos los artículos requieren identificador, cantidad positiva y precio no negativo.");
        if (!Enum.IsDefined(sale.PaymentType))
            return (false, "Método de pago desconocido.");
        if (!await db.Users.AnyAsync(u => u.Id == sale.UserId &&
            (u.CompanyId == sale.CompanyId || u.RoleId == 1) && u.IsActive))
            return (false, "Usuario vendedor no autorizado.");

        foreach (var d in sale.SaleDetails)
            d.SubTotal = decimal.Round(d.Quantity * d.UnitPrice, 2, MidpointRounding.AwayFromZero);
        var total = sale.SaleDetails.Sum(d => d.SubTotal);
        if (total <= 0)
            return (false, "La venta debe tener un importe mayor a cero.");

        List<SaleInstallment> schedule = [];
        if (sale.PaymentType == PaymentType.Cuotas)
        {
            if (!SaleInstallmentCalculator.TryBuild(total, sale.InstallmentPlan, SaleInstallmentCalculator.BusinessToday(),
                out schedule, out var error))
                return (false, error);
            sale.AmountReceived = sale.InstallmentPlan!.DownPayment;
            sale.ChangeGiven = 0m;
        }
        else if (sale.InstallmentPlan != null)
            return (false, "El cronograma solo se puede configurar para ventas a cuotas.");
        else if (sale.AmountReceived < 0 || sale.ChangeGiven < 0)
            return (false, "Los importes recibidos y de vuelto no pueden ser negativos.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            if (sale.SalesModeId <= 0)
            {
                var modeName = sale.PaymentType == PaymentType.Cuotas ? schedule[0].Frequency : "Venta mostrador";
                var mode = await db.SalesModes.FirstOrDefaultAsync(m => m.SalesModeName == modeName);
                if (mode == null)
                {
                    mode = new SalesMode { SalesModeName = modeName };
                    db.SalesModes.Add(mode);
                    await db.SaveChangesAsync();
                }
                sale.SalesModeId = mode.Id;
            }
            else if (!await db.SalesModes.AnyAsync(m => m.Id == sale.SalesModeId))
                return (false, "La modalidad de venta indicada no existe.");

            var summary = sale.SaleDetails.GroupBy(d => d.ArticleId).OrderBy(g => g.Key)
                .Select(g => new { ArticleId = g.Key, Quantity = g.Sum(d => d.Quantity) }).ToArray();
            var locked = new Dictionary<int, Article>();
            foreach (var item in summary)
            {
                var article = await locks.LockArticleAsync(item.ArticleId, sale.CompanyId);
                if (article == null || !article.IsActive)
                    return (false, $"Artículo #{item.ArticleId} no disponible.");
                if (article.Tracking.ToString() == "Serialized")
                    return (false, "La venta de artículos serializados debe identificar sus series. Utiliza POS con selección de unidades.");
                if (item.Quantity > article.Stock)
                    return (false, $"Stock insuficiente de {article.Name}.");
                locked[article.Id] = article;
            }

            sale.Id = 0;
            sale.SaleDate = DateTime.UtcNow;
            sale.TotalAmount = total;
            foreach (var detail in sale.SaleDetails)
            {
                detail.Id = 0;
                detail.SaleId = 0;
                detail.CompanyId = sale.CompanyId;
            }
            db.Sales.Add(sale);
            foreach (var item in summary)
            {
                locked[item.ArticleId].Stock -= item.Quantity;
                locked[item.ArticleId].ModificationDate = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();

            foreach (var item in schedule)
            {
                item.SaleId = sale.Id;
                item.CompanyId = sale.CompanyId;
            }
            db.SaleInstallments.AddRange(schedule);

            foreach (var detail in sale.SaleDetails)
            {
                db.Movements.Add(new Movement
                {
                    ArticleId = detail.ArticleId,
                    CompanyId = sale.CompanyId,
                    ActionId = 2,
                    MovementDate = DateTime.UtcNow,
                    Amount = detail.Quantity,
                    Recipient = string.IsNullOrWhiteSpace(sale.CustomerName) ? "Público general" : sale.CustomerName,
                    PaymentMethod = sale.PaymentType.ToString(),
                    SalePrice = detail.UnitPrice,
                    DocumentNumber = $"VENTA-{sale.Id}",
                    Observation = sale.Notes
                });
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return (true, "Venta, stock, movimientos y cronograma confirmados.");
        }
        catch (DbUpdateException)
        {
            return (false, "La venta no pudo registrarse; se revirtió la operación.");
        }
    }
}