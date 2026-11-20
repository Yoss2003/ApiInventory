using InventoryAPI.Models;

namespace InventoryAPI.Repositories.IRepositories;

public interface IPessimisticLockRepository
{
    Task<Article?> LockArticleAsync(int articleId, int companyId);
    Task<Sale?> LockSaleAsync(int saleId, int companyId);
    Task<SaleInstallment?> LockSaleInstallmentAsync(int installmentId, int saleId, int companyId);
    Task<AccountReceivable?> LockAccountReceivableAsync(int installmentId, int companyId);
    Task<Movement?> LockMovementAsync(int movementId, int companyId);
    Task<ArticleAssignment?> LockAssignmentAsync(int assignmentId, int companyId);
    Task<PasswordRecoveryRequest?> LockPasswordRecoveryByHashAsync(string tokenHash);
    Task<User?> LockUserAsync(int userId);
    Task AcquireCompanyAdvisoryLockAsync(int lockNamespace, int companyId);
}