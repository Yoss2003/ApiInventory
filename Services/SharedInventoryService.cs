using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services;

public sealed class SharedInventoryService(IWorkFlow workFlow, AppDbContext context)
    : WorkContainer<SharedInventory>(workFlow), ISharedInventoryService
{
    public Task<List<SharedInventoryDTO>> GetSharedWithUsersAsync(int inventoryId, int companyId)
    {
        if (inventoryId <= 0 || companyId <= 0)
            return Task.FromResult(new List<SharedInventoryDTO>());

        return context.SharedInventories
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId &&
                        s.InventoryId == inventoryId &&
                        s.Inventory != null &&
                        s.Inventory.CompanyId == companyId)
            .Select(s => new SharedInventoryDTO
            {
                Id = s.Id,
                InventoryId = s.InventoryId,
                UserId = s.UserId,
                Username = s.User != null ? s.User.Username! : "Desconocido",
                AccessLevel = s.AccessLevel,
                SharedDate = s.SharedDate,
                GrantedBy = s.Inventory != null ? s.Inventory.Username : "Administrador"
            })
            .ToListAsync();
    }
}