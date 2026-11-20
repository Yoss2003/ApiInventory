using FirebaseAdmin.Messaging;
using InventoryAPI.Services.IServices;

namespace InventoryAPI.Services;

public sealed class FcmNotificationService(ILogger<FcmNotificationService> logger) : IFcmNotificationService
{
    public async Task<bool> SendPushNotificationAsync(
        string firebaseInstallationId,
        string title,
        string body,
        Dictionary<string, string>? dataPayload = null)
    {
        if (string.IsNullOrWhiteSpace(firebaseInstallationId)) return false;

        try
        {
            var message = new Message
            {
                Fid = firebaseInstallationId.Trim(),
                Notification = new Notification
                {
                    Title = title,
                    Body = body
                },
                Data = dataPayload ?? []
            };

            await FirebaseMessaging.DefaultInstance.SendAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al enviar notificación push por Firebase FID.");
            return false;
        }
    }
}