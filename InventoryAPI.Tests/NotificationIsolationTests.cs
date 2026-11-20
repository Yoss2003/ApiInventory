using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Tests;

public sealed class NotificationIsolationTests
{
    [Fact]
    public async Task GetByIdInCompanyAsync_DoesNotReturnNotificationFromAnotherCompany()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        db.Notifications.AddRange(
            new Notification { Id = 1, CompanyId = 1, NotificationName = "Empresa 1" },
            new Notification { Id = 2, CompanyId = 2, NotificationName = "Empresa 2" });
        await db.SaveChangesAsync();

        var service = new NotificationService(new WorkFlow(db), db);

        Assert.Equal("Empresa 1", (await service.GetByIdInCompanyAsync(1, 1))?.NotificationName);
        Assert.Null(await service.GetByIdInCompanyAsync(2, 1));
        Assert.Null(await service.GetByIdInCompanyAsync(1, 2));
        Assert.Null(await service.GetByIdInCompanyAsync(1, 0));
    }
}