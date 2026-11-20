namespace InventoryAPI.Services.IServices;

public interface IUserCredentialMailer
{
    Task<bool> IsConfiguredAsync(int companyId);
    Task<(bool Sent, string Message)> SendAsync(
        int companyId, string recipientEmail, string firstName, string username, string password);
}