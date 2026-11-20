using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Security;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace InventoryAPI.Services;

public sealed class UserManagementService(
    AppDbContext db,
    IUserCredentialMailer mailer) : IUserManagementService
{
    public async Task<object> ListUsersAsync(int companyId)
    {
        return await db.Users.AsNoTracking()
            .Where(u => u.CompanyId == companyId)
            .OrderBy(u => u.Username)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.Email,
                u.RoleId,
                u.CompanyId,
                u.IsActive,
                u.StatusId,
                firstName = u.Employee != null ? u.Employee.FirstName : null,
                lastName = u.Employee != null ? u.Employee.LastName : null,
                dni = u.Employee != null ? u.Employee.DNI : null,
                personalInventoryId = db.Inventories.Where(i => i.UserId == u.Id &&
                    i.CompanyId == companyId && !i.IsMaster && i.IsActive)
                    .Select(i => (int?)i.Id).FirstOrDefault()
            }).ToListAsync();
    }

    public async Task<object> GetAssignableRolesAsync()
    {
        return await db.Roles.AsNoTracking()
            .Where(r => r.Id > 2)
            .Select(r => new { r.Id, r.Name, r.Description })
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<ServiceResult> UpdateDetailsAsync(int id, UserDetailsUpdate input, int companyId, bool isDeveloper)
    {
        var user = await db.Users.Include(u => u.Employee)
            .FirstOrDefaultAsync(u => u.Id == id && u.CompanyId == companyId);
        if (user is null) return ServiceResult.NotFound();
        if (user.RoleId == 1 || (user.RoleId == 2 && !isDeveloper)) return ServiceResult.Forbid();

        var username = input.Username.Trim();
        var email = input.Email.Trim();
        if (await db.Users.AnyAsync(u => u.Id != id && u.Username == username))
            return ServiceResult.Conflict("Este usuario ya existe.");
        if (await db.Users.AnyAsync(u => u.Id != id && u.Email == email))
            return ServiceResult.Conflict("El correo ya existe.");
        if (!string.IsNullOrWhiteSpace(input.Dni) && await db.Employees.AnyAsync(e =>
            e.CompanyId == companyId && e.UserId != id && e.DNI == input.Dni))
            return ServiceResult.Conflict("El DNI ya está registrado.");

        user.Username = username;
        user.Email = email;
        user.Employee ??= new Employee
        {
            UserId = user.Id,
            CompanyId = companyId,
            IsActive = true,
            StatusId = user.StatusId
        };
        user.Employee.FirstName = input.FirstName.Trim();
        user.Employee.LastName = input.LastName.Trim();
        user.Employee.DNI = input.Dni?.Trim();
        await db.SaveChangesAsync();
        return ServiceResult.NoContent();
    }

    public async Task<ServiceResult> GetUserPermissionsAsync(int id, int companyId)
    {
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.CompanyId == companyId);
        if (user is null) return ServiceResult.NotFound();

        var flags = await db.EmployeePermissions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.UserId == id);
        var roleCodes = await db.RolePermissions.AsNoTracking()
            .Where(r => r.RoleId == user.RoleId && r.Permission != null)
            .Select(r => r.Permission!.SystemCode).Distinct().ToListAsync();

        return ServiceResult.Ok(new
        {
            canApplyDiscounts = flags?.CanApplyDiscounts ?? false,
            canViewPurchaseCosts = flags?.CanViewPurchaseCosts ?? false,
            canCancelSales = flags?.CanCancelSales ?? false,
            canEditProducts = flags?.CanEditProducts ?? false,
            canViewProfits = flags?.CanViewProfits ?? false,
            rolePermissions = user.RoleId is 1 or 2
                ? InventoryPermissionCodes.Definitions.Keys.ToList()
                : roleCodes
        });
    }

    public async Task<ServiceResult> UpdateUserPermissionsAsync(int id, UserPermissionUpdate input, int companyId, int actorUserId)
    {
        if (id == actorUserId)
            return ServiceResult.Conflict("No puedes cambiar tus propios permisos.");

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.CompanyId == companyId);
        if (user is null) return ServiceResult.NotFound();
        if (user.RoleId is 1 or 2) return ServiceResult.Forbid();

        var flags = await db.EmployeePermissions.FirstOrDefaultAsync(p =>
            p.CompanyId == companyId && p.UserId == id);
        if (flags is null)
        {
            flags = new EmployeePermission { CompanyId = companyId, UserId = id };
            db.EmployeePermissions.Add(flags);
        }
        flags.CanApplyDiscounts = input.CanApplyDiscounts;
        flags.CanViewPurchaseCosts = input.CanViewPurchaseCosts;
        flags.CanCancelSales = input.CanCancelSales;
        flags.CanEditProducts = input.CanEditProducts;
        flags.CanViewProfits = input.CanViewProfits;
        await db.SaveChangesAsync();
        return ServiceResult.NoContent();
    }

    public async Task<ServiceResult> ResendCredentialsAsync(int id, ResendCredentialsInput input, int companyId, int actorUserId, bool isDeveloper)
    {
        if (id == actorUserId)
            return ServiceResult.Conflict("Utiliza recuperación de cuenta para tus propias credenciales.");

        var target = await db.Users.Include(u => u.Employee)
            .FirstOrDefaultAsync(u => u.Id == id && u.CompanyId == companyId);
        if (target is null) return ServiceResult.NotFound("Usuario no encontrado en tu empresa.");
        if (target.RoleId == 1 || (target.RoleId == 2 && !isDeveloper)) return ServiceResult.Forbid();

        var email = string.IsNullOrWhiteSpace(input.CorrectedEmail)
            ? target.Email?.Trim() : input.CorrectedEmail.Trim();
        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email) || email.Length > 254)
            return ServiceResult.BadRequest("Ingresa un correo válido.");
        if (await db.Users.AnyAsync(u => u.Id != target.Id && u.Email != null && u.Email.ToLower() == email.ToLower()))
            return ServiceResult.Conflict("El correo ya pertenece a otro usuario.");
        if (!await mailer.IsConfiguredAsync(companyId))
            return ServiceResult.ServiceUnavailable("SMTP no configurado. Revisa Ajustes antes de reenviar.");

        var generated = UserCredentialMailer.NewTemporaryPassword();
        target.Email = email;
        target.Password = PasswordHashService.HashPassword(generated);
        target.MustChangePassword = true;
        await db.SaveChangesAsync();

        var delivered = await mailer.SendAsync(companyId, email,
            target.Employee?.FirstName ?? target.Username ?? "Usuario",
            target.Username ?? string.Empty, generated);

        return ServiceResult.Ok(new
        {
            emailSent = delivered.Sent,
            message = delivered.Sent
                ? "Nuevas credenciales enviadas. La contraseña anterior ya no funciona."
                : delivered.Message,
            email
        });
    }

    public async Task<ServiceResult> UpdateAccessAsync(int id, UserAccessUpdate input, int companyId, int actorUserId, Func<int, Task<bool>> canAssignRoleAsync)
    {
        if (id == actorUserId)
            return ServiceResult.Conflict("No puedes modificar tu propio estado o rol.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.CompanyId == companyId);
        if (user is null) return ServiceResult.NotFound();
        if (user.RoleId is 1 or 2) return ServiceResult.Forbid();

        if (input.RoleId.HasValue)
        {
            if (!await canAssignRoleAsync(input.RoleId.Value)) return ServiceResult.Forbid();
            user.RoleId = input.RoleId.Value;
        }
        user.IsActive = input.IsActive;
        await db.SaveChangesAsync();
        return ServiceResult.NoContent();
    }
}