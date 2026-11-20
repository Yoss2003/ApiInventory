using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services;

public sealed class InventoryScopeService(AppDbContext db) : IInventoryScopeService
{
    public IQueryable<int> GetAccessibleInventoryIds(
        int companyId,
        int userId,
        bool isDeveloper,
        bool requireWrite = false,
        bool excludeMaster = true)
    {
        return db.Inventories
            .AsNoTracking()
            .Where(i => i.CompanyId == companyId &&
                (!excludeMaster || !i.IsMaster) &&
                (isDeveloper || i.UserId == userId || db.SharedInventories.Any(s =>
                    s.InventoryId == i.Id &&
                    s.CompanyId == companyId &&
                    s.UserId == userId &&
                    (!requireWrite || s.AccessLevel == SharedInventory.AccessMode.Editor))))
            .Select(i => i.Id);
    }

    public Task<int?> GetActiveMasterInventoryIdAsync(int companyId, CancellationToken ct = default)
    {
        return db.Inventories
            .AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.IsMaster && i.IsActive)
            .Select(i => (int?)i.Id)
            .FirstOrDefaultAsync(ct);
    }
}