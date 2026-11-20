using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Repositories;

public sealed class PessimisticLockRepository(AppDbContext db) : IPessimisticLockRepository
{
    public Task<Article?> LockArticleAsync(int articleId, int companyId) =>
        db.Articles.FromSqlInterpolated(
            $"SELECT * FROM \"Articles\" WHERE \"Id\" = {articleId} AND \"CompanyId\" = {companyId} FOR UPDATE")
            .SingleOrDefaultAsync();

    public Task<Sale?> LockSaleAsync(int saleId, int companyId) =>
        db.Sales.FromSqlInterpolated(
            $"SELECT * FROM \"Sales\" WHERE \"Id\" = {saleId} AND \"CompanyId\" = {companyId} FOR UPDATE")
            .SingleOrDefaultAsync();

    public Task<SaleInstallment?> LockSaleInstallmentAsync(int installmentId, int saleId, int companyId) =>
        db.SaleInstallments.FromSqlInterpolated(
            $"SELECT * FROM \"SaleInstallments\" WHERE \"Id\" = {installmentId} AND \"SaleId\" = {saleId} AND \"CompanyId\" = {companyId} FOR UPDATE")
            .SingleOrDefaultAsync();

    public Task<AccountReceivable?> LockAccountReceivableAsync(int installmentId, int companyId) =>
        db.AccountsReceivables.FromSqlInterpolated(
            $"SELECT * FROM \"AccountsReceivables\" WHERE \"Id\" = {installmentId} AND \"CompanyId\" = {companyId} FOR UPDATE")
            .SingleOrDefaultAsync();

    public Task<Movement?> LockMovementAsync(int movementId, int companyId) =>
        db.Movements.FromSqlInterpolated(
            $"SELECT * FROM \"Movements\" WHERE \"Id\" = {movementId} AND \"CompanyId\" = {companyId} FOR UPDATE")
            .SingleOrDefaultAsync();

    public Task<ArticleAssignment?> LockAssignmentAsync(int assignmentId, int companyId) =>
        db.ArticleAssignments.FromSqlInterpolated(
            $"SELECT * FROM \"ArticleAssignments\" WHERE \"Id\" = {assignmentId} AND \"CompanyId\" = {companyId} FOR UPDATE")
            .SingleOrDefaultAsync();

    public Task<PasswordRecoveryRequest?> LockPasswordRecoveryByHashAsync(string tokenHash) =>
        db.PasswordRecoveryRequests.FromSqlInterpolated(
            $"SELECT * FROM \"PasswordRecoveryRequests\" WHERE \"TokenHash\" = {tokenHash} FOR UPDATE")
            .SingleOrDefaultAsync();

    public Task<User?> LockUserAsync(int userId) =>
        db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync();

    public Task AcquireCompanyAdvisoryLockAsync(int lockNamespace, int companyId) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockNamespace}, {companyId})");
}