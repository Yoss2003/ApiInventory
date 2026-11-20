namespace InventoryAPI.Services.IServices;

public interface ILegacyMediaMigrator
{
    Task RunAsync(CancellationToken ct = default);
}