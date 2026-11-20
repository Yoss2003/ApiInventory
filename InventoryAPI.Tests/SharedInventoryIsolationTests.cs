using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Tests;

public sealed class SharedInventoryIsolationTests
{
    [Fact]
    public async Task GetSharedWithUsersAsync_OnlyReturnsSharesForRequestedCompanyAndInventory()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        db.Users.AddRange(
            new User { Id = 10, Username = "user-a", CompanyId = 1, RoleId = 3, IsActive = true, StatusId = 1 },
            new User { Id = 20, Username = "user-b", CompanyId = 2, RoleId = 3, IsActive = true, StatusId = 1 });
        db.Inventories.AddRange(
            new Inventory { Id = 100, InventoryName = "A", CompanyId = 1, UserId = 10, Username = "user-a" },
            new Inventory { Id = 200, InventoryName = "B", CompanyId = 2, UserId = 20, Username = "user-b" });
        db.SharedInventories.AddRange(
            new SharedInventory { Id = 1, CompanyId = 1, InventoryId = 100, UserId = 10 },
            new SharedInventory { Id = 2, CompanyId = 2, InventoryId = 200, UserId = 20 },
            new SharedInventory { Id = 3, CompanyId = 2, InventoryId = 100, UserId = 20 });
        await db.SaveChangesAsync();

        var service = new SharedInventoryService(new WorkFlow(db), db);

        var companyOne = await service.GetSharedWithUsersAsync(100, 1);
        var companyTwo = await service.GetSharedWithUsersAsync(100, 2);
        var invalidCompany = await service.GetSharedWithUsersAsync(100, 0);

        Assert.Single(companyOne);
        Assert.Equal(1, companyOne[0].Id);
        Assert.Empty(companyTwo);
        Assert.Empty(invalidCompany);
    }
}