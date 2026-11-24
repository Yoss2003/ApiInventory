using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services;

public class InventoryService(IWorkFlow workFlow, AppDbContext db)
    : WorkContainer<Inventory>(workFlow), IInventoryService
{
    public async Task<List<Inventory>> GetVisibleInventoriesAsync(int companyId, int userId, bool isDeveloper, bool viewMaster)
    {
        return await db.Inventories.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.IsActive &&
                (i.IsMaster ? viewMaster :
                    (isDeveloper || i.UserId == userId ||
                     db.SharedInventories.Any(s => s.InventoryId == i.Id &&
                         s.UserId == userId && s.CompanyId == companyId))))
            .OrderBy(i => i.Alias ?? i.InventoryName)
            .ToListAsync();
    }

    public async Task<List<Inventory>> GetCompanyOperationalAsync(int companyId)
    {
        return await db.Inventories.AsNoTracking()
            .Where(i => i.CompanyId == companyId && !i.IsMaster)
            .OrderBy(i => i.Alias ?? i.InventoryName)
            .ToListAsync();
    }

    public async Task<object> GetMasterInventoriesAsync(int companyId, bool isDeveloper)
    {
        return await db.Inventories.AsNoTracking()
            .Where(i => i.IsMaster && (isDeveloper || i.CompanyId == companyId))
            .Join(db.Companies.AsNoTracking(),
                i => i.CompanyId, c => c.Id,
                (i, c) => new
                {
                    i.Id,
                    i.CompanyId,
                    companyName = c.BusinessName,
                    i.InventoryName,
                    i.Alias,
                    i.IsMaster,
                    i.IsActive
                })
            .OrderBy(i => i.companyName)
            .ToListAsync();
    }

    public async Task<ServiceResult> GetMasterCatalogsAsync(int masterId, int companyId, bool isDeveloper)
    {
        var master = await db.Inventories.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == masterId && i.IsMaster &&
                (isDeveloper || i.CompanyId == companyId));
        if (master is null) return ServiceResult.NotFound();

        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.CompanyId == master.CompanyId && c.InventoryId == masterId && c.IsActive)
            .OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.ParentCategoryId })
            .ToListAsync();
        var brands = await db.Brands.AsNoTracking()
            .Where(b => b.CompanyId == master.CompanyId && b.InventoryId == masterId && b.IsActive)
            .OrderBy(b => b.Name).Select(b => new { b.Id, b.Name, b.CategoryId })
            .ToListAsync();
        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(p => p.CompanyId == master.CompanyId && p.InventoryId == masterId && p.IsActive)
            .OrderBy(p => p.BusinessName)
            .Select(p => new { p.Id, p.Ruc, p.BusinessName, p.ContactName, p.Email, p.Phone })
            .ToListAsync();

        return ServiceResult.Ok(new
        {
            master.Id,
            master.CompanyId,
            master.InventoryName,
            categories,
            brands,
            suppliers
        });
    }

    public async Task<Inventory?> GetVisibleInventoryByIdAsync(int id, int companyId, int userId, bool isDeveloper, bool viewMaster)
    {
        return await db.Inventories.AsNoTracking().FirstOrDefaultAsync(i =>
            i.Id == id && i.CompanyId == companyId && i.IsActive &&
            (i.IsMaster ? viewMaster :
                (isDeveloper || i.UserId == userId ||
                 db.SharedInventories.Any(s => s.InventoryId == i.Id &&
                    s.UserId == userId && s.CompanyId == companyId))));
    }

    public async Task<ServiceResult> CreatePersonalInventoryAsync(Inventory inventory, int companyId, int userId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.CompanyId == companyId && u.IsActive))
            return ServiceResult.Conflict("Selecciona un propietario de esta empresa mediante el alta administrada de inventario.");
        if (string.IsNullOrWhiteSpace(inventory.InventoryName))
            return ServiceResult.BadRequest("El nombre del inventario es obligatorio.");

        inventory.Id = 0;
        inventory.CompanyId = companyId;
        inventory.UserId = userId;
        inventory.IsMaster = false;
        inventory.CreationDate = DateTime.UtcNow;
        inventory.ModificationDate = DateTime.UtcNow;
        inventory.IsActive = true;
        inventory.InventoryName = inventory.InventoryName.Trim();
        inventory.Alias = inventory.Alias?.Trim();
        inventory.Username = await db.Users.Where(u => u.Id == userId)
            .Select(u => u.Username).FirstOrDefaultAsync() ?? string.Empty;

        db.Inventories.Add(inventory);
        await db.SaveChangesAsync();
        return ServiceResult.Created(inventory);
    }

    public async Task<ServiceResult> AssignInventoryAsync(int ownerUserId, string inventoryName, string? alias, int companyId)
    {
        if (ownerUserId <= 0 || string.IsNullOrWhiteSpace(inventoryName) || inventoryName.Trim().Length > 255)
            return ServiceResult.BadRequest("Indica un propietario y nombre válidos.");

        var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u =>
            u.Id == ownerUserId && u.CompanyId == companyId && u.IsActive && u.StatusId == 1);
        if (owner is null)
            return ServiceResult.NotFound("El propietario no existe o no pertenece a la empresa.");

        var now = DateTime.UtcNow;
        var item = new Inventory
        {
            CompanyId = companyId,
            UserId = owner.Id,
            Username = owner.Username ?? string.Empty,
            InventoryName = inventoryName.Trim(),
            Alias = alias?.Trim(),
            CreationDate = now,
            ModificationDate = now,
            IsActive = true,
            IsMaster = false
        };
        db.Inventories.Add(item);
        await db.SaveChangesAsync();
        return ServiceResult.Ok(new { item.Id, item.CompanyId, item.UserId, item.InventoryName, item.Alias });
    }

    public async Task<ServiceResult> UpdateInventoryAsync(int id, Inventory update, int companyId, int userId, bool isManager)
    {
        if (id != update.Id) return ServiceResult.BadRequest("El ID no coincide.");

        var inventory = await db.Inventories.FirstOrDefaultAsync(i =>
            i.Id == id && i.CompanyId == companyId && (i.UserId == userId || isManager));
        if (inventory is null) return ServiceResult.NotFound();
        if (inventory.IsMaster)
            return ServiceResult.Conflict("El inventario maestro es administrado exclusivamente por el sistema.");
        if (!inventory.IsActive)
            return ServiceResult.Conflict("Inventario inactivo.");
        if (string.IsNullOrWhiteSpace(update.InventoryName))
            return ServiceResult.BadRequest("El nombre del inventario es obligatorio.");

        inventory.InventoryName = update.InventoryName.Trim();
        inventory.Alias = update.Alias?.Trim();
        inventory.ModificationDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ServiceResult.NoContent();
    }

    public async Task<ServiceResult> DeactivateInventoryAsync(int id, int companyId, int userId, bool isManager)
    {
        var inventory = await db.Inventories.FirstOrDefaultAsync(i =>
            i.Id == id && i.CompanyId == companyId && (i.UserId == userId || isManager));
        if (inventory is null) return ServiceResult.NotFound();
        if (inventory.IsMaster)
            return ServiceResult.Conflict("El inventario maestro es administrado exclusivamente por el sistema.");

        if (await db.Articles.AnyAsync(a => a.InventoryId == id) ||
            await db.Categories.AnyAsync(c => c.InventoryId == id) ||
            await db.Brands.AnyAsync(b => b.InventoryId == id) ||
            await db.SharedInventories.AnyAsync(sh => sh.InventoryId == id) ||
            await db.ArticleAssignments.AnyAsync(a => a.InventoryId == id))
            return ServiceResult.Conflict("Inventario referenciado por artículos, categorías, marcas, asignaciones o permisos. No se puede desactivar.");

        inventory.IsActive = false;
        inventory.ModificationDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ServiceResult.NoContent();
    }

    public async Task<ServiceResult> GetShareRecipientsAuthorizedAsync(int inventoryId, int companyId, int userId, bool isManager)
    {
        var source = await db.Inventories.AsNoTracking().FirstOrDefaultAsync(i =>
            i.Id == inventoryId && i.CompanyId == companyId && i.IsActive &&
            !i.IsMaster && (i.UserId == userId || isManager));
        if (source is null)
            return ServiceResult.NotFound(
                "No se encontró un inventario operativo activo de tu empresa que puedas compartir. Verifica que no sea el inventario maestro y que seas su propietario o administrador.");

        var recipients = await (
            from employee in db.Employees.AsNoTracking()
            join user in db.Users.AsNoTracking() on employee.UserId equals (int?)user.Id
            where employee.CompanyId == companyId && employee.IsActive &&
                  user.CompanyId == companyId && user.IsActive && user.StatusId == 1 &&
                  user.Id != source.UserId && user.Id != userId
            orderby employee.FirstName, employee.LastName, user.Username
            select new InventoryShareRecipientDto(
                user.Id,
                user.Username ?? string.Empty,
                user.Email,
                employee.FirstName,
                employee.LastName,
                employee.DNI,
                user.ProfilePictureUrl)
        ).ToListAsync();

        return ServiceResult.Ok(recipients);
    }

    public async Task<ServiceResult> ShareInventoryAuthorizedAsync(ShareRequestDTO request, int companyId, int userId, bool isManager)
    {
        var canShare = await db.Inventories.AnyAsync(i =>
            i.Id == request.InventoryId && i.CompanyId == companyId &&
            i.IsActive && !i.IsMaster && (i.UserId == userId || isManager));
        if (!canShare) return ServiceResult.Forbid();

        var result = await ShareInventoryAsync(request);
        return result.Success
            ? ServiceResult.Ok(new { message = result.Message })
            : ServiceResult.BadRequest(result.Message);
    }

    public async Task<ServiceResult> GetSharedInventoriesAuthorizedAsync(int inventoryId, int companyId, int userId, bool isManager)
    {
        var canView = await db.Inventories.AnyAsync(i =>
            i.Id == inventoryId && i.CompanyId == companyId &&
            i.IsActive && !i.IsMaster && (i.UserId == userId || isManager));
        if (!canView) return ServiceResult.Forbid();

        var records = await GetSharedInventoriesAsync(inventoryId);
        return ServiceResult.Ok(records.Select(x => new
        {
            x.Id,
            x.InventoryId,
            x.UserId,
            username = x.User?.Username,
            x.AccessLevel,
            x.SharedDate
        }));
    }

    public async Task<ServiceResult> UpdateShareAccessAsync(int sharedInventoryId, int accessLevel, int companyId, int userId, bool isManager)
    {
        if (accessLevel is not (1 or 2))
            return ServiceResult.BadRequest("El permiso debe ser 1 (Lector) o 2 (Editor).");

        var share = await db.SharedInventories.FirstOrDefaultAsync(s =>
            s.Id == sharedInventoryId && s.CompanyId == companyId &&
            db.Inventories.Any(i => i.Id == s.InventoryId && i.CompanyId == companyId &&
                i.IsActive && !i.IsMaster && (i.UserId == userId || isManager)));
        if (share is null) return ServiceResult.NotFound();

        share.AccessLevel = (SharedInventory.AccessMode)accessLevel;
        await db.SaveChangesAsync();
        return ServiceResult.Ok(new { share.Id, share.InventoryId, share.UserId, share.AccessLevel });
    }

    public async Task<ServiceResult> RevokeAccessAuthorizedAsync(int sharedInventoryId, int companyId, int userId, bool isManager)
    {
        var valid = await db.SharedInventories.AnyAsync(s =>
            s.Id == sharedInventoryId && s.CompanyId == companyId &&
            db.Inventories.Any(i => i.Id == s.InventoryId && i.CompanyId == companyId &&
                i.IsActive && !i.IsMaster && (i.UserId == userId || isManager)));
        if (!valid) return ServiceResult.Forbid();

        return await RevokeAccessAsync(sharedInventoryId)
            ? ServiceResult.NoContent()
            : ServiceResult.Conflict("No se pudo revocar el permiso.");
    }

    public async Task<(bool Success, string Message)> ShareInventoryAsync(ShareRequestDTO request)
    {
        if (request.InventoryId <= 0 ||
            (request.GuestUserId is null && string.IsNullOrWhiteSpace(request.GuestIdentifier)) ||
            (request.GuestUserId.HasValue && request.GuestUserId.Value <= 0) ||
            request.AccessLevel is not (SharedInventory.AccessMode.Lector or SharedInventory.AccessMode.Editor))
            return (false, "Selecciona un inventario, destinatario y permiso válidos.");

        var source = await db.Inventories.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.InventoryId && i.IsActive);
        if (source == null || source.IsMaster)
            return (false, "El inventario no existe, está desactivado o es un maestro no compartible.");

        User? recipient;
        if (request.GuestUserId is int guestUserId)
        {
            recipient = await db.Users.AsNoTracking().FirstOrDefaultAsync(u =>
                u.Id == guestUserId && u.IsActive && u.StatusId == 1 &&
                u.CompanyId == source.CompanyId);
        }
        else
        {
            string identifier = request.GuestIdentifier.Trim();
            recipient = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.IsActive && u.StatusId == 1 &&
                    u.CompanyId == source.CompanyId &&
                    (u.Username == identifier || u.Email == identifier ||
                     (u.Employee != null && u.Employee.DNI == identifier)));
        }
        if (recipient == null) return (false, "Destinatario no encontrado en la misma empresa.");
        if (source.UserId == recipient.Id) return (false, "No puedes compartir tu inventario contigo mismo.");
        if (await db.SharedInventories.AnyAsync(s => s.CompanyId == source.CompanyId &&
                s.InventoryId == source.Id && s.UserId == recipient.Id))
            return (false, "Este usuario ya tiene acceso al inventario.");

        db.SharedInventories.Add(new SharedInventory
        {
            CompanyId = source.CompanyId,
            InventoryId = source.Id,
            UserId = recipient.Id,
            AccessLevel = request.AccessLevel,
            SharedDate = DateTime.UtcNow
        });
        try
        {
            await db.SaveChangesAsync();
            return (true, "Inventario compartido con el usuario seleccionado.");
        }
        catch (DbUpdateException)
        {
            return (false, "No se pudo compartir: comprueba que el usuario no tenga ya acceso.");
        }
    }

    public async Task<IEnumerable<SharedInventory>> GetSharedInventoriesAsync(int inventoryId)
    {
        return await db.SharedInventories.AsNoTracking().Include(s => s.User)
            .Where(s => s.InventoryId == inventoryId)
            .OrderBy(s => s.UserId).ToListAsync();
    }

    public async Task<bool> RevokeAccessAsync(int sharedInventoryId)
    {
        var record = await db.SharedInventories.FindAsync(sharedInventoryId);
        if (record == null) return false;
        db.SharedInventories.Remove(record);
        return await db.SaveChangesAsync() > 0;
    }
}