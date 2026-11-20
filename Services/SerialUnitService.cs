using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services;

public sealed class SerialUnitService(
    AppDbContext db,
    IInventoryScopeService inventoryScope) : ISerialUnitService
{
    public async Task<ServiceResult> GetAvailableUnitsAsync(int articleId, int companyId, int userId, bool isDeveloper)
    {
        var article = await db.Articles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == articleId && a.CompanyId == companyId);
        if (article is null) return ServiceResult.NotFound();

        if (!await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false, excludeMaster: false).ContainsAsync(article.InventoryId))
            return ServiceResult.Forbid();

        var units = await db.Set<ArticleDetails>().AsNoTracking()
            .Where(d => d.ArticleId == articleId && d.IsActive &&
                !db.ArticleUnitOperations.Any(op => op.ArticleDetailId == d.Id && !op.IsReversed))
            .OrderBy(d => d.SerialNumber)
            .Select(d => new { d.Id, d.SerialNumber, d.Attr1, d.Attr2, d.Attr3, d.Attr4, d.Attr5, d.Attr6 })
            .ToListAsync();

        return ServiceResult.Ok(units);
    }

    public async Task<ServiceResult> GetPendingAssignmentUnitsAsync(int assignmentId, int companyId, int userId, bool isDeveloper)
    {
        var assignment = await db.ArticleAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.CompanyId == companyId);
        if (assignment is null) return ServiceResult.NotFound();

        var visible = isDeveloper || assignment.AssignedByUserId == userId || assignment.RecipientUserId == userId ||
            await db.Inventories.AnyAsync(i => i.Id == assignment.InventoryId && i.CompanyId == companyId && i.UserId == userId);
        if (!visible) return ServiceResult.Forbid();

        var units = await db.ArticleUnitOperations.AsNoTracking()
            .Where(op => op.AssignmentId == assignmentId && op.CompanyId == companyId && !op.IsReversed)
            .Join(db.Set<ArticleDetails>(), op => op.ArticleDetailId, d => d.Id,
                (op, d) => new { id = d.Id, serialNumber = d.SerialNumber })
            .OrderBy(x => x.serialNumber)
            .ToListAsync();

        return ServiceResult.Ok(units);
    }

    public async Task<ServiceResult> AddUnitAsync(
        int articleId, RegisterUnitRequest input,
        int companyId, int userId, bool isDeveloper, string username)
    {
        if (string.IsNullOrWhiteSpace(input.SerialNumber) || input.SerialNumber.Length > 150)
            return ServiceResult.BadRequest("Serie inválida.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var article = await db.Articles.FromSqlInterpolated(
            $"SELECT * FROM \"Articles\" WHERE \"Id\" = {articleId} AND \"CompanyId\" = {companyId} FOR UPDATE")
            .SingleOrDefaultAsync();
        if (article is null || !article.IsActive) return ServiceResult.NotFound();

        if (!await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true, excludeMaster: false).ContainsAsync(article.InventoryId))
            return ServiceResult.Forbid();

        if (article.Tracking.ToString() != "Serialized")
            return ServiceResult.BadRequest("Solo artículos serializados.");

        var serial = input.SerialNumber.Trim();
        if (await db.Set<ArticleDetails>().AnyAsync(d => d.ArticleId == articleId &&
            d.SerialNumber.ToUpper() == serial.ToUpper()))
            return ServiceResult.Conflict("Serie repetida en este artículo.");

        var category = await db.Categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == article.CategoryId && c.CompanyId == companyId && c.IsActive);
        if (category is null)
            return ServiceResult.Conflict("La categoría del artículo ya no está disponible.");

        var uniqueRules = new (string? Label, bool IsUnique, string? Value)[]
        {
            (category.Label1, category.IsUnique1, input.Attr1),
            (category.Label2, category.IsUnique2, input.Attr2),
            (category.Label3, category.IsUnique3, input.Attr3),
            (category.Label4, category.IsUnique4, input.Attr4),
            (category.Label5, category.IsUnique5, input.Attr5),
            (category.Label6, category.IsUnique6, input.Attr6)
        };

        var existingAttributes = await db.Set<ArticleDetails>().AsNoTracking()
            .Where(d => d.ArticleId == articleId)
            .Select(d => new { d.Attr1, d.Attr2, d.Attr3, d.Attr4, d.Attr5, d.Attr6 })
            .ToListAsync();

        for (var index = 0; index < uniqueRules.Length; index++)
        {
            var rule = uniqueRules[index];
            if (!rule.IsUnique || string.IsNullOrWhiteSpace(rule.Label)) continue;
            if (string.IsNullOrWhiteSpace(rule.Value))
                return ServiceResult.BadRequest($"El atributo «{rule.Label}» es obligatorio para esta unidad.");

            var value = rule.Value.Trim();
            var alreadyUsed = existingAttributes.Any(row =>
            {
                var previous = index switch
                {
                    0 => row.Attr1,
                    1 => row.Attr2,
                    2 => row.Attr3,
                    3 => row.Attr4,
                    4 => row.Attr5,
                    _ => row.Attr6
                };
                return string.Equals(previous?.Trim(), value, StringComparison.OrdinalIgnoreCase);
            });
            if (alreadyUsed)
                return ServiceResult.Conflict($"El atributo «{rule.Label}» ya está utilizado en otra unidad.");
        }

        var now = DateTime.UtcNow;
        var detail = new ArticleDetails
        {
            ArticleId = articleId,
            SerialNumber = serial,
            Attr1 = input.Attr1?.Trim(),
            Attr2 = input.Attr2?.Trim(),
            Attr3 = input.Attr3?.Trim(),
            Attr4 = input.Attr4?.Trim(),
            Attr5 = input.Attr5?.Trim(),
            Attr6 = input.Attr6?.Trim(),
            StatusId = article.StatusId,
            RegistrationDate = now,
            IsActive = true
        };

        db.Set<ArticleDetails>().Add(detail);
        article.Stock += 1;
        article.ModificationDate = now;
        db.Movements.Add(new Movement
        {
            ArticleId = articleId,
            CompanyId = companyId,
            ActionId = 1,
            Amount = 1,
            MovementDate = now,
            Observation = "Ingreso de unidad serializada: " + serial
        });
        db.HistoryLogs.Add(new HistoryLog
        {
            LogDate = now,
            Username = username,
            ModuleName = "Inventario",
            ActionName = "Ingreso de serie",
            Detail = $"Se registró la serie «{serial}» del artículo «{article.Name}».",
            CompanyId = companyId
        });

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ServiceResult.Ok(new { detail.Id, detail.SerialNumber });
    }

    public async Task<ServiceResult> AddUnitsBatchAsync(
        int articleId, RegisterUnitBatchRequest input,
        int companyId, int userId, bool isDeveloper, int? employeeId, string username)
    {
        var units = input?.Units;
        if (units is null || units.Count is < 1 or > 1000)
            return ServiceResult.BadRequest("El lote debe contener entre 1 y 1000 unidades.");

        var normalized = units.Select(x => new RegisterUnitRequest(
            (x.SerialNumber ?? string.Empty).Trim(),
            x.Attr1?.Trim(), x.Attr2?.Trim(), x.Attr3?.Trim(),
            x.Attr4?.Trim(), x.Attr5?.Trim(), x.Attr6?.Trim())).ToList();

        if (normalized.Any(x => string.IsNullOrWhiteSpace(x.SerialNumber) || x.SerialNumber.Length > 150))
            return ServiceResult.BadRequest("Todas las unidades necesitan una serie válida de hasta 150 caracteres.");

        var submitted = normalized.Select(x => x.SerialNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (submitted.Count != normalized.Count)
            return ServiceResult.BadRequest("El lote contiene números de serie duplicados.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var article = await db.Articles.FromSqlInterpolated(
            $"SELECT * FROM \"Articles\" WHERE \"Id\" = {articleId} AND \"CompanyId\" = {companyId} FOR UPDATE")
            .SingleOrDefaultAsync();
        if (article is null || !article.IsActive) return ServiceResult.NotFound();

        if (!await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true, excludeMaster: false).ContainsAsync(article.InventoryId))
            return ServiceResult.Forbid();

        if (article.Tracking.ToString() != "Serialized")
            return ServiceResult.BadRequest("Solo se permiten lotes para artículos serializados.");

        var category = await db.Categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == article.CategoryId && c.CompanyId == companyId && c.IsActive);
        if (category is null)
            return ServiceResult.Conflict("La categoría del artículo ya no está disponible.");

        var existingDetails = await db.Set<ArticleDetails>().AsNoTracking()
            .Where(d => d.ArticleId == articleId)
            .ToListAsync();
        var existing = existingDetails.Select(d => d.SerialNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicate = normalized.FirstOrDefault(x => existing.Contains(x.SerialNumber));
        if (duplicate is not null)
            return ServiceResult.Conflict($"La serie «{duplicate.SerialNumber}» ya está registrada en este artículo.");

        var uniqueRules = new (string? Label, bool IsUnique, Func<RegisterUnitRequest, string?> GetValue,
            Func<ArticleDetails, string?> GetExisting)[]
        {
            (category.Label1, category.IsUnique1, x => x.Attr1, x => x.Attr1),
            (category.Label2, category.IsUnique2, x => x.Attr2, x => x.Attr2),
            (category.Label3, category.IsUnique3, x => x.Attr3, x => x.Attr3),
            (category.Label4, category.IsUnique4, x => x.Attr4, x => x.Attr4),
            (category.Label5, category.IsUnique5, x => x.Attr5, x => x.Attr5),
            (category.Label6, category.IsUnique6, x => x.Attr6, x => x.Attr6)
        };

        foreach (var rule in uniqueRules)
        {
            if (!rule.IsUnique || string.IsNullOrWhiteSpace(rule.Label)) continue;
            var values = normalized.Select(rule.GetValue)
                .Select(x => x?.Trim() ?? string.Empty).ToList();
            if (values.Any(string.IsNullOrWhiteSpace))
                return ServiceResult.BadRequest($"Todas las unidades necesitan completar el atributo «{rule.Label}».");

            var normalizedValues = values.Select(x => x.ToUpperInvariant()).ToList();
            if (normalizedValues.Distinct(StringComparer.Ordinal).Count() != normalizedValues.Count)
                return ServiceResult.BadRequest($"El atributo «{rule.Label}» debe ser diferente para cada unidad del lote.");

            var previouslyUsed = existingDetails.Select(rule.GetExisting)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var conflict = values.FirstOrDefault(previouslyUsed.Contains);
            if (conflict is not null)
                return ServiceResult.Conflict($"El atributo «{rule.Label}» con valor «{conflict}» ya está utilizado en otra unidad.");
        }

        var now = DateTime.UtcNow;
        var details = normalized.Select(x => new ArticleDetails
        {
            ArticleId = articleId,
            SerialNumber = x.SerialNumber,
            Attr1 = x.Attr1,
            Attr2 = x.Attr2,
            Attr3 = x.Attr3,
            Attr4 = x.Attr4,
            Attr5 = x.Attr5,
            Attr6 = x.Attr6,
            StatusId = article.StatusId,
            RegistrationDate = now,
            IsActive = true
        }).ToList();

        db.Set<ArticleDetails>().AddRange(details);
        article.Stock += details.Count;
        article.ModificationDate = now;

        db.Movements.Add(new Movement
        {
            ArticleId = articleId,
            EmployeeId = employeeId,
            CompanyId = companyId,
            ActionId = 1,
            Amount = details.Count,
            MovementDate = now,
            SalePrice = 0,
            PaymentMethod = "N/A",
            Recipient = "Almacén Local",
            Observation = $"Ingreso en lote de {details.Count} unidades serializadas por {username}."
        });
        db.HistoryLogs.Add(new HistoryLog
        {
            LogDate = now,
            Username = username,
            ModuleName = "Inventario",
            ActionName = "Ingreso de series",
            Detail = $"Se registraron {details.Count} unidades serializadas del artículo «{article.Name}».",
            CompanyId = companyId
        });

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ServiceResult.Ok(new
        {
            added = details.Count,
            stock = article.Stock,
            units = details.Select(x => new { x.Id, x.SerialNumber })
        });
    }

    public async Task<ServiceResult> DecommissionUnitAsync(
        int articleId, DecommissionUnitRequest input,
        int companyId, int userId, bool isDeveloper, int? employeeId, string username)
    {
        var reason = input?.Observation?.Trim() ?? string.Empty;
        if (input is null || input.DetailId <= 0 || reason.Length is < 5 or > 500)
            return ServiceResult.BadRequest("Indica la unidad y un motivo de baja de entre 5 y 500 caracteres.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var article = await db.Articles.FromSqlInterpolated(
            $"SELECT * FROM \"Articles\" WHERE \"Id\" = {articleId} AND \"CompanyId\" = {companyId} FOR UPDATE")
            .SingleOrDefaultAsync();
        if (article is null) return ServiceResult.NotFound();

        if (!await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true, excludeMaster: false).ContainsAsync(article.InventoryId))
            return ServiceResult.Forbid();

        if (!article.IsActive || article.Tracking.ToString() != "Serialized")
            return ServiceResult.Conflict("El artículo debe estar activo y ser serializado.");

        var detail = await db.Set<ArticleDetails>().FirstOrDefaultAsync(d =>
            d.Id == input.DetailId && d.ArticleId == articleId);
        if (detail is null) return ServiceResult.NotFound("La unidad no pertenece a este artículo.");
        if (!detail.IsActive) return ServiceResult.Conflict("La unidad ya fue retirada.");

        if (await db.ArticleUnitOperations.AnyAsync(op =>
            op.ArticleDetailId == detail.Id && op.CompanyId == companyId && !op.IsReversed))
            return ServiceResult.Conflict("La unidad tiene una venta o asignación vigente. Cierra o revierte esa operación antes de darla de baja.");

        if (article.Stock < 1)
            return ServiceResult.Conflict("El stock del artículo no permite retirar otra unidad.");

        var now = DateTime.UtcNow;
        detail.IsActive = false;
        article.Stock -= 1;
        article.ModificationDate = now;

        db.Movements.Add(new Movement
        {
            ArticleId = articleId,
            EmployeeId = employeeId,
            CompanyId = companyId,
            ActionId = 2,
            Amount = 1,
            MovementDate = now,
            SalePrice = 0,
            PaymentMethod = "N/A",
            Recipient = "Baja definitiva",
            Observation = $"Baja definitiva de serie {detail.SerialNumber}. Motivo: {reason}"
        });
        db.HistoryLogs.Add(new HistoryLog
        {
            LogDate = now,
            Username = username,
            ModuleName = "Inventario",
            ActionName = "Baja de unidad serializada",
            Detail = $"Serie «{detail.SerialNumber}» del artículo «{article.Name}». Motivo: {reason}",
            CompanyId = companyId
        });

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ServiceResult.Ok(new { detail.Id, detail.SerialNumber, detail.IsActive, stock = article.Stock });
    }
}