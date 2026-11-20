using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services;

public class MovementService(
    IWorkFlow workFlow,
    AppDbContext db,
    IPessimisticLockRepository locks,
    IInventoryScopeService inventoryScope) : WorkContainer<Movement>(workFlow), IMovementService
{
    public async Task<ServiceResult> GetMasterHistoryAsync(
        int companyId,
        int? inventoryId,
        int? userId,
        int? actorUserId,
        int? actionId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        int page,
        int pageSize)
    {
        if (actionId.HasValue && actionId.Value is not (1 or 2 or 3))
            return ServiceResult.BadRequest("Tipo válido: 1 entrada, 2 salida, 3 custodia.");
        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
            return ServiceResult.BadRequest("El rango de fechas no es válido.");

        page = Math.Clamp(page, 1, 1000000);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var rows =
            from movement in db.Movements.AsNoTracking()
            join article in db.Articles.AsNoTracking() on movement.ArticleId equals article.Id
            join inventory in db.Inventories.AsNoTracking() on article.InventoryId equals inventory.Id
            where movement.CompanyId == companyId && article.CompanyId == companyId &&
                inventory.CompanyId == companyId && !inventory.IsMaster &&
                (movement.ActionId == 1 || movement.ActionId == 2 || movement.ActionId == 3)
            select new
            {
                movement.Id,
                movement.MovementDate,
                movement.ActionId,
                movement.Amount,
                movement.EmployeeId,
                movement.Observation,
                InventoryId = inventory.Id,
                InventoryName = inventory.Alias ?? inventory.InventoryName,
                UserId = inventory.UserId,
                Username = inventory.Username,
                ActorUserId = db.Employees.Where(e => e.Id == movement.EmployeeId &&
                    e.CompanyId == companyId).Select(e => e.UserId).FirstOrDefault(),
                ArticleId = article.Id,
                ArticleName = article.Name,
                ArticleCode = article.Code
            };

        if (inventoryId.HasValue) rows = rows.Where(x => x.InventoryId == inventoryId.Value);
        if (userId.HasValue) rows = rows.Where(x => x.UserId == userId.Value);
        if (actorUserId.HasValue) rows = rows.Where(x => x.ActorUserId == actorUserId.Value);
        if (actionId.HasValue) rows = rows.Where(x => x.ActionId == actionId.Value);
        if (fromDate.HasValue) rows = rows.Where(x => x.MovementDate >= fromDate.Value.UtcDateTime);
        if (toDate.HasValue) rows = rows.Where(x => x.MovementDate <= toDate.Value.UtcDateTime);

        var total = await rows.CountAsync();
        var items = await rows.OrderByDescending(x => x.MovementDate)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return ServiceResult.Ok(new { items, total, page, pageSize });
    }

    public async Task<List<Movement>> GetVisibleMovementsAsync(int companyId, int userId, bool isDeveloper)
    {
        var accessible = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false, excludeMaster: false);
        return await db.Movements.AsNoTracking()
            .Where(m => m.CompanyId == companyId &&
                db.Articles.Any(a => a.Id == m.ArticleId && accessible.Contains(a.InventoryId)))
            .ToListAsync();
    }

    public async Task<Movement?> GetVisibleMovementByIdAsync(int id, int companyId, int userId, bool isDeveloper)
    {
        var movement = await GetByIdAsync(id);
        if (movement is null || movement.CompanyId != companyId) return null;

        var accessible = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false, excludeMaster: false);
        var hasAccess = await db.Articles.AnyAsync(a => a.Id == movement.ArticleId && accessible.Contains(a.InventoryId));
        return hasAccess ? movement : null;
    }

    public async Task<ServiceResult> CreateAuthorizedMovementAsync(Movement movement, int companyId, int userId, bool isDeveloper)
    {
        movement.CompanyId = companyId;
        var writable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true, excludeMaster: false);
        if (!await db.Articles.AnyAsync(a => a.Id == movement.ArticleId && writable.Contains(a.InventoryId)))
            return ServiceResult.Forbid();

        if (movement.ActionId == 3)
            return ServiceResult.BadRequest("Las entregas se crean mediante /api/Assignments.");

        try
        {
            movement.EmployeeId = await db.Employees
                .Where(e => e.CompanyId == companyId && e.UserId == userId)
                .Select(e => (int?)e.Id)
                .FirstOrDefaultAsync();

            var success = await CreateAsync(movement);
            return success
                ? ServiceResult.Created(movement)
                : ServiceResult.BadRequest("No se pudo registrar el movimiento.");
        }
        catch (ArgumentException e)
        {
            return ServiceResult.BadRequest(e.Message);
        }
        catch (InvalidOperationException e)
        {
            return ServiceResult.Conflict(e.Message);
        }
    }

    public async Task<object> GetVisibleCancellationsAsync(int companyId, int userId, bool isDeveloper)
    {
        var accessible = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false, excludeMaster: false);
        return await db.MovementCancellations.AsNoTracking()
            .Where(c => c.CompanyId == companyId &&
                db.Movements.Any(m => m.Id == c.OriginalMovementId &&
                    db.Articles.Any(a => a.Id == m.ArticleId && accessible.Contains(a.InventoryId))))
            .Select(c => new { c.OriginalMovementId, c.ReversalMovementId, c.CancelledAt, c.Reason })
            .ToListAsync();
    }

    public async Task<ServiceResult> CancelAuthorizedMovementAsync(int id, int companyId, int userId, bool isDeveloper, string reason)
    {
        var writable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true, excludeMaster: false);
        var permitted = await db.Movements.AnyAsync(m => m.Id == id && m.CompanyId == companyId &&
            db.Articles.Any(a => a.Id == m.ArticleId && writable.Contains(a.InventoryId)));
        if (!permitted) return ServiceResult.Forbid();

        var (success, error) = await CancelStockMovementAsync(id, companyId, userId, reason);
        return success
            ? ServiceResult.Ok(new { message = "Movimiento revertido y stock actualizado." })
            : ServiceResult.Conflict(error);
    }

    public override async Task<bool> CreateAsync(Movement movement)
    {
        if (movement.CompanyId <= 0 || movement.ArticleId <= 0 || movement.Amount is null or <= 0)
            throw new ArgumentException("Artículo, empresa y cantidad positiva son obligatorios.");
        if (movement.ActionId is not (1 or 2))
            throw new ArgumentException("Para asignaciones usa /api/Assignments; solo entradas (1) y salidas (2) son admitidas aquí.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var article = await locks.LockArticleAsync(movement.ArticleId, movement.CompanyId);
        if (article is null || !article.IsActive)
            throw new InvalidOperationException("Artículo inexistente o inactivo.");
        if (article.Tracking.ToString() == "Serialized" && movement.Amount != decimal.Truncate(movement.Amount.Value))
            throw new ArgumentException("Un artículo serializado requiere cantidades enteras.");
        if (article.Tracking.ToString() == "Serialized")
            throw new InvalidOperationException("Para serializados, registra primero cada unidad y usa venta/asignación con su ID; no se permite ajuste por cantidad.");

        var delta = movement.ActionId == 1 ? movement.Amount.Value : -movement.Amount.Value;
        if (article.Stock + delta < 0)
            throw new InvalidOperationException("Stock insuficiente para completar la operación.");

        article.Stock += delta;
        article.ModificationDate = DateTime.UtcNow;
        movement.Id = 0;
        movement.MovementDate = DateTime.UtcNow;
        db.Movements.Add(movement);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    public async Task<(bool Success, string Error)> CancelStockMovementAsync(
        int originalId, int companyId, int userId, string reason)
    {
        if (userId <= 0 || companyId <= 0 || string.IsNullOrWhiteSpace(reason))
            return (false, "Indica el motivo de anulación.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var movement = await locks.LockMovementAsync(originalId, companyId);
        if (movement == null) return (false, "Movimiento inexistente.");
        if (movement.ActionId is not (1 or 2))
            return (false, "Las asignaciones se revierten mediante devolución, no anulación.");
        if (!string.IsNullOrWhiteSpace(movement.PaymentMethod) && movement.PaymentMethod != "N/A")
            return (false, "Las ventas se anulan desde el módulo de ventas, no como movimientos aislados.");
        if (movement.Observation?.Contains("Registro inicial del producto", StringComparison.OrdinalIgnoreCase) == true)
            return (false, "El alta inicial no se cancela con este mecanismo.");
        if (await db.MovementCancellations.AnyAsync(c => c.OriginalMovementId == originalId))
            return (false, "Este movimiento ya fue anulado.");

        var article = await locks.LockArticleAsync(movement.ArticleId, companyId);
        if (article == null) return (false, "Artículo original inexistente.");

        var delta = movement.ActionId == 1 ? -(movement.Amount ?? 0m) : (movement.Amount ?? 0m);
        if (article.Stock + delta < 0)
            return (false, "No se puede anular porque consumiría stock ya utilizado.");

        article.Stock += delta;
        article.ModificationDate = DateTime.UtcNow;
        var reversed = new Movement
        {
            ArticleId = movement.ArticleId,
            CompanyId = companyId,
            ActionId = movement.ActionId == 1 ? 2 : 1,
            Amount = Math.Abs(delta),
            MovementDate = DateTime.UtcNow,
            Recipient = movement.Recipient,
            Observation = $"REVERSIÓN MOVIMIENTO #{originalId}: {reason.Trim()}"
        };
        db.Movements.Add(reversed);
        await db.SaveChangesAsync();

        db.MovementCancellations.Add(new MovementCancellation
        {
            OriginalMovementId = originalId,
            ReversalMovementId = reversed.Id,
            CompanyId = companyId,
            CancelledByUserId = userId,
            CancelledAt = DateTime.UtcNow,
            Reason = reason.Trim()
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return (true, string.Empty);
    }
}