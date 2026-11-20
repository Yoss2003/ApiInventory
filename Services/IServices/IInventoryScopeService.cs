namespace InventoryAPI.Services.IServices;

public interface IInventoryScopeService
{
    IQueryable<int> GetAccessibleInventoryIds(int companyId, int userId, bool isDeveloper, bool requireWrite = false, bool excludeMaster = true);
    Task<int?> GetActiveMasterInventoryIdAsync(int companyId, CancellationToken ct = default);
}