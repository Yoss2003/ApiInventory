using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services;

public sealed class ExportRouteService(IWorkFlow workFlow, AppDbContext db)
    : WorkContainer<ExportRoute>(workFlow), IExportRouteService
{
    public Task<List<ExportRoute>> GetAllForUserAsync(int userId) =>
        db.ExportRoutes.AsNoTracking()
            .Where(route => route.UserId == userId)
            .OrderBy(route => route.Id)
            .ToListAsync();

    public Task<ExportRoute?> GetByIdForUserAsync(int id, int userId) =>
        db.ExportRoutes.AsNoTracking()
            .FirstOrDefaultAsync(route => route.Id == id && route.UserId == userId);

    public async Task<bool> CreateForUserAsync(ExportRoute route, int userId)
    {
        if (userId <= 0) return false;
        route.UserId = userId;
        await db.ExportRoutes.AddAsync(route);
        return await db.SaveChangesAsync() > 0;
    }

    public async Task<bool> UpdateForUserAsync(int id, ExportRoute route, int userId)
    {
        if (userId <= 0 || route.Id != id) return false;
        var existing = await db.ExportRoutes
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        if (existing is null) return false;

        existing.DefaultRoute1 = route.DefaultRoute1;
        existing.CustomRoute1 = route.CustomRoute1;
        existing.DefaultRoute2 = route.DefaultRoute2;
        existing.CustomRoute2 = route.CustomRoute2;
        existing.FileType1 = route.FileType1;
        existing.FileType2 = route.FileType2;
        existing.IsDefault1 = route.IsDefault1;
        existing.IsDefault2 = route.IsDefault2;

        return await db.SaveChangesAsync() > 0;
    }

    public async Task<bool> DeleteForUserAsync(int id, int userId, string deletedBy)
    {
        if (userId <= 0) return false;
        var existing = await db.ExportRoutes
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        if (existing is null) return false;

        db.ExportRoutes.Remove(existing);
        return await db.SaveChangesAsync() > 0;
    }
}