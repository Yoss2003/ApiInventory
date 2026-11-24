using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories;
using InventoryAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Tests;

public sealed class InventoryShareListTests
{
    [Fact]
    public async Task GetSharedInventoriesAuthorizedAsync_ReturnsNamesRolesAndOnlyCompanyRecords()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);

        db.Users.AddRange(
            new User { Id = 10, Username = "owner", CompanyId = 1, RoleId = 2, IsActive = true, StatusId = 1 },
            new User { Id = 20, Username = "warehouse.reader", CompanyId = 1, RoleId = 3, IsActive = true, StatusId = 1 },
            new User { Id = 30, Username = "external.user", CompanyId = 2, RoleId = 3, IsActive = true, StatusId = 1 });

        db.Employees.AddRange(
            new Employee { Id = 201, UserId = 20, CompanyId = 1, FirstName = "María", LastName = "López", IsActive = true },
            new Employee { Id = 301, UserId = 30, CompanyId = 2, FirstName = "Usuario", LastName = "Externo", IsActive = true });

        db.Inventories.Add(new Inventory
        {
            Id = 100,
            InventoryName = "Almacén principal",
            CompanyId = 1,
            UserId = 10,
            Username = "owner",
            IsActive = true,
            IsMaster = false
        });

        db.SharedInventories.AddRange(
            new SharedInventory
            {
                Id = 1,
                CompanyId = 1,
                InventoryId = 100,
                UserId = 20,
                AccessLevel = SharedInventory.AccessMode.Editor,
                SharedDate = new DateTime(2026, 11, 24)
            },
            new SharedInventory
            {
                Id = 2,
                CompanyId = 1,
                InventoryId = 100,
                UserId = 30,
                AccessLevel = SharedInventory.AccessMode.Lector
            });

        await db.SaveChangesAsync();

        var service = new InventoryService(new WorkFlow(db), db);
        var result = await service.GetSharedInventoriesAuthorizedAsync(100, 1, 10, false);

        Assert.Equal(ServiceStatus.Ok, result.Status);
        var shares = Assert.IsType<List<SharedInventoryAccessDto>>(result.Payload);
        var share = Assert.Single(shares);
        Assert.Equal(20, share.UserId);
        Assert.Equal("warehouse.reader", share.Username);
        Assert.Equal("María", share.FirstName);
        Assert.Equal("López", share.LastName);
        Assert.Equal(SharedInventory.AccessMode.Editor, share.AccessLevel);
    }
}
