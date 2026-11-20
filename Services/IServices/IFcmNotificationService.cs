namespace InventoryAPI.Services.IServices;

public interface IFcmNotificationService
{
    Task<bool> SendPushNotificationAsync(string firebaseInstallationId, string title, string body, Dictionary<string, string>? dataPayload = null);
}