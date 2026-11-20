using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Security;
using InventoryAPI.Services;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var jwtKey = builder.Configuration["Jwt:Key"];
var encryptionKey = builder.Configuration["Security:EncryptionKey"];
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection no está configurada.");
if (string.IsNullOrWhiteSpace(jwtKey) || (!builder.Environment.IsDevelopment() && jwtKey.Length < 32))
    throw new InvalidOperationException("Jwt:Key no está configurada o no cumple la longitud mínima de 32 caracteres.");
if (string.IsNullOrWhiteSpace(encryptionKey))
{
    if (builder.Environment.IsDevelopment())
        encryptionKey = jwtKey;
    else
        throw new InvalidOperationException("Security:EncryptionKey no está configurada en producción.");
}
else if (!builder.Environment.IsDevelopment() && encryptionKey.Length < 32)
{
    throw new InvalidOperationException("Security:EncryptionKey debe tener al menos 32 caracteres en producción.");
}

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddHostedService<ApprovalCleanupService>();
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("external", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.Services.AddSingleton<ISecretProtector>(new AesGcmSecretProtector(encryptionKey));
builder.Services.AddScoped<IDecolectaTokenProvider, DecolectaTokenProvider>();
builder.Services.AddScoped<IWorkFlow, WorkFlow>();
builder.Services.AddScoped<IPessimisticLockRepository, PessimisticLockRepository>();
builder.Services.AddScoped<IAccountReceivableService, AccountReceivableService>();
builder.Services.AddScoped<IActionItemService, ActionItemService>();
builder.Services.AddScoped<IArticleService, ArticleService>();
builder.Services.AddScoped<IBrandService, BrandService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ICurrencyService, CurrencyService>();
builder.Services.AddHttpClient<ICustomerService, CustomerService>(client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddScoped<IDateFormatService, DateFormatService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IExportRouteService, ExportRouteService>();
builder.Services.AddHttpClient<IExchangeRateService, ExchangeRateService>();
builder.Services.AddHttpClient<ISupplierService, SupplierService>();
builder.Services.AddScoped<IHistoryLogService, HistoryLogService>();
builder.Services.AddScoped<IInstallmentPaymentService, InstallmentPaymentService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<ILanguageService, LanguageService>();
builder.Services.AddScoped<IMeasurementUnitService, MeasurementUnitService>();
builder.Services.AddScoped<IMovementService, MovementService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IParametersService, ParametersService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IRecoveryAttemptService, RecoveryAttemptService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<ISaleService, SaleService>();
builder.Services.AddScoped<ISalesModeService, SalesModeService>();
builder.Services.AddScoped<ISecurityQuestionService, SecurityQuestionService>();
builder.Services.AddScoped<IThemeService, ThemeService>();
builder.Services.AddScoped<ITimeZoneItemService, TimeZoneItemService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICompanyLicenseService, CompanyLicenseService>();
builder.Services.AddScoped<InventoryAccessPolicy>();
builder.Services.AddScoped<IRolePermissionService, RolePermissionService>();
builder.Services.AddScoped<ICategoryMeasurementUnitService, CategoryMeasurementUnitService>();
builder.Services.AddScoped<ISharedInventoryService, SharedInventoryService>();
builder.Services.AddScoped<ISaleDetailService, SaleDetailService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IEmployeePermissionService, EmployeePermissionService>();
builder.Services.AddScoped<IFcmNotificationService, FcmNotificationService>();
builder.Services.AddScoped<IInventoryScopeService, InventoryScopeService>();
builder.Services.AddScoped<IManagedUserRegistration, ManagedUserRegistration>();
builder.Services.AddScoped<IUserCredentialMailer, UserCredentialMailer>();
builder.Services.AddScoped<ILegacyMediaMigrator, LegacyMediaMigrator>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IDevCompanyService, DevCompanyService>();
builder.Services.AddScoped<IPasswordRecoveryService, PasswordRecoveryService>();
builder.Services.AddScoped<ICompanyLogoService, CompanyLogoService>();
builder.Services.AddScoped<IArticleMediaService, ArticleMediaService>();
builder.Services.AddScoped<IAssignmentService, AssignmentService>();
builder.Services.AddScoped<ISerialUnitService, SerialUnitService>();
builder.Services.AddHttpClient<IPrivateMediaStorage, PrivateMediaStorage>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = SessionSecurityValidator.ValidateAsync
        };
    });

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length == 0 && builder.Environment.IsDevelopment())
    allowedOrigins = ["http://localhost:4200"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

if (!builder.Environment.IsDevelopment() && allowedOrigins.Length == 0)
    throw new InvalidOperationException("Cors:AllowedOrigins debe tener al menos un origen configurado en producción.");

var app = builder.Build();

try
{
    var firebaseKeyPath = Path.Combine(app.Environment.ContentRootPath, "firebase-key.json");
    if (File.Exists(firebaseKeyPath))
    {
        string jsonCreds = File.ReadAllText(firebaseKeyPath);
#pragma warning disable CS0618
        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromJson(jsonCreds)
        });
#pragma warning restore CS0618
    }
    else
    {
        app.Logger.LogWarning("Archivo de credenciales Firebase no encontrado en {Path}.", firebaseKeyPath);
    }
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Error crítico al inicializar Firebase.");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowAngularApp");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        context.Database.Migrate();

        if (!context.Companies.Any())
        {
            var defaultCompany = new Company
            {
                BusinessName = "Administración Central",
                Ruc = "00000000000",
                LogoUrl = "dotnet_bot.png",
                PrimaryColorHex = "#8A2BE2",
                IsActive = true,
                RegistrationDate = DateTime.Now
            };

            context.Companies.Add(defaultCompany);
            context.SaveChanges();

            const int superAdminRoleId = 1;
            var superAdmins = context.Users
                .Where(u => u.CompanyId == null && u.RoleId == superAdminRoleId)
                .ToList();

            foreach (var admin in superAdmins)
            {
                admin.CompanyId = defaultCompany.Id;
            }
            context.SaveChanges();
        }

        InventoryProvisioner.EnsureExistingAsync(context).GetAwaiter().GetResult();

        using (var permissionsTx = context.Database.BeginTransaction())
        {
            context.Database.ExecuteSqlRaw(
                @"LOCK TABLE ""Permissions"" IN SHARE ROW EXCLUSIVE MODE;");
            context.Database.ExecuteSqlRaw("""
                DO $permissions_seed$
                DECLARE identity_sequence regclass;
                BEGIN
                    identity_sequence := pg_get_serial_sequence('"Permissions"', 'Id')::regclass;
                    IF identity_sequence IS NULL THEN
                        RAISE EXCEPTION 'No se encontró la secuencia de Permissions.Id';
                    END IF;
                    PERFORM setval(identity_sequence,
                        GREATEST(
                            COALESCE((SELECT MAX("Id") FROM "Permissions"), 0),
                            nextval(identity_sequence)
                        ), true);
                END;
                $permissions_seed$;
                """);

            var registeredCodes = context.Permissions
                .Select(x => x.SystemCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var initialViewUsersSeed = !registeredCodes.Contains(InventoryPermissionCodes.ViewUsers);
            foreach (var (code, label) in InventoryPermissionCodes.Definitions)
            {
                if (registeredCodes.Add(code))
                    context.Permissions.Add(new Permission { SystemCode = code, Name = label });
            }
            context.SaveChanges();

            if (initialViewUsersSeed && context.Roles.Any(r => r.Id == 2))
            {
                var codes = new[] { InventoryPermissionCodes.ViewUsers, InventoryPermissionCodes.ManageUsers };
                var ids = context.Permissions.Where(p => codes.Contains(p.SystemCode))
                    .Select(p => p.Id).ToList();
                var existingIds = context.RolePermissions.Where(p => p.RoleId == 2)
                    .Select(p => p.PermissionId).ToHashSet();
                foreach (var id in ids)
                    if (!existingIds.Contains(id))
                        context.RolePermissions.Add(new RolePermission { RoleId = 2, PermissionId = id });
                context.SaveChanges();
            }
            permissionsTx.Commit();
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Ocurrió un error al aplicar las migraciones o preparar los inventarios.");
        throw;
    }
}

if (args.Contains("--migrate-private-media", StringComparer.OrdinalIgnoreCase))
{
    using var migrationScope = app.Services.CreateScope();
    var migrator = migrationScope.ServiceProvider.GetRequiredService<ILegacyMediaMigrator>();
    await migrator.RunAsync();
    return;
}

app.Run();