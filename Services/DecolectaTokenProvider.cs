using InventoryAPI.Data;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services;

public sealed class DecolectaTokenProvider(IConfiguration configuration, AppDbContext db) : IDecolectaTokenProvider
{
    public async Task<string> GetTokenAsync(bool preferExchangeKey = false)
    {
        if (preferExchangeKey)
        {
            var exchangeToken = configuration["DecolectaApi:ExchangeToken"]?.Trim();
            if (!string.IsNullOrWhiteSpace(exchangeToken)) return exchangeToken;
        }

        var envToken = configuration["Decolecta:Token"]?.Trim();
        if (!string.IsNullOrWhiteSpace(envToken)) return envToken;

        var dbToken = await db.Parameters.AsNoTracking()
            .Where(p => p.CompanyId == 1 && p.ParameterType == "API_KEY" && p.Name == "Decolecta_Token")
            .OrderBy(p => p.InventoryId == 0 ? 0 : 1)
            .Select(p => p.Description)
            .FirstOrDefaultAsync();

        return dbToken?.Trim() ?? string.Empty;
    }
}