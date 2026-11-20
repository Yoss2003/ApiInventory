using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class CurrenciesController(ICurrencyService currencyService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetCurrencies() =>
        Ok(await currencyService.GetAllAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCurrency(int id)
    {
        var currency = await currencyService.GetByIdAsync(id);
        return currency is null ? NotFound() : Ok(currency);
    }
}