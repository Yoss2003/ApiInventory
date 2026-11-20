using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Tests;

public sealed class ExportRouteIsolationTests
{
    [Fact]
    public async Task ExportRoutes_ArePrivateToTheirOwningUser()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        var service = new ExportRouteService(new WorkFlow(db), db);

        var route = new ExportRoute
        {
            UserId = 999,
            DefaultRoute1 = "default.csv",
            CustomRoute1 = "private-company-route.csv",
            FileType1 = "csv",
            IsDefault1 = false
        };

        Assert.True(await service.CreateForUserAsync(route, userId: 10));
        Assert.Equal(10, route.UserId);

        Assert.Single(await service.GetAllForUserAsync(10));
        Assert.Empty(await service.GetAllForUserAsync(20));
        Assert.NotNull(await service.GetByIdForUserAsync(route.Id, 10));
        Assert.Null(await service.GetByIdForUserAsync(route.Id, 20));

        var foreignUpdate = await service.UpdateForUserAsync(route.Id, new ExportRoute
        {
            Id = route.Id,
            UserId = 20,
            CustomRoute1 = "overwritten.csv"
        }, userId: 20);
        Assert.False(foreignUpdate);

        var foreignDelete = await service.DeleteForUserAsync(route.Id, userId: 20, deletedBy: "20");
        Assert.False(foreignDelete);

        var stored = await db.ExportRoutes.AsNoTracking().SingleAsync();
        Assert.Equal(10, stored.UserId);
        Assert.Equal("private-company-route.csv", stored.CustomRoute1);

        var ownUpdate = await service.UpdateForUserAsync(route.Id, new ExportRoute
        {
            Id = route.Id,
            UserId = 20,
            CustomRoute1 = "updated-own-route.csv",
            FileType1 = "csv"
        }, userId: 10);
        Assert.True(ownUpdate);

        Assert.True(await service.DeleteForUserAsync(route.Id, userId: 10, deletedBy: "10"));
        Assert.Empty(await db.ExportRoutes.ToListAsync());
    }
}