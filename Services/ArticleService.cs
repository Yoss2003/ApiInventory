using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services
{
    public class ArticleService(
        IWorkFlow workFlow,
        AppDbContext db,
        IPessimisticLockRepository locks,
        IInventoryScopeService inventoryScope) : WorkContainer<Article>(workFlow), IArticleService
    {
        public async Task<List<Article>> GetVisibleArticlesAsync(int companyId, int userId, bool isDeveloper)
        {
            var permitted = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false);
            return await db.Articles.AsNoTracking()
                .Where(a => a.CompanyId == companyId && permitted.Contains(a.InventoryId))
                .Include(a => a.Details)
                .OrderBy(a => a.Name)
                .ToListAsync();
        }

        public async Task<Article?> GetVisibleArticleByIdAsync(int id, int companyId, int userId, bool isDeveloper)
        {
            var permitted = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false);
            return await db.Articles.AsNoTracking()
                .Include(a => a.Details)
                .FirstOrDefaultAsync(a => a.Id == id && a.CompanyId == companyId && permitted.Contains(a.InventoryId));
        }

        private async Task<string?> ValidateMasterDataAsync(Article article, int companyId, int userId, bool isDeveloper)
        {
            var writable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true);
            if (!await writable.ContainsAsync(article.InventoryId))
                return "No tienes permiso para modificar este almacén.";

            if (!await db.Categories.AnyAsync(c => c.Id == article.CategoryId && c.CompanyId == companyId && c.IsActive &&
                (c.InventoryId == article.InventoryId ||
                 db.Inventories.Any(i => i.Id == c.InventoryId && i.CompanyId == companyId && i.IsMaster))))
                return "La categoría no pertenece al catálogo de esta empresa o está inactiva.";

            if (article.BrandId.HasValue && !await db.Brands.AnyAsync(b =>
                b.Id == article.BrandId && b.CompanyId == companyId && b.CategoryId == article.CategoryId && b.IsActive))
                return "La marca no pertenece a esta categoría.";

            if (article.SupplierId.HasValue && !await db.Suppliers.AnyAsync(s =>
                s.Id == article.SupplierId && s.CompanyId == companyId && s.IsActive))
                return "El proveedor no pertenece a esta empresa o está inactivo.";

            async Task<bool> IsCompanyParameter(int? parameterId, string parameterType)
            {
                if (!parameterId.HasValue) return true;
                return await db.Parameters.AnyAsync(p =>
                    p.Id == parameterId && p.CompanyId == companyId &&
                    p.ParameterType.ToUpper() == parameterType.ToUpper() &&
                    (p.InventoryId == 0 || db.Inventories.Any(i =>
                        i.Id == p.InventoryId && i.CompanyId == companyId && i.IsMaster)));
            }

            if (!await IsCompanyParameter(article.StatusId, "Estado"))
                return "El estado seleccionado no pertenece a los parámetros de esta empresa.";
            if (!await IsCompanyParameter(article.ConditionId, "Condicion"))
                return "La condición seleccionada no pertenece a los parámetros de esta empresa.";
            if (!await IsCompanyParameter(article.LocationId, "Ubicacion"))
                return "La ubicación seleccionada no pertenece a los parámetros de esta empresa.";

            if (string.IsNullOrWhiteSpace(article.Code) || string.IsNullOrWhiteSpace(article.Name))
                return "Código y nombre del artículo son obligatorios.";
            if (article.Stock < 0)
                return "El stock no puede ser negativo.";

            return null;
        }

        public async Task<ServiceResult> CreateArticleAsync(Article article, int companyId, int userId, bool isDeveloper)
        {
            article.CompanyId = companyId;
            article.Id = 0;
            article.RegistrationDate = DateTime.UtcNow;

            var error = await ValidateMasterDataAsync(article, companyId, userId, isDeveloper);
            if (error is not null) return ServiceResult.BadRequest(error);

            if (await db.Articles.AnyAsync(a => a.CompanyId == companyId && a.InventoryId == article.InventoryId && a.Code == article.Code && a.IsActive))
                return ServiceResult.Conflict("El código ya existe en este inventario.");

            if (article.Tracking.ToString() == "Serialized")
            {
                var units = article.Details.ToList();
                if (units.Any(d => string.IsNullOrWhiteSpace(d.SerialNumber)) ||
                    units.Select(d => d.SerialNumber.Trim().ToUpperInvariant()).Distinct().Count() != units.Count ||
                    article.Stock != units.Count)
                    return ServiceResult.BadRequest("Un artículo serializado necesita una serie distinta por cada unidad de stock inicial.");
            }

            foreach (var d in article.Details)
            {
                d.Id = 0;
                d.IsActive = true;
                d.RegistrationDate = DateTime.UtcNow;
            }

            try
            {
                if (!await CreateAsync(article))
                    return ServiceResult.BadRequest("No se pudo crear.");
                return ServiceResult.Created(article);
            }
            catch (DbUpdateException)
            {
                return ServiceResult.Conflict("No se pudo guardar: verifica referencias y códigos duplicados.");
            }
        }

        public async Task<ServiceResult> UpdateArticleAsync(int id, Article update, int companyId, int userId, bool isDeveloper)
        {
            if (id != update.Id) return ServiceResult.BadRequest("El ID no coincide.");

            update.CompanyId = companyId;
            var error = await ValidateMasterDataAsync(update, companyId, userId, isDeveloper);
            if (error is not null) return ServiceResult.BadRequest(error);

            await using var tx = await db.Database.BeginTransactionAsync();
            var current = await locks.LockArticleAsync(id, companyId);
            if (current is null) return ServiceResult.NotFound();

            await db.Entry(current).Collection(a => a.Details).LoadAsync();
            if (!await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true).ContainsAsync(current.InventoryId))
                return ServiceResult.Forbid();
            if (!current.IsActive)
                return ServiceResult.Conflict("No se edita un artículo inactivo.");
            if (current.InventoryId != update.InventoryId || current.Tracking != update.Tracking)
                return ServiceResult.Conflict("No se puede cambiar el almacén ni el tipo de tracking de un artículo existente.");
            if (await db.Articles.AnyAsync(a => a.Id != id && a.CompanyId == companyId && a.InventoryId == current.InventoryId && a.Code == update.Code && a.IsActive))
                return ServiceResult.Conflict("El código ya está utilizado por otro artículo.");
            if (await db.ArticleAssignments.AnyAsync(a => a.ArticleId == id && a.CompanyId == companyId && a.Quantity > a.ReturnedQuantity) &&
                update.Stock != current.Stock)
                return ServiceResult.Conflict("Hay custodias vigentes: el stock se modifica con operaciones, no editando el artículo.");
            if (current.Tracking.ToString() == "Serialized" && update.Stock != current.Stock)
                return ServiceResult.Conflict("Para serializados, registra unidades en Gestión de series; no edites stock por cantidad.");

            var stockDelta = update.Stock - current.Stock;
            current.Name = update.Name.Trim();
            current.Code = update.Code.Trim();
            current.Barcode = update.Barcode;
            current.Model = update.Model;
            current.Presentation = update.Presentation;
            current.SerialNumber = update.SerialNumber;
            current.CategoryId = update.CategoryId;
            current.BrandId = update.BrandId;
            current.MeasurementUnit = update.MeasurementUnit;
            current.AcquisitionUnit = update.AcquisitionUnit;
            current.SaleUnit = update.SaleUnit;
            current.ConversionFactor = update.ConversionFactor;
            current.AcquisitionPrice = update.AcquisitionPrice;
            current.AcquisitionCurrency = update.AcquisitionCurrency;
            current.SalePrice = update.SalePrice;
            current.SaleCurrency = update.SaleCurrency;
            current.SupplierId = update.SupplierId;
            current.StatusId = update.StatusId;
            current.ConditionId = update.ConditionId;
            current.LocationId = update.LocationId;
            current.AcquisitionDate = update.AcquisitionDate;
            current.UsefulLifeMonths = update.UsefulLifeMonths;
            current.WarrantyEndDate = update.WarrantyEndDate;
            current.Observation = update.Observation;
            current.Characteristics = update.Characteristics;
            current.Stock = update.Stock;
            current.ModificationDate = DateTime.UtcNow;

            if (current.Tracking.ToString() == "Serialized" && update.Details.Count > 0 && current.Details.Count == 1)
            {
                var from = update.Details.First();
                var to = current.Details.First();
                to.SerialNumber = from.SerialNumber;
                to.Attr1 = from.Attr1;
                to.Attr2 = from.Attr2;
                to.Attr3 = from.Attr3;
                to.Attr4 = from.Attr4;
                to.Attr5 = from.Attr5;
                to.Attr6 = from.Attr6;
            }

            if (stockDelta != 0)
            {
                db.Movements.Add(new Movement
                {
                    ArticleId = id,
                    CompanyId = companyId,
                    MovementDate = DateTime.UtcNow,
                    ActionId = stockDelta > 0 ? 1 : 2,
                    Amount = Math.Abs(stockDelta),
                    Observation = "Ajuste de stock desde edición de artículo"
                });
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return ServiceResult.NoContent();
        }

        public async Task<ServiceResult> DeactivateArticleAsync(int id, int companyId, int userId, bool isDeveloper)
        {
            var article = await db.Articles.FirstOrDefaultAsync(a => a.Id == id && a.CompanyId == companyId);
            if (article is null) return ServiceResult.NotFound();
            if (!await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true).ContainsAsync(article.InventoryId))
                return ServiceResult.Forbid();
            if (!article.IsActive) return ServiceResult.NoContent();

            if (article.Stock != 0 ||
                await db.Movements.AnyAsync(m => m.ArticleId == id) ||
                await db.SaleDetails.AnyAsync(d => d.ArticleId == id) ||
                await db.ArticleAssignments.AnyAsync(a => a.ArticleId == id && a.CompanyId == companyId && a.Quantity > a.ReturnedQuantity))
                return ServiceResult.Conflict("Primero agota el stock y cierra las asignaciones pendientes.");

            article.IsActive = false;
            article.ModificationDate = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return ServiceResult.NoContent();
        }

        public async Task<ServiceResult> GetInventoryStockCountAsync(int inventoryId, int companyId, int userId, bool isDeveloper)
        {
            if (!await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false).ContainsAsync(inventoryId))
                return ServiceResult.Forbid();

            var total = await db.Articles
                .Where(a => a.InventoryId == inventoryId && a.CompanyId == companyId && a.IsActive)
                .SumAsync(a => a.Stock);
            return ServiceResult.Ok(total);
        }

        public async Task<Article?> GetVisibleArticleByBarcodeAsync(string barcode, int companyId, int userId, bool isDeveloper)
        {
            var permitted = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: false);
            return await db.Articles.AsNoTracking()
                .FirstOrDefaultAsync(a => a.CompanyId == companyId && permitted.Contains(a.InventoryId) && a.Barcode == barcode && a.IsActive);
        }

        public async Task<ServiceResult> AddDetailAuthorizedAsync(ArticleDetails detail, int companyId, int userId, bool isDeveloper)
        {
            var article = await db.Articles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == detail.ArticleId && a.CompanyId == companyId);
            if (article is null) return ServiceResult.NotFound();
            if (!await inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true).ContainsAsync(article.InventoryId))
                return ServiceResult.Forbid();
            if (article.Tracking.ToString() == "Serialized")
                return ServiceResult.Conflict("Registra unidades con POST /api/serial-units/{articleId}, que también crea el movimiento.");

            var result = await AddDetailAsync(detail);
            return result.Success
                ? ServiceResult.Ok(new { newId = detail.Id })
                : ServiceResult.BadRequest(result.ErrorMessage);
        }

        public async Task<ServiceResult> UpdateDetailAuthorizedAsync(int id, ArticleDetails detail, int companyId, int userId, bool isDeveloper)
        {
            var writable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true);
            if (id != detail.Id || !await db.Articles.AnyAsync(a => a.Id == detail.ArticleId && a.CompanyId == companyId && writable.Contains(a.InventoryId)))
                return ServiceResult.BadRequest("Detalle inválido o sin permiso.");

            return await UpdateDetailAsync(detail)
                ? ServiceResult.NoContent()
                : ServiceResult.BadRequest("No se pudo actualizar el detalle.");
        }

        public async Task<ServiceResult> DeleteDetailAuthorizedAsync(int id, int companyId, int userId, bool isDeveloper)
        {
            var detail = await db.Set<ArticleDetails>().FirstOrDefaultAsync(x => x.Id == id);
            var writable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true);
            if (detail is null || !await db.Articles.AnyAsync(a => a.Id == detail.ArticleId && a.CompanyId == companyId && writable.Contains(a.InventoryId)))
                return ServiceResult.NotFound();
            if (await db.ArticleUnitOperations.AnyAsync(o => o.ArticleDetailId == id))
                return ServiceResult.Conflict("La serie tiene historial de venta o asignación; no puede eliminarse.");

            return await DeleteDetailAsync(id)
                ? ServiceResult.NoContent()
                : ServiceResult.BadRequest("No se pudo eliminar el detalle.");
        }

        public override async Task<bool> CreateAsync(Article article)
        {
            await _workFlow.BeginTransactionAsync();

            try
            {
                await _workFlow.Repository<Article>().AddAsync(article);
                await _workFlow.CompleteAsync();

                var nuevoMovimiento = new Movement
                {
                    ArticleId = article.Id,
                    EmployeeId = article.LoggedUserId,
                    ActionId = 1,
                    MovementDate = DateTime.UtcNow,
                    Observation = "Registro inicial del producto en almacén",
                    Amount = article.Stock,
                    SalePrice = article.SalePrice ?? 0m,
                    CompanyId = article.CompanyId
                };
                await _workFlow.Repository<Movement>().AddAsync(nuevoMovimiento);

                string nombreUsuario = string.IsNullOrWhiteSpace(article.LoggedUserFullName)
                    ? "Usuario Desconocido"
                    : article.LoggedUserFullName;

                var nuevoLog = new HistoryLog
                {
                    LogDate = DateTime.UtcNow,
                    Username = nombreUsuario,
                    ModuleName = "Inventario",
                    ActionName = "Creación",
                    Detail = $"Producto \"{article.Name}\" agregado por \"{nombreUsuario}\" el \"{DateTime.Now:dd/MM/yyyy HH:mm}\"",
                    CompanyId = article.CompanyId
                };
                await _workFlow.Repository<HistoryLog>().AddAsync(nuevoLog);

                await _workFlow.CompleteAsync();
                await _workFlow.CommitTransactionAsync();

                return true;
            }
            catch (Exception)
            {
                await _workFlow.RollbackTransactionAsync();
                throw;
            }
        }

        public async Task<int> GetArticleCountByInventoryIdAsync(int inventoryId)
        {
            var articles = await _workFlow.Repository<Article>().FindAsync(a => a.InventoryId == inventoryId);
            return (int)articles.Sum(a => a.Stock);
        }

        public async Task<Article?> GetArticleByBarcodeAsync(string barcode)
        {
            var articles = await _workFlow.Repository<Article>().FindAsync(a => a.Barcode == barcode);
            return articles.FirstOrDefault();
        }

        public async Task<(bool Success, string ErrorMessage)> AddDetailAsync(ArticleDetails detail)
        {
            await _workFlow.BeginTransactionAsync();
            try
            {
                await _workFlow.Repository<ArticleDetails>().AddAsync(detail);

                var padre = await _workFlow.Repository<Article>().GetByIdAsync(detail.ArticleId);
                if (padre != null && padre.Tracking.ToString() == "Serialized")
                {
                    padre.Stock += 1;
                    _workFlow.Repository<Article>().Update(padre);
                }

                await _workFlow.CompleteAsync();
                await _workFlow.CommitTransactionAsync();

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                await _workFlow.RollbackTransactionAsync();
                string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return (false, msg);
            }
        }

        public async Task<bool> UpdateDetailAsync(ArticleDetails detail)
        {
            try
            {
                var existing = await _workFlow.Repository<ArticleDetails>().GetByIdAsync(detail.Id);
                if (existing == null) return false;
                existing.SerialNumber = detail.SerialNumber;
                _workFlow.Repository<ArticleDetails>().Update(existing);
                await _workFlow.CompleteAsync();
                return true;
            }
            catch { return false; }
        }

        public async Task<bool> DeleteDetailAsync(int id)
        {
            await _workFlow.BeginTransactionAsync();
            try
            {
                var existing = await _workFlow.Repository<ArticleDetails>().GetByIdAsync(id);
                if (existing == null) return false;

                existing.IsActive = false;
                _workFlow.Repository<ArticleDetails>().Update(existing);

                var padre = await _workFlow.Repository<Article>().GetByIdAsync(existing.ArticleId);
                if (padre != null && padre.Stock > 0)
                {
                    padre.Stock -= 1;
                    _workFlow.Repository<Article>().Update(padre);
                }

                await _workFlow.CompleteAsync();
                await _workFlow.CommitTransactionAsync();
                return true;
            }
            catch
            {
                await _workFlow.RollbackTransactionAsync();
                return false;
            }
        }

        public async Task<bool> UpdateArticleWithAuditAsync(int id, Article updatedArticle, int companyId)
        {
            await _workFlow.BeginTransactionAsync();
            try
            {
                var existingArticle = await _workFlow.Repository<Article>().GetByIdAsync(id);
                if (existingArticle == null || existingArticle.CompanyId != companyId)
                {
                    await _workFlow.RollbackTransactionAsync();
                    return false;
                }

                decimal stockOriginal = existingArticle.Stock;
                decimal diferenciaStock = updatedArticle.Stock - stockOriginal;

                foreach (var property in typeof(Article).GetProperties())
                {
                    if (property.Name != "Id" &&
                        property.Name != "CompanyId" &&
                        property.Name != "RegistrationDate" &&
                        property.Name != "IsActive" &&
                        property.CanWrite)
                    {
                        var newValue = property.GetValue(updatedArticle);
                        property.SetValue(existingArticle, newValue);
                    }
                }

                _workFlow.Repository<Article>().Update(existingArticle);
                await _workFlow.CompleteAsync();

                string nombreEmpleado = string.IsNullOrWhiteSpace(updatedArticle.LoggedUserFullName)
                    ? "Usuario Sistema" : updatedArticle.LoggedUserFullName;
                int empleadoId = updatedArticle.CurrentEmployeeId ?? 1;

                if (diferenciaStock != 0)
                {
                    var movimientoKardex = new Movement
                    {
                        ArticleId = existingArticle.Id,
                        EmployeeId = empleadoId,
                        ActionId = diferenciaStock > 0 ? 1 : 2,
                        MovementDate = DateTime.UtcNow,
                        Observation = $"Ajuste de stock automatizado ({(diferenciaStock > 0 ? "+" : "")}{diferenciaStock:0.##})",
                        Amount = Math.Abs(diferenciaStock),
                        SalePrice = 0,
                        PaymentMethod = "N/A",
                        Recipient = "Almacén Local",
                        CompanyId = companyId
                    };
                    await _workFlow.Repository<Movement>().AddAsync(movimientoKardex);
                }

                var nuevoLog = new HistoryLog
                {
                    LogDate = DateTime.Now,
                    Username = nombreEmpleado,
                    ModuleName = "Inventario",
                    ActionName = "Modificación",
                    Detail = $"Producto \"{existingArticle.Name}\" modificado. Variación de stock: {diferenciaStock:0.##}.",
                    CompanyId = companyId
                };
                await _workFlow.Repository<HistoryLog>().AddAsync(nuevoLog);

                await _workFlow.CompleteAsync();
                await _workFlow.CommitTransactionAsync();

                return true;
            }
            catch
            {
                await _workFlow.RollbackTransactionAsync();
                return false;
            }
        }
    }
}