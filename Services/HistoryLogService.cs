using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services;

public sealed class HistoryLogService(IWorkFlow workFlow, AppDbContext db)
    : WorkContainer<HistoryLog>(workFlow), IHistoryLogService
{
    public Task<HistoryLog?> GetByIdInCompanyAsync(int id, int companyId)
    {
        if (id <= 0 || companyId <= 0) return Task.FromResult<HistoryLog?>(null);
        return db.HistoryLogs.AsNoTracking()
            .FirstOrDefaultAsync(log => log.Id == id && log.CompanyId == companyId);
    }

    public async Task<bool> CreateInCompanyAsync(HistoryLog log, int companyId, int userId)
    {
        if (companyId <= 0 || userId <= 0) return false;

        var username = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId && user.CompanyId == companyId)
            .Select(user => user.Username)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(username)) return false;

        log.Id = 0;
        log.CompanyId = companyId;
        log.Username = username;
        log.LogDate = DateTime.UtcNow;

        await _workFlow.Repository<HistoryLog>().AddAsync(log);
        return await _workFlow.CompleteAsync() > 0;
    }
}