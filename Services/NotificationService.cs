using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services;

public sealed class NotificationService(IWorkFlow workFlow, AppDbContext db)
    : WorkContainer<Notification>(workFlow), INotificationService
{
    public Task<Notification?> GetByIdInCompanyAsync(int id, int companyId) =>
        db.Notifications.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id && n.CompanyId == companyId);
}