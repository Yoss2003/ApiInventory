using InventoryAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Tests;

public sealed class MigrationSnapshotTests
{
    [Fact]
    public void Current_model_matches_the_latest_migration_snapshot()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=omni_stock_model_snapshot;Username=postgres;Password=postgres")
            .Options;

        using var context = new AppDbContext(options);

        Assert.False(context.Database.HasPendingModelChanges());
    }
}
