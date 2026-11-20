namespace InventoryAPI.Services.IServices;

public interface IDecolectaTokenProvider
{
    Task<string> GetTokenAsync(bool preferExchangeKey = false);
}