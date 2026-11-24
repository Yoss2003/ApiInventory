using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories;
using InventoryAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Tests;

public sealed class InventoryShareRecipientTests
{
    [Fact]
    public async Task ShareInventoryAsync_ResolvesRecipientByUserIdAndKeepsCompanyBoundary()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        db.Users.AddRange(
            new User { Id = 10, Username = "owner", CompanyId = 1, RoleId = 2, IsActive = true, StatusId = 1 },
            new User { Id = 20, Username = "recipient", CompanyId = 1, RoleId = 3, IsActive = true, StatusId = 1 },
            new User { Id = 30, Username = "other-company", CompanyId = 2, RoleId = 3, IsActive = true, StatusId = 1 });
        db.Inventories.Add(new Inventory
        {
            Id = 100, InventoryName = "Almacén propietario", CompanyId = 1,
            UserId = 10, Username = "owner", IsActive = true, IsMaster = false
        });
        await db.SaveChangesAsync();

        var service = new InventoryService(new WorkFlow(db), db);
        var success = await service.ShareInventoryAsync(new ShareRequestDTO
        {
            InventoryId = 100,
            GuestUserId = 20,
            AccessLevel = SharedInventory.AccessMode.Editor
        });
        var crossCompany = await service.ShareInventoryAsync(new ShareRequestDTO
        {
            InventoryId = 100,
            GuestUserId = 30,
            AccessLevel = SharedInventory.AccessMode.Lector
        });

        Assert.True(success.Success);
        Assert.False(crossCompany.Success);
        Assert.Contains("misma empresa", crossCompany.Message, StringComparison.OrdinalIgnoreCase);
        var saved = await db.SharedInventories.SingleAsync();
        Assert.Equal(20, saved.UserId);
        Assert.Equal(1, saved.CompanyId);
        Assert.Equal(SharedInventory.AccessMode.Editor, saved.AccessLevel);
    }
}
