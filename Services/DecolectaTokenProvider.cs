using InventoryAPI.Data;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services;

public sealed class DecolectaTokenProvider(IConfiguration configuration, AppDbContext db) : IDecolectaTokenProvider
{
    private static string? CleanToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("AQUI_VA_", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("TU_", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return trimmed;
    }

    public async Task<string> GetDocumentTokenAsync()
    {
        var token = CleanToken(configuration["Decolecta:Token"])
                 ?? CleanToken(configuration["DecolectaApi:ReniecToken"]);

        if (!string.IsNullOrWhiteSpace(token))
            return token;

        return await ResolveDatabaseTokenAsync();
    }

    public async Task<string> GetExchangeTokenAsync()
    {
        var token = CleanToken(configuration["DecolectaApi:ExchangeToken"])
                 ?? CleanToken(configuration["Decolecta:Token"]);

        if (!string.IsNullOrWhiteSpace(token))
            return token;

        return await ResolveDatabaseTokenAsync();
    }

    private async Task<string> ResolveDatabaseTokenAsync()
    {
        var dbToken = await db.Parameters.AsNoTracking()
            .Where(p => p.CompanyId == 1 && p.ParameterType == "API_KEY" && p.Name == "Decolecta_Token")
            .OrderBy(p => p.InventoryId == 0 ? 0 : 1)
            .Select(p => p.Description)
            .FirstOrDefaultAsync();

        return CleanToken(dbToken) ?? string.Empty;
    }
}