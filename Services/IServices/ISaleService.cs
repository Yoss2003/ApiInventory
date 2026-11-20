using InventoryAPI.Models;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices;

public sealed record QuickSaleRequest(
    int ArticleId,
    decimal Quantity,
    decimal UnitPrice,
    string? CustomerName,
    string PaymentMethod,
    string? Notes,
    Guid OperationKey,
    List<int>? DetailIds,
    string? DigitalWallet = null,
    string? InstallmentFrequency = null,
    int? InstallmentCount = null,
    decimal? DownPayment = null,
    DateOnly? FirstDueDate = null);

public sealed record CancelSaleRequest(string Reason);

public sealed record InstallmentSimulationRequest(decimal TotalAmount, SaleInstallmentPlanRequest InstallmentPlan);

public sealed record InstallmentReceiptRequest(
    decimal Amount,
    string PaymentMethod,
    string? DigitalWallet,
    string? Notes,
    Guid OperationKey);

public interface ISaleService : IWorkContainer<Sale>
{
    Task<List<Sale>> GetVisibleSalesAsync(int companyId, int userId, bool isDeveloper);
    Task<ServiceResult> CreateAuthorizedSaleAsync(Sale sale, int companyId, int userId, bool isDeveloper);
    Task<object> GetCancellationsAsync(int companyId);
    object[] GetPaymentMethods();
    ServiceResult SimulateInstallments(InstallmentSimulationRequest request);
    Task<ServiceResult> GetSaleInstallmentsAsync(int id, int companyId, int userId, bool isDeveloper);
    Task<ServiceResult> PaySaleInstallmentAsync(int id, int installmentId, InstallmentReceiptRequest request, int companyId, int userId, bool isDeveloper);
    Task<ServiceResult> QuickSaleAsync(QuickSaleRequest request, int companyId, int userId, bool isDeveloper);
    Task<ServiceResult> CancelSaleAsync(int id, CancelSaleRequest request, int companyId, int userId, bool isDeveloper);
    Task<(bool Success, string Message)> ProcessSaleAsync(Sale nuevaVenta);
}