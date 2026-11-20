using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Services;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class ArticlesController(IArticleService articleService) : BaseApiController
{
    private static Article MapToEntity(ArticleWriteDto dto, int id = 0) => new()
    {
        Id = id,
        InventoryId = dto.InventoryId,
        CategoryId = dto.CategoryId,
        BrandId = dto.BrandId,
        Code = dto.Code,
        Barcode = dto.Barcode,
        Name = dto.Name,
        Model = dto.Model,
        SerialNumber = dto.SerialNumber,
        Tracking = dto.Tracking,
        Presentation = dto.Presentation,
        LocationId = dto.LocationId,
        StatusId = dto.StatusId,
        ConditionId = dto.ConditionId,
        Stock = dto.Stock,
        MeasurementUnit = dto.MeasurementUnit,
        AcquisitionUnit = dto.AcquisitionUnit,
        SaleUnit = dto.SaleUnit,
        ConversionFactor = dto.ConversionFactor,
        AcquisitionPrice = dto.AcquisitionPrice,
        AcquisitionCurrency = dto.AcquisitionCurrency,
        SalePrice = dto.SalePrice,
        SaleCurrency = dto.SaleCurrency,
        SupplierId = dto.SupplierId,
        CurrentEmployeeId = dto.CurrentEmployeeId,
        PreviousEmployeeId = dto.PreviousEmployeeId,
        FixedAsset = dto.FixedAsset,
        UsefulLifeMonths = dto.UsefulLifeMonths,
        WarrantyEndDate = dto.WarrantyEndDate,
        AcquisitionDate = dto.AcquisitionDate,
        DepartureDate = dto.DepartureDate,
        DecommissionDate = dto.DecommissionDate,
        ActionId = dto.ActionId,
        Characteristics = dto.Characteristics,
        Observation = dto.Observation,
        MainPhotoPath = dto.MainPhotoPath,
        MainVoucherPath = dto.MainVoucherPath
    };

    private static ArticleDetails MapDetailToEntity(ArticleDetailWriteDto dto, int id = 0) => new()
    {
        Id = id,
        ArticleId = dto.ArticleId,
        SerialNumber = dto.SerialNumber,
        Attr1 = dto.Attr1,
        Attr2 = dto.Attr2,
        Attr3 = dto.Attr3,
        Attr4 = dto.Attr4,
        Attr5 = dto.Attr5,
        Attr6 = dto.Attr6,
        StatusId = dto.StatusId
    };

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
    public async Task<IActionResult> PostArticle([FromBody] ArticleWriteDto dto)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var article = MapToEntity(dto);
        var result = await articleService.CreateArticleAsync(article, companyId, userId, EsDesarrollador);
        if (result.Status == ServiceStatus.Created && result.Payload is Article created)
            return CreatedAtAction(nameof(GetArticle), new { id = created.Id }, created);

        return FromServiceResult(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutArticle(int id, [FromBody] ArticleWriteDto dto)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var update = MapToEntity(dto, id);
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
    public async Task<IActionResult> PostArticleDetail([FromBody] ArticleDetailWriteDto dto)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var detail = MapDetailToEntity(dto);
        return FromServiceResult(await articleService.AddDetailAuthorizedAsync(detail, companyId, userId, EsDesarrollador));
    }

    [HttpPut("UpdateDetail/{id:int}")]
    public async Task<IActionResult> UpdateDetail(int id, [FromBody] ArticleDetailWriteDto dto)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var detail = MapDetailToEntity(dto, id);
        return FromServiceResult(await articleService.UpdateDetailAuthorizedAsync(id, detail, companyId, userId, EsDesarrollador));
    }

    [HttpDelete("DeleteDetail/{id:int}")]
    public async Task<IActionResult> DeleteDetail(int id)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await articleService.DeleteDetailAuthorizedAsync(id, companyId, userId, EsDesarrollador));
    }
}