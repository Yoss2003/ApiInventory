using InventoryAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services
{
    public class ApprovalCleanupService(
        IServiceProvider serviceProvider,
        ILogger<ApprovalCleanupService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = serviceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var limiteTiempo = DateTime.UtcNow.AddHours(-48);

                    var usuariosCaducados = await context.Users
                        .Include(u => u.Employee)
                        .Where(u => u.StatusId == 2 && u.CreatedAt != null && u.CreatedAt < limiteTiempo)
                        .ToListAsync(stoppingToken);

                    if (usuariosCaducados.Count != 0)
                    {
                        foreach (var user in usuariosCaducados)
                        {
                            user.IsActive = false;
                            user.StatusId = 3;
                            if (user.Employee is not null)
                                user.Employee.StatusId = 3;
                        }

                        await context.SaveChangesAsync(stoppingToken);
                        logger.LogInformation("Se suspendieron {Count} empleados por exceder 48 horas sin aprobación.", usuariosCaducados.Count);
                    }

                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error durante la limpieza automática de aprobaciones pendientes.");
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }
    }
}