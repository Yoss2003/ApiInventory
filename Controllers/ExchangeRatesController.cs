using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class ExchangeRatesController(IExchangeRateService exchangeRateService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetExchangeRates() =>
        Ok(await exchangeRateService.GetAllAsync());

    [HttpGet("today/{currency?}")]
    public async Task<IActionResult> GetTodayExchangeRate(string? currency) =>
        Ok(await exchangeRateService.GetTodayExchangeRateAsync(currency));
}