namespace InventoryAPI.Services.IServices;

public interface IDecolectaTokenProvider
{
    Task<string> GetDocumentTokenAsync();
    Task<string> GetExchangeTokenAsync();
}