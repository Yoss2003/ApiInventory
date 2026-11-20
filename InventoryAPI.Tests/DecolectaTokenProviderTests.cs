using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace InventoryAPI.Tests;

public sealed class DecolectaTokenProviderTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetTokenAsync_PrefersEnvironmentConfig_OverDatabaseFallback()
    {
        await using var db = CreateContext();
        db.Parameters.Add(new Parameters
        {
            Id = 1,
            CompanyId = 1,
            InventoryId = 0,
            ParameterType = "API_KEY",
            Name = "Decolecta_Token",
            Description = "db-token"
        });
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Decolecta:Token"] = "env-token",
                ["DecolectaApi:ExchangeToken"] = "exchange-token"
            })
            .Build();

        var provider = new DecolectaTokenProvider(config, db);

        Assert.Equal("env-token", await provider.GetTokenAsync());
        Assert.Equal("exchange-token", await provider.GetTokenAsync(preferExchangeKey: true));
    }

    [Fact]
    public async Task GetTokenAsync_FallsBackToDatabase_WhenConfigIsEmpty()
    {
        await using var db = CreateContext();
        db.Parameters.Add(new Parameters
        {
            Id = 2,
            CompanyId = 1,
            InventoryId = 0,
            ParameterType = "API_KEY",
            Name = "Decolecta_Token",
            Description = "db-fallback-token"
        });
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build();
        var provider = new DecolectaTokenProvider(config, db);

        Assert.Equal("db-fallback-token", await provider.GetTokenAsync());
    }
}