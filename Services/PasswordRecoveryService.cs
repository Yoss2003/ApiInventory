using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Security;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace InventoryAPI.Services;

public sealed class PasswordRecoveryService(AppDbContext db, IConfiguration config) : IPasswordRecoveryService
{
    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task SendEmailAsync(string to, string subject, string body)
    {
        var host = config["PasswordRecovery:SmtpHost"];
        var from = config["PasswordRecovery:SenderEmail"];
        var smtpUser = config["PasswordRecovery:SmtpUser"];
        var smtpPass = config["PasswordRecovery:SmtpPassword"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) ||
            string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass))
            throw new InvalidOperationException("SMTP de recuperación no configurado.");

        using var smtp = new SmtpClient(host, int.TryParse(config["PasswordRecovery:SmtpPort"], out var port) ? port : 587)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(smtpUser, smtpPass)
        };
        using var message = new MailMessage(new MailAddress(from, "OmniStock"), new MailAddress(to))
        {
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        await smtp.SendMailAsync(message);
    }

    public async Task<object> RequestResetAsync(PasswordRecoveryRequestDto input)
    {
        var reply = new { message = "Si la cuenta existe, está activa y tiene correo, tu solicitud será revisada." };
        if (input.CompanyId <= 0 || string.IsNullOrWhiteSpace(input.UsernameOrEmail)) return reply;

        var name = input.UsernameOrEmail.Trim();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u =>
            u.CompanyId == input.CompanyId && u.IsActive && u.StatusId == 1 &&
            (u.Username == name || u.Email == name));
        if (user is null || string.IsNullOrWhiteSpace(user.Email)) return reply;

        var since = DateTime.UtcNow.AddMinutes(-15);
        if (await db.PasswordRecoveryRequests.AnyAsync(r => r.UserId == user.Id && r.RequestedAt >= since))
            return reply;

        var request = new PasswordRecoveryRequest { CompanyId = input.CompanyId, UserId = user.Id };
        db.PasswordRecoveryRequests.Add(request);
        await db.SaveChangesAsync();

        var developerMail = config["PasswordRecovery:DeveloperEmail"];
        if (!string.IsNullOrWhiteSpace(developerMail))
        {
            try
            {
                await SendEmailAsync(developerMail, "OmniStock: solicitud de recuperación",
                    $"Solicitud #{request.Id} pendiente de revisión. Ingresa a Gestión Dev para aprobarla. No responda con contraseñas.");
            }
            catch { }
        }
        return reply;
    }

    public async Task<object> GetPendingAsync()
    {
        return await db.PasswordRecoveryRequests.AsNoTracking()
            .Where(r => !r.Rejected && r.ApprovedAt == null && r.CompletedAt == null)
            .OrderByDescending(r => r.RequestedAt)
            .Select(r => new
            {
                r.Id,
                r.CompanyId,
                r.UserId,
                r.RequestedAt,
                username = db.Users.Where(u => u.Id == r.UserId).Select(u => u.Username).FirstOrDefault(),
                company = db.Companies.Where(c => c.Id == r.CompanyId).Select(c => c.BusinessName).FirstOrDefault()
            }).ToListAsync();
    }

    public async Task<ServiceResult> ApproveAsync(int id, int approverUserId)
    {
        var request = await db.PasswordRecoveryRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (request is null || request.Rejected || request.ApprovedAt != null || request.CompletedAt != null)
            return ServiceResult.Conflict("Solicitud inexistente o ya procesada.");

        var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId && u.CompanyId == request.CompanyId);
        if (target is null || !target.IsActive || string.IsNullOrWhiteSpace(target.Email))
            return ServiceResult.Conflict("El usuario no está activo o no tiene correo registrado.");

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var frontend = config["PasswordRecovery:FrontendBaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(frontend) || !Uri.TryCreate(frontend, UriKind.Absolute, out var url) ||
            (url.Scheme != Uri.UriSchemeHttps && url.Host != "localhost"))
            return ServiceResult.ServiceUnavailable("Configura FrontendBaseUrl con HTTPS.");

        var link = frontend + "/reset-password?token=" + Uri.EscapeDataString(token);
        request.ApprovedAt = DateTime.UtcNow;
        request.ApprovedByUserId = approverUserId;
        request.ExpiresAt = DateTime.UtcNow.AddMinutes(30);
        request.TokenHash = HashToken(token);
        await db.SaveChangesAsync();

        try
        {
            await SendEmailAsync(target.Email, "Recuperación aprobada - OmniStock",
                $"Se aprobó tu solicitud. En los próximos 30 minutos, establece tu propia contraseña mediante este enlace de un solo uso:\n{link}\nSi no lo solicitaste, comunícate con el administrador.");
        }
        catch
        {
            request.ApprovedAt = null;
            request.ApprovedByUserId = null;
            request.ExpiresAt = null;
            request.TokenHash = null;
            await db.SaveChangesAsync();
            return ServiceResult.ServiceUnavailable("No fue posible enviar el correo. Revisa SMTP y vuelve a aprobar.");
        }

        return ServiceResult.Ok(new { message = "Aprobado. Se envió un enlace temporal al correo registrado." });
    }

    public async Task<ServiceResult> RejectAsync(int id)
    {
        var r = await db.PasswordRecoveryRequests.FirstOrDefaultAsync(x => x.Id == id && x.ApprovedAt == null);
        if (r is null) return ServiceResult.NotFound();
        r.Rejected = true;
        await db.SaveChangesAsync();
        return ServiceResult.NoContent();
    }

    public async Task<ServiceResult> CompleteAsync(PasswordRecoveryCompleteDto input)
    {
        if (string.IsNullOrWhiteSpace(input.Token) || input.Token.Length != 64 ||
            string.IsNullOrWhiteSpace(input.NewPassword) || input.NewPassword.Length < 12 || input.NewPassword.Length > 128)
            return ServiceResult.BadRequest("Enlace inválido o contraseña fuera de 12–128 caracteres.");

        var hash = HashToken(input.Token);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var r = await db.PasswordRecoveryRequests.FromSqlInterpolated(
            $"SELECT * FROM \"PasswordRecoveryRequests\" WHERE \"TokenHash\" = {hash} FOR UPDATE")
            .SingleOrDefaultAsync();
        if (r is null || r.ApprovedAt is null || r.CompletedAt != null || r.ExpiresAt <= DateTime.UtcNow)
            return ServiceResult.BadRequest("Enlace inválido, utilizado o expirado.");

        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"Users\" WHERE \"Id\" = {r.UserId} FOR UPDATE").SingleOrDefaultAsync();
        if (user is null || !user.IsActive || user.CompanyId != r.CompanyId)
            return ServiceResult.BadRequest("Cuenta no disponible.");

        user.Password = PasswordHashService.HashPassword(input.NewPassword);
        user.MustChangePassword = false;
        r.CompletedAt = DateTime.UtcNow;
        r.TokenHash = null;

        var others = await db.PasswordRecoveryRequests
            .Where(x => x.UserId == r.UserId && x.Id != r.Id && x.CompletedAt == null)
            .ToListAsync();
        foreach (var other in others)
        {
            other.TokenHash = null;
            other.Rejected = true;
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ServiceResult.Ok(new { message = "Contraseña actualizada. Ya puedes iniciar sesión." });
    }
}