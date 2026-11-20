using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class DateFormatsController(IDateFormatService dateFormatService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetDateFormats() =>
        Ok(await dateFormatService.GetAllAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDateFormat(int id)
    {
        var format = await dateFormatService.GetByIdAsync(id);
        return format is null ? NotFound() : Ok(format);
    }
}