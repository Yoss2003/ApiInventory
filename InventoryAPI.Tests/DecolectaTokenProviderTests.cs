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
    public async Task ResolvesDocumentAndExchangeTokens_IgnoringPlaceholders()
    {
        await using var db = CreateContext();
        db.Parameters.Add(new Parameters
        {
            Id = 1,
            CompanyId = 1,
            InventoryId = 0,
            ParameterType = "API_KEY",
            Name = "Decolecta_Token",
            Description = "db-fallback-token"
        });
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DecolectaApi:ReniecToken"] = "AQUI_VA_EL_TOKEN_DE_RENIEC",
                ["DecolectaApi:ExchangeToken"] = "AQUI_VA_EL_TOKEN_DE_EXCHANGE"
            })
            .Build();

        var provider = new DecolectaTokenProvider(config, db);

        Assert.Equal("db-fallback-token", await provider.GetDocumentTokenAsync());
        Assert.Equal("db-fallback-token", await provider.GetExchangeTokenAsync());
    }

    [Fact]
    public async Task PrefersSpecificTokens_OverDatabaseFallback()
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

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Decolecta:Token"] = "doc-token",
                ["DecolectaApi:ExchangeToken"] = "fx-token"
            })
            .Build();

        var provider = new DecolectaTokenProvider(config, db);

        Assert.Equal("doc-token", await provider.GetDocumentTokenAsync());
        Assert.Equal("fx-token", await provider.GetExchangeTokenAsync());
    }
}