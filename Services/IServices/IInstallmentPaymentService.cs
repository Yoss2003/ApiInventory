using InventoryAPI.Models;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices
{
    public sealed record RecordPaymentRequest(int InstallmentId, decimal PaymentAmount, string? Observation);

    public sealed record RecordPaymentResponse(
        int Id,
        int InstallmentId,
        decimal PaymentAmount,
        string PaymentDate,
        decimal InstallmentPaid,
        decimal InstallmentBalance,
        decimal SaleBalance);

    public interface IInstallmentPaymentService : IWorkContainer<InstallmentPayment>
    {
        Task<List<InstallmentPayment>> GetVisiblePaymentsAsync(int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> GetVisiblePaymentByIdAsync(int id, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> GetHistoricalSaleInstallmentsAsync(int saleId, int companyId, int userId, bool isDeveloper);
        Task<ServiceResult> RecordHistoricalPaymentAsync(RecordPaymentRequest request, int companyId, int userId, bool isDeveloper);
    }
}