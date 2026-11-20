using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InventoryAPI.Services
{
    public class SupplierService(
        IWorkFlow workFlow,
        HttpClient httpClient,
        AppDbContext db,
        IDecolectaTokenProvider decolectaTokens,
        IInventoryScopeService inventoryScope) : WorkContainer<Supplier>(workFlow), ISupplierService
    {
        private static readonly HashSet<string> AllowedFiscalKeys = new(StringComparer.Ordinal)
        {
            "tipo", "actividad_economica", "ubigeo", "numero_trabajadores",
            "tipo_facturacion", "tipo_contabilidad", "comercio_exterior",
            "via_tipo", "via_nombre", "zona_codigo", "zona_tipo",
            "numero", "interior", "lote", "dpto", "manzana", "kilometro",
            "es_agente_retencion", "es_buen_contribuyente"
        };

        private static bool TryNormalizeFiscalData(string? source, out string? normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(source)) return true;
            if (source.Length > 10000) return false;
            try
            {
                using var document = JsonDocument.Parse(source);
                if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
                var data = new Dictionary<string, JsonElement>();
                foreach (var field in document.RootElement.EnumerateObject())
                {
                    if (!AllowedFiscalKeys.Contains(field.Name)) continue;
                    if (field.Value.ValueKind is JsonValueKind.String)
                    {
                        if ((field.Value.GetString()?.Length ?? 0) > 500) return false;
                    }
                    else if (field.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
                        return false;
                    data[field.Name] = field.Value.Clone();
                }
                normalized = JsonSerializer.Serialize(data);
                return true;
            }
            catch (JsonException) { return false; }
        }

        public async Task<ServiceResult> LookupRucWithCacheAsync(string ruc, int companyId)
        {
            if (!Regex.IsMatch(ruc ?? string.Empty, @"^\d{11}$"))
                return ServiceResult.BadRequest("El RUC debe tener 11 dígitos.");

            var result = await ConsultarRucAsync(ruc!);
            if (!result.Success)
            {
                var cached = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s =>
                    s.CompanyId == companyId && s.Ruc == ruc && s.IsActive);
                if (cached is not null)
                {
                    cached.LookupFromCache = true;
                    return ServiceResult.Ok(cached);
                }
                if (result.Message.Contains("crítica"))
                    return ServiceResult.BadGateway(result.Message);
                if (result.Message.Contains("no fue localizado"))
                    return ServiceResult.NotFound(result.Message);
                return ServiceResult.BadRequest(result.Message);
            }

            return ServiceResult.Ok(result.Data);
        }

        public async Task<ServiceResult> CreateSupplierAsync(Supplier supplier, int companyId, int userId, bool isDeveloper)
        {
            var ruc = supplier.Ruc?.Trim() ?? string.Empty;
            if (!Regex.IsMatch(ruc, @"^\d{11}$"))
                return ServiceResult.BadRequest("El proveedor necesita un RUC válido de 11 dígitos.");

            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (!masterId.HasValue)
                return ServiceResult.Conflict("Inventario maestro no encontrado.");

            var writable = inventoryScope.GetAccessibleInventoryIds(companyId, userId, isDeveloper, requireWrite: true);
            if (supplier.InventoryId != masterId.Value && !await writable.ContainsAsync(supplier.InventoryId))
                return ServiceResult.Forbid();
            if (supplier.InventoryId == masterId.Value && !await writable.AnyAsync())
                return ServiceResult.Forbid();

            var existing = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s =>
                s.CompanyId == companyId && s.Ruc == ruc && s.IsActive);
            if (existing is not null)
            {
                existing.Reused = true;
                return ServiceResult.Ok(existing);
            }

            if (!TryNormalizeFiscalData(supplier.FiscalDataJson, out var fiscalData))
                return ServiceResult.BadRequest("Los datos fiscales adicionales tienen un formato inválido.");

            supplier.Id = 0;
            supplier.Ruc = ruc;
            supplier.CompanyId = companyId;
            supplier.InventoryId = masterId.Value;
            supplier.IsActive = true;
            supplier.BusinessName = supplier.BusinessName.Trim();
            supplier.RelationshipDescription = supplier.RelationshipDescription?.Trim();
            supplier.FiscalDataJson = fiscalData;

            try
            {
                db.Suppliers.Add(supplier);
                await db.SaveChangesAsync();
                return ServiceResult.Created(supplier);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
                pg.SqlState == PostgresErrorCodes.UniqueViolation &&
                pg.ConstraintName == "IX_Suppliers_Company_Ruc")
            {
                db.Entry(supplier).State = EntityState.Detached;
                var conflict = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s =>
                    s.CompanyId == companyId && s.Ruc == ruc && s.IsActive);
                if (conflict is null)
                    return ServiceResult.Conflict("RUC duplicado. Actualiza el catálogo.");
                conflict.Reused = true;
                return ServiceResult.Ok(conflict);
            }
        }

        public async Task<ServiceResult> UpdateSupplierAsync(int id, Supplier supplier, int companyId)
        {
            if (id != supplier.Id) return ServiceResult.BadRequest("El ID no coincide.");

            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (!masterId.HasValue)
                return ServiceResult.Conflict("Inventario maestro no encontrado.");

            var existing = await db.Suppliers.FirstOrDefaultAsync(s =>
                s.Id == id && s.CompanyId == companyId && s.InventoryId == masterId.Value && s.IsActive);
            if (existing is null) return ServiceResult.NotFound();

            var ruc = supplier.Ruc?.Trim() ?? string.Empty;
            if (!Regex.IsMatch(ruc, @"^\d{11}$"))
                return ServiceResult.BadRequest("RUC inválido.");

            if (await db.Suppliers.AnyAsync(s => s.CompanyId == companyId && s.Ruc == ruc && s.Id != id && s.IsActive))
                return ServiceResult.Conflict("Ya existe un proveedor con ese RUC.");

            if (!TryNormalizeFiscalData(supplier.FiscalDataJson, out var fiscalData))
                return ServiceResult.BadRequest("Los datos fiscales adicionales tienen un formato inválido.");

            existing.Ruc = ruc;
            existing.BusinessName = supplier.BusinessName.Trim();
            existing.ContactName = supplier.ContactName;
            existing.Phone = supplier.Phone;
            existing.Email = supplier.Email;
            existing.Address = supplier.Address;
            existing.Estado = supplier.Estado;
            existing.Condicion = supplier.Condicion;
            existing.Distrito = supplier.Distrito;
            existing.Provincia = supplier.Provincia;
            existing.Departamento = supplier.Departamento;
            existing.StatusId = supplier.StatusId;
            existing.RelationshipDescription = supplier.RelationshipDescription?.Trim();
            existing.FiscalDataJson = fiscalData ?? existing.FiscalDataJson;

            await db.SaveChangesAsync();
            return ServiceResult.NoContent();
        }

        public async Task<ServiceResult> DeleteSupplierAsync(int id, int companyId)
        {
            var masterId = await inventoryScope.GetActiveMasterInventoryIdAsync(companyId);
            if (!masterId.HasValue)
                return ServiceResult.Conflict("Inventario maestro no encontrado.");

            var supplier = await db.Suppliers.FirstOrDefaultAsync(s =>
                s.Id == id && s.CompanyId == companyId && s.InventoryId == masterId.Value);
            if (supplier is null) return ServiceResult.NotFound();

            if (await db.Articles.AnyAsync(a => a.CompanyId == companyId && a.SupplierId == id))
                return ServiceResult.Conflict("El proveedor se utiliza en artículos y no se puede desactivar.");

            supplier.IsActive = false;
            await db.SaveChangesAsync();
            return ServiceResult.NoContent();
        }

        public async Task<(bool Success, Supplier? Data, string Message)> ConsultarRucAsync(string ruc)
        {
            if (string.IsNullOrWhiteSpace(ruc) || ruc.Length != 11)
            {
                return (false, null, "El RUC debe tener exactamente 11 dígitos numéricos.");
            }

            try
            {
                var apiToken = await decolectaTokens.GetTokenAsync();
                if (string.IsNullOrWhiteSpace(apiToken))
                {
                    apiToken = await db.Parameters.AsNoTracking()
                        .Where(p => p.CompanyId == 1 && p.ParameterType == "API_KEY" &&
                            p.Name == "Decolecta_Token")
                        .OrderBy(p => p.InventoryId == 0 ? 0 : 1)
                        .Select(p => p.Description)
                        .FirstOrDefaultAsync() ?? "";
                }

                if (string.IsNullOrWhiteSpace(apiToken))
                {
                    return (false, null, "El servicio de SUNAT está inactivo. El administrador debe configurar el Token desde la App.");
                }

                static string? Field(JsonElement obj, string key)
                {
                    if (!obj.TryGetProperty(key, out var val)) return null;
                    var value = val.ValueKind switch
                    {
                        JsonValueKind.String => val.GetString()?.Trim(),
                        JsonValueKind.Number => val.GetRawText(),
                        _ => null
                    };
                    return string.IsNullOrWhiteSpace(value) || value == "-" ? null : value;
                }

                async Task<HttpResponseMessage> RequestAsync(string path)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get,
                        $"https://api.decolecta.com/v1/sunat/{path}?numero={ruc}");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    return await httpClient.SendAsync(req);
                }

                using var advancedResponse = await RequestAsync("ruc/full");
                var advancedAvailable = advancedResponse.IsSuccessStatusCode;
                HttpResponseMessage? basicResponse = null;
                HttpResponseMessage finalResponse = advancedResponse;
                try
                {
                    if (!advancedAvailable &&
                        advancedResponse.StatusCode is HttpStatusCode.BadRequest
                            or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                            or HttpStatusCode.UnprocessableEntity)
                    {
                        basicResponse = await RequestAsync("ruc");
                        finalResponse = basicResponse;
                    }

                    var json = await finalResponse.Content.ReadAsStringAsync();
                    if (!finalResponse.IsSuccessStatusCode)
                    {
                        var detail = "Consulta no autorizada o servicio no disponible.";
                        try
                        {
                            using var errorJson = JsonDocument.Parse(json);
                            if (errorJson.RootElement.ValueKind == JsonValueKind.Object)
                                detail = Field(errorJson.RootElement, "message") ??
                                    Field(errorJson.RootElement, "error") ?? detail;
                        }
                        catch (JsonException) { }
                        return (false, null,
                            $"Decolecta rechazó la consulta ({(int)finalResponse.StatusCode}): {detail}");
                    }

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                        return (false, null, "La consulta SUNAT devolvió una respuesta inválida.");

                    var businessName = Field(root, "razon_social");
                    if (string.IsNullOrWhiteSpace(businessName))
                        return (false, null, "SUNAT no devolvió una razón social válida para ese RUC.");

                    var supplier = new Supplier
                    {
                        InventoryId = 0,
                        Ruc = ruc,
                        BusinessName = businessName,
                        Address = Field(root, "direccion"),
                        StatusId = 1,
                        Estado = Field(root, "estado"),
                        Condicion = Field(root, "condicion"),
                        Distrito = Field(root, "distrito"),
                        Provincia = Field(root, "provincia"),
                        Departamento = Field(root, "departamento"),
                        AdvancedDataAvailable = advancedAvailable,
                        Ubigeo = Field(root, "ubigeo"),
                        TipoContribuyente = Field(root, "tipo"),
                        ActividadEconomica = Field(root, "actividad_economica"),
                        NumeroTrabajadores = Field(root, "numero_trabajadores"),
                        TipoFacturacion = Field(root, "tipo_facturacion"),
                        TipoContabilidad = Field(root, "tipo_contabilidad"),
                        ComercioExterior = Field(root, "comercio_exterior"),
                        FiscalDataJson = advancedAvailable
                            ? JsonSerializer.Serialize(new Dictionary<string, object?>
                            {
                                ["tipo"] = Field(root, "tipo"),
                                ["actividad_economica"] = Field(root, "actividad_economica"),
                                ["ubigeo"] = Field(root, "ubigeo"),
                                ["numero_trabajadores"] = Field(root, "numero_trabajadores"),
                                ["tipo_facturacion"] = Field(root, "tipo_facturacion"),
                                ["tipo_contabilidad"] = Field(root, "tipo_contabilidad"),
                                ["comercio_exterior"] = Field(root, "comercio_exterior"),
                                ["via_tipo"] = Field(root, "via_tipo"),
                                ["via_nombre"] = Field(root, "via_nombre"),
                                ["zona_codigo"] = Field(root, "zona_codigo"),
                                ["zona_tipo"] = Field(root, "zona_tipo"),
                                ["numero"] = Field(root, "numero"),
                                ["interior"] = Field(root, "interior"),
                                ["lote"] = Field(root, "lote"),
                                ["dpto"] = Field(root, "dpto"),
                                ["manzana"] = Field(root, "manzana"),
                                ["kilometro"] = Field(root, "kilometro"),
                                ["es_agente_retencion"] = root.TryGetProperty("es_agente_retencion", out var agente) &&
                                    agente.ValueKind == JsonValueKind.True,
                                ["es_buen_contribuyente"] = root.TryGetProperty("es_buen_contribuyente", out var buen) &&
                                    buen.ValueKind == JsonValueKind.True
                            })
                            : null
                    };
                    return (true, supplier,
                        advancedAvailable ? "Consulta RUC avanzada completada." :
                            "Consulta RUC básica completada; los datos avanzados no están disponibles con esta credencial.");
                }
                finally
                {
                    basicResponse?.Dispose();
                }
            }
            catch (Exception ex)
            {
                return (false, null, $"Falla en el servidor interno: {ex.Message}");
            }
        }
    }
}