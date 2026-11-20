using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories.IRepositories;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.Concurrent;

namespace InventoryAPI.Repositories
{
    public class WorkFlow(AppDbContext context) : IWorkFlow
    {
        private readonly AppDbContext _context = context;
        private readonly ConcurrentDictionary<Type, object> _repositories = new();
        private IDbContextTransaction? _transaction;

        public IGenericRepository<AccountReceivable> AccountReceivables => Repository<AccountReceivable>();
        public IGenericRepository<ActionItem> ActionItems => Repository<ActionItem>();
        public IGenericRepository<Article> Articles => Repository<Article>();
        public IGenericRepository<Brand> Brands => Repository<Brand>();
        public IGenericRepository<Category> Categories => Repository<Category>();
        public IGenericRepository<Currency> Currencies => Repository<Currency>();
        public IGenericRepository<Customer> Customers => Repository<Customer>();
        public IGenericRepository<Employee> Employees => Repository<Employee>();
        public IGenericRepository<ExportRoute> ExportRoutes => Repository<ExportRoute>();
        public IGenericRepository<ExchangeRate> ExchangeRates => Repository<ExchangeRate>();
        public IGenericRepository<HistoryLog> HistoryLogs => Repository<HistoryLog>();
        public IGenericRepository<InstallmentPayment> InstallmentPayments => Repository<InstallmentPayment>();
        public IGenericRepository<Inventory> Inventories => Repository<Inventory>();
        public IGenericRepository<Language> Languages => Repository<Language>();
        public IGenericRepository<MeasurementUnit> MeasurementUnits => Repository<MeasurementUnit>();
        public IGenericRepository<Movement> Movements => Repository<Movement>();
        public IGenericRepository<Notification> Notifications => Repository<Notification>();
        public IGenericRepository<Parameters> Parameters => Repository<Parameters>();
        public IGenericRepository<Permission> Permissions => Repository<Permission>();
        public IGenericRepository<Profile> Profiles => Repository<Profile>();
        public IGenericRepository<RecoveryAttempt> RecoveryAttempts => Repository<RecoveryAttempt>();
        public IGenericRepository<Role> Roles => Repository<Role>();
        public IGenericRepository<RolePermission> RolePermissions => Repository<RolePermission>();
        public IGenericRepository<Sale> Sales => Repository<Sale>();
        public IGenericRepository<SalesMode> SalesModes => Repository<SalesMode>();
        public IGenericRepository<SecurityQuestion> SecurityQuestions => Repository<SecurityQuestion>();
        public IGenericRepository<Supplier> Suppliers => Repository<Supplier>();
        public IGenericRepository<Theme> Themes => Repository<Theme>();
        public IGenericRepository<TimeZoneItem> TimeZones => Repository<TimeZoneItem>();
        public IGenericRepository<User> Users => Repository<User>();
        public IGenericRepository<CategoryMeasurementUnit> CategoryMeasurementUnits => Repository<CategoryMeasurementUnit>();

        public IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class
        {
            return (IGenericRepository<TEntity>)_repositories.GetOrAdd(
                typeof(TEntity),
                _ => new GenericRepository<TEntity>(_context));
        }

        public async Task<int> CompleteAsync()
        {
            return await _context.SaveChangesAsync();
        }

        public async Task BeginTransactionAsync()
        {
            if (_transaction is null)
            {
                _transaction = await _context.Database.BeginTransactionAsync();
            }
        }

        public async Task CommitTransactionAsync()
        {
            if (_transaction is null) return;
            try
            {
                await _transaction.CommitAsync();
            }
            finally
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public async Task RollbackTransactionAsync()
        {
            if (_transaction is null) return;
            try
            {
                await _transaction.RollbackAsync();
            }
            finally
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public void Dispose()
        {
            _transaction?.Dispose();
            _transaction = null;
            GC.SuppressFinalize(this);
        }
    }
}