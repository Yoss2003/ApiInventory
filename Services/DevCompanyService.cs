using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InventoryAPI.Services;

public sealed class DevCompanyService(
    AppDbContext db,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    IManagedUserRegistration registration) : IDevCompanyService
{
    private static bool TryResolvePlan(string? value, out string plan, out int months, out int maxUsers)
    {
        plan = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "basico" or "básico" or "basic" => "Básico",
            "pro" => "Pro",
            "plus" => "Plus",
            "max" => "Max",
            _ => string.Empty
        };
        months = plan == "Max" ? 3 : 1;
        maxUsers = plan switch
        {
            "Básico" => 5,
            "Pro" => 10,
            "Plus" => 15,
            "Max" => 0,
            _ => -1
        };
        return plan.Length > 0;
    }

    private static string? ValidateCompanyInput(CompanyInput input, bool requireNewFields)
    {
        if (string.IsNullOrWhiteSpace(input.BusinessName) || input.BusinessName.Trim().Length > 255)
            return "Ingresa una razón social válida (máximo 255 caracteres).";
        if (!Regex.IsMatch(input.Ruc ?? "", @"^\d{11}$"))
            return "El RUC debe tener 11 dígitos.";
        if (!string.IsNullOrWhiteSpace(input.PrimaryColorHex) &&
            !Regex.IsMatch(input.PrimaryColorHex, @"^#[0-9a-fA-F]{6}$"))
            return "El color debe tener el formato #RRGGBB.";

        if (requireNewFields || input.FiscalOwnerName is not null)
        {
            if (string.IsNullOrWhiteSpace(input.FiscalOwnerName) || input.FiscalOwnerName.Trim().Length > 255)
                return "Ingresa el nombre del dueño fiscal (máximo 255 caracteres).";
        }
        if (requireNewFields || input.ContactEmail is not null)
        {
            var email = input.ContactEmail?.Trim() ?? "";
            if (email.Length == 0 || email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
                return "Ingresa un correo de contacto válido.";
        }
        return null;
    }

    private Task<bool> HasDeveloperUserAsync(int companyId) =>
        db.Users.AnyAsync(u => u.CompanyId == companyId && u.RoleId == 1);

    public async Task<object> ListCompaniesAsync()
    {
        return await db.Companies.AsNoTracking().OrderBy(x => x.BusinessName)
            .Select(c => new
            {
                c.Id,
                c.BusinessName,
                c.Ruc,
                c.FiscalOwnerName,
                c.ContactEmail,
                c.IsActive,
                c.LogoUrl,
                c.PrimaryColorHex,
                userCount = db.Users.Count(u => u.CompanyId == c.Id && u.IsActive),
                totalUsers = db.Users.Count(u => u.CompanyId == c.Id),
                adminCount = db.Users.Count(u => u.CompanyId == c.Id && u.RoleId == 2),
                inventoryCount = db.Inventories.Count(i => i.CompanyId == c.Id && !i.IsMaster),
                masterInventoryId = db.Inventories.Where(i => i.CompanyId == c.Id && i.IsMaster)
                    .Select(i => (int?)i.Id).FirstOrDefault(),
                masterInventoryActive = db.Inventories.Any(i => i.CompanyId == c.Id &&
                    i.IsMaster && i.IsActive),
                license = db.CompanyLicenses.Where(l => l.CompanyId == c.Id).Select(l => new
                { l.Plan, l.MaxUsers, l.StartsAt, l.ExpiresAt, l.IsEnabled }).FirstOrDefault()
            }).ToListAsync();
    }

    public async Task<ServiceResult> CreateCompanyAsync(CompanyInput input)
    {
        var validation = ValidateCompanyInput(input, requireNewFields: true);
        if (validation is not null) return ServiceResult.BadRequest(validation);
        if (!TryResolvePlan(input.Plan, out var plan, out var months, out var maxUsers))
            return ServiceResult.BadRequest("Selecciona un plan Básico, Pro, Plus o Max.");
        if (await db.Companies.AnyAsync(c => c.Ruc == input.Ruc))
            return ServiceResult.Conflict("Ya hay una empresa con este RUC.");

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync();
        var company = new Company
        {
            BusinessName = input.BusinessName.Trim(),
            Ruc = input.Ruc,
            FiscalOwnerName = input.FiscalOwnerName!.Trim(),
            ContactEmail = input.ContactEmail!.Trim(),
            IsActive = true,
            RegistrationDate = now,
            PrimaryColorHex = input.PrimaryColorHex?.Trim(),
            LogoUrl = input.LogoUrl
        };
        db.Companies.Add(company);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation && pg.ConstraintName == "PK_Companies")
        {
            return ServiceResult.Conflict("El generador de IDs de empresas parece desfasado. Un administrador debe revisar la secuencia de PostgreSQL antes de reintentar.");
        }

        db.CompanyLicenses.Add(new CompanyLicense
        {
            CompanyId = company.Id,
            Plan = plan,
            MaxUsers = maxUsers,
            StartsAt = now,
            ExpiresAt = now.AddMonths(months),
            IsEnabled = input.LicenseEnabled,
            UpdatedAt = now
        });

        var masterInventory = InventoryProvisioner.NewMaster(company.Id);
        db.Inventories.Add(masterInventory);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ServiceResult.Ok(new { company.Id, masterInventoryId = masterInventory.Id });
    }

    public async Task<ServiceResult> UpdateCompanyAsync(int id, CompanyInput input)
    {
        var validation = ValidateCompanyInput(input, requireNewFields: false);
        if (validation is not null) return ServiceResult.BadRequest(validation);

        var company = await db.Companies.FindAsync(id);
        if (company is null) return ServiceResult.NotFound();
        if (await db.Companies.AnyAsync(c => c.Ruc == input.Ruc && c.Id != id))
            return ServiceResult.Conflict("RUC utilizado por otra empresa.");

        var central = await HasDeveloperUserAsync(id);
        if (central && company.Ruc != input.Ruc)
            return ServiceResult.Conflict("No se puede cambiar el RUC de la empresa que administra el sistema.");

        var license = await db.CompanyLicenses.SingleOrDefaultAsync(l => l.CompanyId == id);
        if (!central)
        {
            if (license is null)
            {
                if (!TryResolvePlan(input.Plan, out var newPlan, out var months, out var maxUsers))
                    return ServiceResult.BadRequest("Selecciona un plan Básico, Pro, Plus o Max.");
                var now = DateTime.UtcNow;
                license = new CompanyLicense
                {
                    CompanyId = id,
                    Plan = newPlan,
                    MaxUsers = maxUsers,
                    StartsAt = now,
                    ExpiresAt = now.AddMonths(months),
                    IsEnabled = input.LicenseEnabled,
                    UpdatedAt = now
                };
                db.CompanyLicenses.Add(license);
            }
            else
            {
                if (!string.Equals(input.Plan?.Trim(), license.Plan, StringComparison.OrdinalIgnoreCase))
                    return ServiceResult.Conflict("Para cambiar de plan, utiliza la renovación de licencia.");
                license.IsEnabled = input.LicenseEnabled;
                license.UpdatedAt = DateTime.UtcNow;
            }
            if (license is not null && license.MaxUsers > 0)
            {
                var activeUsers = await db.Users.CountAsync(u => u.CompanyId == id && u.IsActive);
                if (license.MaxUsers < activeUsers)
                    return ServiceResult.Conflict("El plan no admite los usuarios activos de esta empresa.");
            }
        }

        company.BusinessName = input.BusinessName.Trim();
        company.Ruc = input.Ruc;
        if (input.FiscalOwnerName is not null) company.FiscalOwnerName = input.FiscalOwnerName.Trim();
        if (input.ContactEmail is not null) company.ContactEmail = input.ContactEmail.Trim();
        company.PrimaryColorHex = input.PrimaryColorHex?.Trim();
        company.LogoUrl = input.LogoUrl;
        await db.SaveChangesAsync();
        return ServiceResult.NoContent();
    }

    public async Task<ServiceResult> RenewLicenseAsync(int id, RenewLicenseInput input)
    {
        if (!TryResolvePlan(input.Plan, out var plan, out var months, out var maxUsers))
            return ServiceResult.BadRequest("Selecciona un plan Básico, Pro, Plus o Max.");

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (company is null) return ServiceResult.NotFound();
        if (!company.IsActive)
            return ServiceResult.Conflict("No se puede renovar una empresa inactiva.");
        if (await HasDeveloperUserAsync(id))
            return ServiceResult.Conflict("La licencia de Administración Central no se modifica desde esta operación.");

        var now = DateTime.UtcNow;
        var current = await db.CompanyLicenses.SingleOrDefaultAsync(l => l.CompanyId == id);
        var activeUsers = await db.Users.CountAsync(u => u.CompanyId == id && u.IsActive);
        if (maxUsers > 0 && maxUsers < activeUsers)
            return ServiceResult.Conflict($"El plan {plan} admite hasta {maxUsers} usuarios; la empresa tiene {activeUsers} activos.");

        var baseDate = current is not null && current.ExpiresAt > now ? current.ExpiresAt : now;
        if (current is null)
        {
            current = new CompanyLicense { CompanyId = id, StartsAt = now };
            db.CompanyLicenses.Add(current);
        }
        current.StartsAt ??= now;
        current.Plan = plan;
        current.MaxUsers = maxUsers;
        current.ExpiresAt = baseDate.AddMonths(months);
        current.IsEnabled = true;
        current.UpdatedAt = now;
        await db.SaveChangesAsync();
        return ServiceResult.Ok(new { current.Plan, current.MaxUsers, current.StartsAt, current.ExpiresAt, current.IsEnabled });
    }

    public async Task<ServiceResult> ReactivateCompanyAsync(int id)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return ServiceResult.NotFound("La empresa no existe.");
        if (await HasDeveloperUserAsync(id))
            return ServiceResult.Conflict("La empresa administradora está protegida.");
        if (company.IsActive)
            return ServiceResult.Conflict("La empresa ya está activa.");

        company.IsActive = true;
        company.DeletionDate = null;
        company.DeletionUser = null;
        await db.SaveChangesAsync();
        return ServiceResult.NoContent();
    }

    public async Task<ServiceResult> DeactivateCompanyAsync(int id, string? actorUserId)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return ServiceResult.NotFound();
        if (await HasDeveloperUserAsync(id))
            return ServiceResult.Conflict("No se puede desactivar la empresa administradora.");

        if (await db.Users.AnyAsync(u => u.CompanyId == id) ||
            await db.Inventories.AnyAsync(i => i.CompanyId == id && !i.IsMaster) ||
            await db.Categories.AnyAsync(c => c.CompanyId == id) ||
            await db.Articles.AnyAsync(a => a.CompanyId == id) ||
            await db.Movements.AnyAsync(m => m.CompanyId == id))
            return ServiceResult.Conflict("La empresa tiene usuarios, inventarios u operaciones. Suspende su licencia si deseas bloquear el acceso.");

        company.IsActive = false;
        company.DeletionDate = DateTime.UtcNow;
        company.DeletionUser = actorUserId;
        await db.SaveChangesAsync();
        return ServiceResult.NoContent();
    }

    public async Task<ServiceResult> LookupRucAsync(string ruc, int adminCompanyId, CancellationToken ct)
    {
        if (!Regex.IsMatch(ruc ?? "", @"^\d{11}$"))
            return ServiceResult.BadRequest("El RUC debe tener exactamente 11 dígitos.");

        var token = config["Decolecta:Token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            if (adminCompanyId <= 0) return ServiceResult.Forbid();
            token = await db.Parameters.AsNoTracking()
                .Where(p => p.CompanyId == adminCompanyId &&
                    p.ParameterType == "API_KEY" && p.Name == "Decolecta_Token")
                .Select(p => p.Description).FirstOrDefaultAsync(ct);
        }
        if (string.IsNullOrWhiteSpace(token))
            return ServiceResult.ServiceUnavailable("Configura el token Decolecta en el servidor para consultar RUC.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.decolecta.com/v1/sunat/ruc?numero={ruc}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var client = httpFactory.CreateClient();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return ServiceResult.BadGateway("El servicio de consulta de RUC no respondió correctamente.");

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ServiceResult.BadGateway("La respuesta del servicio de RUC no es válida.");

            static string Get(JsonElement obj, string key) =>
                obj.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String
                    ? p.GetString()?.Trim() ?? "" : "";

            var name = Get(root, "razon_social");
            if (string.IsNullOrWhiteSpace(name))
                return ServiceResult.NotFound("No se encontró una razón social válida para este RUC.");

            return ServiceResult.Ok(new
            {
                ruc,
                businessName = name,
                address = Get(root, "direccion"),
                estado = Get(root, "estado"),
                condicion = Get(root, "condicion"),
                source = "Decolecta / SUNAT"
            });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ServiceResult.GatewayTimeout("La consulta de RUC superó el tiempo de espera.");
        }
        catch (HttpRequestException)
        {
            return ServiceResult.BadGateway("No se pudo conectar al servicio de consulta de RUC.");
        }
        catch (JsonException)
        {
            return ServiceResult.BadGateway("El servicio de RUC devolvió un formato no válido.");
        }
    }

    public async Task<ServiceResult> CreateFirstAdminAsync(int companyId, ManagedUserInput input)
    {
        if (input.RoleId != 2)
            return ServiceResult.BadRequest("Selecciona el rol Administrador (2).");
        if (!await db.Companies.AsNoTracking().AnyAsync(c => c.Id == companyId && c.IsActive))
            return ServiceResult.NotFound("Empresa no encontrada.");

        var result = await registration.RegisterAsync(companyId, input, firstAdministrator: true);
        return result.Success
            ? ServiceResult.Ok(result.Created)
            : ServiceResult.Conflict(result.Error ?? "No se pudo registrar el administrador.");
    }
}