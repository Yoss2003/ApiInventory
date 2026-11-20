using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services;

public sealed class AssignmentService(
    AppDbContext db,
    IWebHostEnvironment environment,
    IPrivateMediaStorage storage,
    IPessimisticLockRepository locks,
    ILogger<AssignmentService> logger) : IAssignmentService
{
    private IQueryable<ArticleAssignment> Visible(int companyId, int userId, bool isDeveloper) =>
        db.ArticleAssignments.AsNoTracking().Where(a => a.CompanyId == companyId &&
            (isDeveloper || a.AssignedByUserId == userId || a.RecipientUserId == userId ||
             db.Inventories.Any(i => i.Id == a.InventoryId && i.UserId == userId)));

    private IQueryable<AssignmentSummary> Display(IQueryable<ArticleAssignment> query) =>
        query.Select(a => new AssignmentSummary(
            a.Id, a.ArticleId, a.InventoryId, a.RecipientName, a.RecipientUserId,
            a.AssignedByUserId, a.AssignedAt, a.Quantity, a.ReturnedQuantity,
            a.Quantity - a.ReturnedQuantity,
            a.Quantity <= a.ReturnedQuantity ? "Devuelto" : "Asignado",
            a.Observation, a.EvidencePath != null,
            db.Articles.Where(t => t.Id == a.ArticleId).Select(t => t.Name).FirstOrDefault(),
            db.Articles.Where(t => t.Id == a.ArticleId).Select(t => t.Code).FirstOrDefault(),
            db.Articles.Where(t => t.Id == a.ArticleId).Select(t => t.MeasurementUnit).FirstOrDefault()));

    public async Task<List<AssignmentSummary>> GetOutgoingAsync(int companyId, int userId, bool isDeveloper)
    {
        var query = Visible(companyId, userId, isDeveloper).Where(a => isDeveloper || a.AssignedByUserId == userId ||
            db.Inventories.Any(i => i.Id == a.InventoryId && i.UserId == userId));
        return await Display(query.OrderByDescending(a => a.AssignedAt)).ToListAsync();
    }

    public async Task<List<AssignmentSummary>> GetReceivedAsync(int companyId, int userId)
    {
        return await Display(db.ArticleAssignments.AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.RecipientUserId == userId)
            .OrderByDescending(a => a.AssignedAt)).ToListAsync();
    }

    public async Task<ServiceResult> GetEventsAsync(int id, int companyId, int userId, bool isDeveloper)
    {
        if (!await Visible(companyId, userId, isDeveloper).AnyAsync(a => a.Id == id))
            return ServiceResult.NotFound();

        var events = await db.ArticleAssignmentEvents.AsNoTracking()
            .Where(e => e.AssignmentId == id && e.CompanyId == companyId)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new { e.Id, e.Kind, e.Quantity, e.OccurredAt, e.Observation, e.ActorUserId, e.MovementId })
            .ToListAsync();
        return ServiceResult.Ok(events);
    }

    public async Task<object> GetRecipientsAsync(int companyId, int userId, string? query)
    {
        var users = db.Users.AsNoTracking().Where(u => u.CompanyId == companyId && u.Id != userId);
        if (!string.IsNullOrWhiteSpace(query))
            users = users.Where(u => u.Username != null && EF.Functions.ILike(u.Username, "%" + query.Trim() + "%"));

        return await users.OrderBy(u => u.Username)
            .Take(30)
            .Select(u => new { id = u.Id, name = u.Username })
            .ToListAsync();
    }

    public async Task<ServiceResult> AssignAsync(AssignRequest request, int companyId, int userId, bool isDeveloper)
    {
        if (request.OperationKey == Guid.Empty || request.Quantity <= 0 ||
            string.IsNullOrWhiteSpace(request.RecipientName))
            return ServiceResult.BadRequest("Artículo, cantidad, destinatario y operación son obligatorios.");

        var prior = await db.ArticleAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.OperationKey == request.OperationKey);
        if (prior != null)
            return prior.CompanyId == companyId && prior.AssignedByUserId == userId
                ? ServiceResult.Ok(new { id = prior.Id, reused = true })
                : ServiceResult.Conflict("Operación duplicada.");

        if (request.RecipientUserId is int recipientId &&
            !await db.Users.AnyAsync(u => u.Id == recipientId && u.CompanyId == companyId && u.Id != userId))
            return ServiceResult.BadRequest("El usuario destinatario no pertenece a tu empresa.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var article = await locks.LockArticleAsync(request.ArticleId, companyId);
        if (article == null || !article.IsActive)
            return ServiceResult.NotFound("Artículo no encontrado o inactivo.");

        if (!await db.Inventories.AnyAsync(i => i.Id == article.InventoryId && i.CompanyId == companyId && (i.UserId == userId || isDeveloper)))
            return ServiceResult.Forbid();

        if (article.Stock < request.Quantity)
            return ServiceResult.Conflict("Stock insuficiente.");

        if (article.Tracking.ToString() == "Serialized" && request.Quantity != decimal.Truncate(request.Quantity))
            return ServiceResult.BadRequest("La cantidad serializada debe ser entera.");

        var detailIds = request.DetailIds ?? [];
        if (article.Tracking.ToString() == "Serialized")
        {
            if (detailIds.Count != (int)request.Quantity || detailIds.Distinct().Count() != detailIds.Count)
                return ServiceResult.BadRequest("Selecciona exactamente una serie distinta por unidad asignada.");

            var available = await db.Set<ArticleDetails>().CountAsync(d => d.ArticleId == article.Id && d.IsActive &&
                detailIds.Contains(d.Id) && !db.ArticleUnitOperations.Any(op => op.ArticleDetailId == d.Id && !op.IsReversed));
            if (available != detailIds.Count)
                return ServiceResult.Conflict("Una de las series ya está vendida o asignada.");
        }
        else if (detailIds.Count > 0)
        {
            return ServiceResult.BadRequest("Este artículo no usa series.");
        }

        article.Stock -= request.Quantity;
        article.ModificationDate = DateTime.UtcNow;

        var assignment = new ArticleAssignment
        {
            OperationKey = request.OperationKey,
            CompanyId = companyId,
            InventoryId = article.InventoryId,
            ArticleId = article.Id,
            RecipientUserId = request.RecipientUserId,
            RecipientName = request.RecipientName.Trim(),
            AssignedByUserId = userId,
            Quantity = request.Quantity,
            Observation = request.Observation?.Trim(),
            AssignedAt = DateTime.UtcNow
        };

        var employeeId = await db.Employees
            .Where(e => e.CompanyId == companyId && e.UserId == userId)
            .Select(e => (int?)e.Id)
            .FirstOrDefaultAsync();

        var movement = new Movement
        {
            ArticleId = article.Id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            ActionId = 3,
            MovementDate = DateTime.UtcNow,
            Amount = request.Quantity,
            Recipient = assignment.RecipientName,
            Observation = $"Entrega de custodia. {request.Observation}".Trim(),
            SalePrice = 0m
        };

        db.ArticleAssignments.Add(assignment);
        db.Movements.Add(movement);
        await db.SaveChangesAsync();

        foreach (var detailId in detailIds)
        {
            db.ArticleUnitOperations.Add(new ArticleUnitOperation
            {
                CompanyId = companyId,
                ArticleId = article.Id,
                ArticleDetailId = detailId,
                AssignmentId = assignment.Id,
                CreatedAt = DateTime.UtcNow
            });
        }

        db.ArticleAssignmentEvents.Add(new ArticleAssignmentEvent
        {
            AssignmentId = assignment.Id,
            CompanyId = companyId,
            ActorUserId = userId,
            Kind = "Entrega",
            Quantity = request.Quantity,
            OccurredAt = DateTime.UtcNow,
            MovementId = movement.Id,
            Observation = request.Observation,
            OperationKey = request.OperationKey
        });

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Created(new CreatedAssignmentResponse(assignment.Id));
    }

    public async Task<ServiceResult> UploadEvidenceAsync(int id, IFormFile file, int companyId, int userId, CancellationToken ct)
    {
        var assignment = await db.ArticleAssignments.FirstOrDefaultAsync(a => a.Id == id && a.CompanyId == companyId, ct);
        if (assignment is null) return ServiceResult.NotFound();
        if (assignment.AssignedByUserId != userId) return ServiceResult.Forbid();
        if (file.Length is <= 0 or > 8_000_000)
            return ServiceResult.BadRequest("Imagen inválida (máx. 8 MB).");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp") || !await IsValidEvidenceAsync(file, ext, ct))
            return ServiceResult.BadRequest("Solo se aceptan imágenes JPG, PNG o WEBP válidas.");
        if (!storage.IsConfigured)
            return ServiceResult.ServiceUnavailable("Almacenamiento privado no configurado en la API.");

        var previous = assignment.EvidencePath;
        string? uploaded = null;
        try
        {
            var key = PrivateMediaStorage.NewKey(companyId, "assignments", id, "evidence", ext);
            var mime = ext switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
            uploaded = await storage.UploadAsync(key, file, mime, ct);
            assignment.EvidencePath = uploaded;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo almacenar la evidencia privada de la custodia {AssignmentId}", id);
            if (uploaded is not null)
            {
                try { await storage.DeleteAsync(uploaded, CancellationToken.None); }
                catch (Exception cleanupError) { logger.LogWarning(cleanupError, "Objeto de evidencia huérfano pendiente de limpieza"); }
            }
            return ServiceResult.BadGateway("No se pudo guardar la evidencia en almacenamiento privado.");
        }

        if (PrivateMediaStorage.IsCloudReference(previous))
        {
            try { await storage.DeleteAsync(previous!, CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "No se pudo eliminar evidencia reemplazada {AssignmentId}", id); }
        }

        return ServiceResult.Ok(new { evidence = true });
    }

    public async Task<MediaFileResult> GetEvidenceAsync(int id, int companyId, int userId, bool isDeveloper, CancellationToken ct)
    {
        var assignment = await Visible(companyId, userId, isDeveloper).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (assignment is null || string.IsNullOrWhiteSpace(assignment.EvidencePath))
            return new MediaFileResult(ServiceResult.NotFound());

        var reference = assignment.EvidencePath;
        if (PrivateMediaStorage.IsCloudReference(reference))
        {
            if (!storage.IsConfigured)
                return new MediaFileResult(ServiceResult.ServiceUnavailable("Almacenamiento privado no configurado en la API."));
            try
            {
                var remote = await storage.DownloadAsync(reference, ct);
                if (remote is null) return new MediaFileResult(ServiceResult.NotFound());
                var ext = Path.GetExtension(reference).ToLowerInvariant();
                var type = ext switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
                return new MediaFileResult(null, Bytes: remote.Value.Content, ContentType: type);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo consultar la evidencia remota {AssignmentId}", id);
                return new MediaFileResult(ServiceResult.BadGateway("No se pudo recuperar la evidencia privada."));
            }
        }

        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "PrivateFiles", "assignments"));
        var full = Path.GetFullPath(Path.Combine(root, reference.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full))
            return new MediaFileResult(ServiceResult.NotFound());

        var extension = Path.GetExtension(full).ToLowerInvariant();
        var mimeType = extension switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
        return new MediaFileResult(null, PhysicalPath: full, ContentType: mimeType);
    }

    public async Task<ServiceResult> ReturnAsync(int id, ReturnRequest request, int companyId, int userId, bool isDeveloper)
    {
        if (request.OperationKey == Guid.Empty || request.Quantity <= 0)
            return ServiceResult.BadRequest("Cantidad y clave de operación son obligatorias.");

        var old = await db.ArticleAssignmentEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.OperationKey == request.OperationKey);
        if (old != null)
            return old.CompanyId == companyId && old.AssignmentId == id
                ? ServiceResult.Ok(new { reused = true })
                : ServiceResult.Conflict("Operación duplicada.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var assignment = await locks.LockAssignmentAsync(id, companyId);
        if (assignment is null) return ServiceResult.NotFound();

        var owns = isDeveloper || assignment.AssignedByUserId == userId ||
            await db.Inventories.AnyAsync(i => i.Id == assignment.InventoryId && i.UserId == userId && i.CompanyId == companyId);
        if (!owns && assignment.RecipientUserId != userId)
            return ServiceResult.Forbid();

        if (assignment.Quantity - assignment.ReturnedQuantity < request.Quantity)
            return ServiceResult.Conflict("La devolución supera el saldo pendiente.");

        var article = await locks.LockArticleAsync(assignment.ArticleId, companyId);
        if (article is null)
            return ServiceResult.NotFound("Artículo original no encontrado.");

        var returningIds = request.DetailIds ?? [];
        if (article.Tracking.ToString() == "Serialized")
        {
            if (returningIds.Count != (int)request.Quantity || returningIds.Distinct().Count() != returningIds.Count)
                return ServiceResult.BadRequest("Selecciona una serie por cada unidad devuelta.");

            var units = await db.ArticleUnitOperations
                .Where(u => u.AssignmentId == id && !u.IsReversed && returningIds.Contains(u.ArticleDetailId))
                .ToListAsync();
            if (units.Count != returningIds.Count)
                return ServiceResult.Conflict("Una serie no pertenece a esta custodia o ya fue devuelta.");

            foreach (var unit in units)
            {
                unit.IsReversed = true;
                unit.ReversedAt = DateTime.UtcNow;
            }
        }
        else if (returningIds.Count > 0)
        {
            return ServiceResult.BadRequest("Artículo sin series.");
        }

        article.Stock += request.Quantity;
        article.ModificationDate = DateTime.UtcNow;
        assignment.ReturnedQuantity += request.Quantity;

        var employeeId = await db.Employees
            .Where(e => e.CompanyId == companyId && e.UserId == userId)
            .Select(e => (int?)e.Id)
            .FirstOrDefaultAsync();

        var movement = new Movement
        {
            ArticleId = article.Id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            ActionId = 1,
            MovementDate = DateTime.UtcNow,
            Amount = request.Quantity,
            Recipient = assignment.RecipientName,
            Observation = $"Devolución de custodia #{id}. {request.Observation}".Trim()
        };

        db.Movements.Add(movement);
        await db.SaveChangesAsync();

        db.ArticleAssignmentEvents.Add(new ArticleAssignmentEvent
        {
            AssignmentId = assignment.Id,
            CompanyId = companyId,
            ActorUserId = userId,
            Kind = "Devolucion",
            Quantity = request.Quantity,
            OccurredAt = DateTime.UtcNow,
            MovementId = movement.Id,
            Observation = request.Observation,
            OperationKey = request.OperationKey
        });

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return ServiceResult.Ok(new
        {
            assignment.Id,
            assignment.ReturnedQuantity,
            remainingQuantity = assignment.Quantity - assignment.ReturnedQuantity
        });
    }

    private static async Task<bool> IsValidEvidenceAsync(IFormFile file, string ext, CancellationToken ct)
    {
        await using var input = file.OpenReadStream();
        var magic = new byte[12];
        var read = await input.ReadAsync(magic, ct);
        var jpg = read >= 3 && magic[0] == 0xFF && magic[1] == 0xD8 && magic[2] == 0xFF;
        var png = read >= 8 && magic.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var webp = read >= 12 && System.Text.Encoding.ASCII.GetString(magic, 0, 4) == "RIFF" &&
            System.Text.Encoding.ASCII.GetString(magic, 8, 4) == "WEBP";
        return (jpg && ext is ".jpg" or ".jpeg") || (png && ext == ".png") || (webp && ext == ".webp");
    }
}