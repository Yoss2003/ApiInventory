using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class NotificationsController(INotificationService notificationService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetNotifications()
    {
        var companyId = ObtenerEmpresaSegura();
        if (companyId <= 0) return Unauthorized();

        return Ok(await notificationService.GetAllByCompanyIdAsync(companyId));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetNotification(int id)
    {
        var companyId = ObtenerEmpresaSegura();
        if (companyId <= 0) return Unauthorized();

        var notification = await notificationService.GetByIdInCompanyAsync(id, companyId);
        return notification is null ? NotFound() : Ok(notification);
    }
}