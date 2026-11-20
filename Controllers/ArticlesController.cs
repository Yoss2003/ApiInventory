using InventoryAPI.Models;
using InventoryAPI.Services;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class ArticlesController(IArticleService articleService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetArticles()
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return Ok(await articleService.GetVisibleArticlesAsync(companyId, userId, EsDesarrollador));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetArticle(int id)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        var article = await articleService.GetVisibleArticleByIdAsync(id, companyId, userId, EsDesarrollador);
        return article is null ? NotFound() : Ok(article);
    }

    [HttpPost]
    public async Task<IActionResult> PostArticle([FromBody] Article article)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await articleService.CreateArticleAsync(article, companyId, userId, EsDesarrollador);
        if (result.Status == ServiceStatus.Created && result.Payload is Article created)
            return CreatedAtAction(nameof(GetArticle), new { id = created.Id }, created);

        return FromServiceResult(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutArticle(int id, [FromBody] Article update)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        return FromServiceResult(await articleService.UpdateArticleAsync(id, update, companyId, userId, EsDesarrollador));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteArticle(int id)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await articleService.DeactivateArticleAsync(id, companyId, userId, EsDesarrollador));
    }

    [HttpGet("count/inventory/{inventoryId:int}")]
    public async Task<IActionResult> GetArticleCount(int inventoryId)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await articleService.GetInventoryStockCountAsync(inventoryId, companyId, userId, EsDesarrollador));
    }

    [HttpGet("barcode/{barcode}")]
    public async Task<IActionResult> GetArticleByBarcode(string barcode)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        var article = await articleService.GetVisibleArticleByBarcodeAsync(barcode, companyId, userId, EsDesarrollador);
        return article is null ? NotFound() : Ok(article);
    }

    [HttpPost("AddDetail")]
    public async Task<IActionResult> PostArticleDetail([FromBody] ArticleDetails detail)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await articleService.AddDetailAuthorizedAsync(detail, companyId, userId, EsDesarrollador));
    }

    [HttpPut("UpdateDetail/{id:int}")]
    public async Task<IActionResult> UpdateDetail(int id, [FromBody] ArticleDetails detail)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await articleService.UpdateDetailAuthorizedAsync(id, detail, companyId, userId, EsDesarrollador));
    }

    [HttpDelete("DeleteDetail/{id:int}")]
    public async Task<IActionResult> DeleteDetail(int id)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await articleService.DeleteDetailAuthorizedAsync(id, companyId, userId, EsDesarrollador));
    }
}