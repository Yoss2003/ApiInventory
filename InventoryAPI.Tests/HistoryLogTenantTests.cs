using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Tests;

public sealed class HistoryLogTenantTests
{
    [Fact]
    public async Task HistoryLog_ReadAndCreate_AreBoundToAuthenticatedCompanyAndUser()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        db.Users.AddRange(
            new User { Id = 10, Username = "empresa-a", CompanyId = 1, RoleId = 3, IsActive = true, StatusId = 1 },
            new User { Id = 20, Username = "empresa-b", CompanyId = 2, RoleId = 3, IsActive = true, StatusId = 1 });
        db.HistoryLogs.Add(new HistoryLog
        {
            Id = 1, CompanyId = 2, Username = "empresa-b",
            ModuleName = "Inventario", ActionName = "Creación"
        });
        await db.SaveChangesAsync();

        var service = new HistoryLogService(new WorkFlow(db), db);
        Assert.Null(await service.GetByIdInCompanyAsync(1, 1));
        Assert.NotNull(await service.GetByIdInCompanyAsync(1, 2));

        var newLog = new HistoryLog
        {
            CompanyId = 999,
            Username = "spoofed",
            LogDate = DateTime.MinValue,
            ModuleName = "Ventas",
            ActionName = "Registro"
        };
        Assert.True(await service.CreateInCompanyAsync(newLog, 1, 10, isDeveloper: false));
        Assert.Equal(1, newLog.CompanyId);
        Assert.Equal("empresa-a", newLog.Username);
        Assert.NotEqual(DateTime.MinValue, newLog.LogDate);
    }
}