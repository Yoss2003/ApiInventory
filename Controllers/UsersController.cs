using InventoryAPI.Models;
using InventoryAPI.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using InventoryAPI.Models.DTO;
using InventoryAPI.Security;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InventoryAPI.Controllers
{
    public class UsersController(
        IUserService userService,
        IWebHostEnvironment env,
        IFcmNotificationService fcmNotificationService,
        ICompanyLicenseService licenseService,
        InventoryAccessPolicy access,
        AppDbContext db,
        IConfiguration configuration) : BaseApiController
    {
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserDTO>>> GetUsers()
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();
            int companyId = ObtenerEmpresaSegura();
            return Ok(await userService.GetUsersDtoByCompanyAsync(companyId));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<UserDTO>> GetUser(int id)
        {
            if (id != ObtenerUsuarioSeguro() && !await access.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();
            int companyId = ObtenerEmpresaSegura();
            var userDto = await userService.GetUserDtoByIdAsync(id, companyId);
            if (userDto == null || userDto.CompanyId != companyId)
                return NotFound(new { mensaje = "El usuario no fue localizado o no tienes permisos." });
            return Ok(userDto);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutUser(int id, [FromBody] UserUpdateAdminDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();
            int companyId = ObtenerEmpresaSegura();

            var existingUser = await userService.GetUserDtoByIdAsync(id, companyId);
            if (existingUser == null || existingUser.CompanyId != companyId)
                return NotFound(new { mensaje = "El usuario no existe o no pertenece a tu sucursal." });

            if (existingUser.RoleId == 1 || (existingUser.RoleId == 2 && !EsDesarrollador)) return Forbid();
            if (dto.RoleId is 1 or 2) return Forbid();
            if (dto.RoleId > 0 && !await access.CanAssignRoleAsync(User, dto.RoleId)) return Forbid();
            if (id == ObtenerUsuarioSeguro() && dto.RoleId > 0 && dto.RoleId != existingUser.RoleId)
                return Conflict(new { error = "No puedes cambiar tu propio rol." });

            var userActualizado = new User
            {
                Id = id,
                CompanyId = companyId,
                Username = dto.Username ?? existingUser.Username,
                Email = dto.Email ?? existingUser.Email,
                RoleId = dto.RoleId > 0 ? dto.RoleId : existingUser.RoleId,
                IsActive = dto.IsActive ?? existingUser.IsActive,
                Password = null
            };

            var (Success, Message) = await userService.UpdateUserAsync(id, userActualizado, companyId);
            if (!Success)
            {
                if (Message.Contains("no existe")) return NotFound(new { mensaje = Message });
                if (Message.Contains("no coincide")) return BadRequest(new { mensaje = Message });
                return StatusCode(500, new { error = "Error al editar el usuario." });
            }
            return NoContent();
        }

        [HttpPost]
        public async Task<IActionResult> PostUser([FromBody] UserCreateAdminDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();
            if (dto.RoleId is 1 or 2)
                return Conflict(new { error = "Usa Gestión Dev para registrar el administrador inicial. El rol desarrollador está protegido." });
            if (!await access.CanAssignRoleAsync(User, dto.RoleId)) return Forbid();

            int companyId = ObtenerEmpresaSegura();

            var (allowed, licenseError) = await licenseService.CanCreateUserAsync(companyId);
            if (!allowed) return Conflict(new { error = licenseError });

            var user = new User
            {
                Username = dto.Username.Trim(),
                Email = dto.Email.Trim(),
                Password = dto.Password,
                RoleId = dto.RoleId,
                CompanyId = companyId
            };

            var result = await userService.CreateUserAsync(user, companyId, env.ContentRootPath);
            if (!result.Success) return BadRequest(result.Data);

            var created = (User)result.Data;
            return CreatedAtAction(nameof(GetUser), new { id = created.Id }, new
            {
                created.Id,
                created.Username,
                created.Email,
                created.RoleId,
                created.CompanyId,
                personalInventoryProvisioned = true
            });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();
            if (id == ObtenerUsuarioSeguro()) return Conflict(new { error = "No puedes desactivar tu propia cuenta." });

            int companyId = ObtenerEmpresaSegura();
            var existingUser = await userService.GetUserDtoByIdAsync(id, companyId);
            if (existingUser == null || existingUser.CompanyId != companyId) return NotFound();
            if (existingUser.RoleId == 1 || (existingUser.RoleId == 2 && !EsDesarrollador)) return Forbid();
            if (existingUser.RoleId == 2 && await userService.CountActiveAdminsAsync(companyId) <= 1)
                return Conflict(new { error = "No se puede desactivar el último administrador." });

            var success = await userService.DeleteUserInCompanyAsync(id, companyId, ObtenerUsuarioSeguro().ToString());
            return success ? NoContent() : NotFound();
        }

        [EnableRateLimiting("auth")]
        [HttpPost("Login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequestDTO request)
        {
            var result = await userService.LoginAsync(request);
            if (!result.Success)
            {
                if (result.Requires2FA) return Unauthorized(new { requires2FA = true, mensaje = result.Message });
                if (result.AccountPending) return Unauthorized(new { accountPending = true, mensaje = result.Message });
                return Unauthorized(new { mensaje = result.Message });
            }
            if (result.RequirePasswordChange)
            {
                var token = await userService.CreateForcedPasswordResetTokenAsync(result.User!.Id, request.CompanyId);
                return Ok(new { requirePasswordChange = true, passwordResetToken = token, expiresInMinutes = 15 });
            }
            var u = result.User!;
            var now = DateTime.UtcNow;
            var expiresAt = now.Add(request.RememberMe ? TimeSpan.FromDays(30) : TimeSpan.FromHours(8));
            var refreshToken = CreateOpaqueToken();
            var session = new RefreshSession
            {
                Id = Guid.NewGuid(),
                UserId = u.Id,
                CompanyId = request.CompanyId,
                TokenHash = HashToken(refreshToken),
                CreatedAt = now,
                ExpiresAt = expiresAt,
                RememberMe = request.RememberMe
            };
            db.RefreshSessions.Add(session);
            await db.SaveChangesAsync();
            WriteRefreshCookie(refreshToken, session, now);
            Response.Headers.CacheControl = "no-store";
            var accessToken = CreateAccessToken(u, request.CompanyId, session.Id, now);
            return Ok(new { u.Id, u.Username, u.Email, u.RoleId, companyId = request.CompanyId, token = accessToken });
        }

        [EnableRateLimiting("auth")]
        [HttpPost("RefreshSession")]
        [AllowAnonymous]
        public async Task<IActionResult> RefreshSession()
        {
            if (!IsTrustedSessionRequest()) return Forbid();
            var currentToken = Request.Cookies[RefreshCookieName];
            if (string.IsNullOrWhiteSpace(currentToken)) return Unauthorized();

            var tokenHash = HashToken(currentToken);
            var now = DateTime.UtcNow;
            var session = await db.RefreshSessions.FirstOrDefaultAsync(x =>
                x.TokenHash == tokenHash && x.RevokedAt == null && x.ExpiresAt > now);
            if (session is null)
            {
                ClearRefreshCookie();
                return Unauthorized();
            }

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == session.UserId);
            var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == session.CompanyId);
            var valid = user is not null && company is not null &&
                user.IsActive && user.StatusId == 1 &&
                (user.RoleId == 1 || user.CompanyId == session.CompanyId) &&
                company.IsActive;

            if (valid && user!.RoleId != 1)
            {
                var license = await db.CompanyLicenses.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.CompanyId == session.CompanyId);
                valid = license is null || (license.IsEnabled && license.ExpiresAt > now);
            }

            if (valid)
            {
                var lastPasswordRecovery = await db.PasswordRecoveryRequests.AsNoTracking()
                    .Where(x => x.UserId == session.UserId && x.CompletedAt != null)
                    .MaxAsync(x => (DateTime?)x.CompletedAt);
                if (lastPasswordRecovery.HasValue && lastPasswordRecovery.Value >= session.CreatedAt)
                    valid = false;
            }

            if (!valid)
            {
                session.RevokedAt = now;
                await db.SaveChangesAsync();
                ClearRefreshCookie();
                return Unauthorized();
            }

            // Rotación: el refresh anterior deja de ser válido en cuanto se emite el nuevo.
            var nextRefreshToken = CreateOpaqueToken();
            session.TokenHash = HashToken(nextRefreshToken);
            session.LastUsedAt = now;
            await db.SaveChangesAsync();
            WriteRefreshCookie(nextRefreshToken, session, now);
            Response.Headers.CacheControl = "no-store";

            var accessToken = CreateAccessToken(user!, session.CompanyId, session.Id, now);
            return Ok(new
            {
                id = user!.Id,
                username = user.Username,
                email = user.Email,
                roleId = user.RoleId,
                companyId = session.CompanyId,
                token = accessToken
            });
        }

        [EnableRateLimiting("auth")]
        [HttpPost("Logout")]
        [AllowAnonymous]
        public async Task<IActionResult> Logout()
        {
            if (!IsTrustedSessionRequest()) return Forbid();
            var currentToken = Request.Cookies[RefreshCookieName];
            if (!string.IsNullOrWhiteSpace(currentToken))
            {
                var tokenHash = HashToken(currentToken);
                var session = await db.RefreshSessions.FirstOrDefaultAsync(x =>
                    x.TokenHash == tokenHash && x.RevokedAt == null);
                if (session is not null)
                {
                    session.RevokedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            ClearRefreshCookie();
            Response.Headers.CacheControl = "no-store";
            return NoContent();
        }

        private const string RefreshCookieName = "OmniStock.Session";

        private bool IsTrustedSessionRequest()
        {
            if (!string.Equals(Request.Headers["X-OmniStock-Request"].ToString(), "1", StringComparison.Ordinal))
                return false;
            var origin = Request.Headers["Origin"].ToString();
            if (string.IsNullOrWhiteSpace(origin)) return false;
            var allowed = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            if (allowed.Length == 0 && env.IsDevelopment()) allowed = ["http://localhost:4200"];
            return allowed.Contains(origin, StringComparer.OrdinalIgnoreCase);
        }

        private static string CreateOpaqueToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string HashToken(string token)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        private string CreateAccessToken(User user, int companyId, Guid sessionId, DateTime now)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
            var signing = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Username ?? string.Empty),
                new Claim("UserId", user.Id.ToString()),
                new Claim("CompanyId", companyId.ToString()),
                new Claim("RoleId", user.RoleId.ToString()),
                new Claim("sid", sessionId.ToString("D")),
                new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            };
            var token = new JwtSecurityToken(
                issuer: configuration["Jwt:Issuer"],
                audience: configuration["Jwt:Audience"],
                claims: claims,
                notBefore: now,
                expires: now.AddMinutes(15),
                signingCredentials: signing);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private void WriteRefreshCookie(string token, RefreshSession session, DateTime now)
        {
            var options = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                IsEssential = true,
                Path = "/api/Users"
            };
            if (session.RememberMe)
            {
                options.Expires = new DateTimeOffset(DateTime.SpecifyKind(session.ExpiresAt, DateTimeKind.Utc));
                options.MaxAge = session.ExpiresAt - now;
            }
            Response.Cookies.Append(RefreshCookieName, token, options);
        }

        private void ClearRefreshCookie()
        {
            Response.Cookies.Delete(RefreshCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                IsEssential = true,
                Path = "/api/Users"
            });
        }

        [HttpPost("ChangeInitialPassword")]
        [AllowAnonymous]
        public IActionResult ChangeInitialPassword() =>
            StatusCode(410, new { error = "Utiliza la recuperación aprobada en Gestión Dev." });

        [HttpPut("{id}/UpdatePhoto")]
        public async Task<IActionResult> UpdatePhoto(int id, [FromBody] PhotoUpdateDTO request)
        {
            if (!await CanManageTargetAsync(id)) return Forbid();
            var result = await userService.UpdatePhotoAsync(id, ObtenerEmpresaSegura(), request.Base64Image, env.ContentRootPath);
            if (!result.Success)
            {
                if (result.Message.Contains("no encontrado")) return NotFound(new { mensaje = result.Message });
                if (result.Message.Contains("ninguna imagen")) return BadRequest(new { mensaje = result.Message });
                return StatusCode(500, new { mensaje = "No fue posible guardar la imagen. Intenta nuevamente." });
            }
            return Ok(new { result.Url, mensaje = result.Message });
        }

        [HttpPost("{id}/generate-2fa")]
        public async Task<IActionResult> Generate2FA(int id)
        {
            if (id != ObtenerUsuarioSeguro()) return Forbid();
            var (Success, Secret, QrUri) = await userService.Generate2FAAsync(id, ObtenerEmpresaSegura());
            return Success ? Ok(new { secret = Secret, qrUri = QrUri }) : NotFound();
        }

        [EnableRateLimiting("auth")]
        [HttpPost("{id}/enable-2fa")]
        public async Task<IActionResult> Enable2FA(int id, [FromBody] string code)
        {
            if (id != ObtenerUsuarioSeguro()) return Forbid();
            return await userService.Enable2FAAsync(id, ObtenerEmpresaSegura(), code)
                ? Ok(new { mensaje = "Activado" })
                : BadRequest(new { mensaje = "Inválido" });
        }

        [HttpPost("{id}/disable-2fa")]
        public async Task<IActionResult> Disable2FA(int id)
        {
            if (id != ObtenerUsuarioSeguro()) return Forbid();
            return await userService.Disable2FAAsync(id, ObtenerEmpresaSegura()) ? Ok(new { mensaje = "Desactivado" }) : NotFound();
        }

        [EnableRateLimiting("auth")]
        [HttpPost("TestEmailConfiguration")]
        public async Task<IActionResult> TestEmailConfiguration([FromBody] SmtpTestRequest request)
        {
            if (!EsDesarrollador) return Forbid();
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { mensaje = "El correo y la contraseña son obligatorios." });

            var (Success, Message) = await userService.TestEmailConnectionAsync(request.Email, request.Password);
            return Success ? Ok(new { mensaje = Message }) : StatusCode(502, new { error = "No fue posible completar la prueba SMTP. Verifica la configuración y los registros del servidor." });
        }

        [HttpGet("ApproveAccount/{id:int}")]
        [AllowAnonymous]
        public IActionResult LegacyApproveDisabled(int id) =>
            StatusCode(410, new { error = "La aprobación por enlace público ha sido retirada. Usa Gestión de usuarios." });

        [HttpPost("ApproveAccount/{id:int}")]
        public async Task<IActionResult> ApproveEmployee(int id)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();
            var roleId = await userService.GetUserRoleIdInCompanyAsync(id, ObtenerEmpresaSegura());
            if (!roleId.HasValue) return NotFound();
            if (roleId.Value == 1 || (roleId.Value == 2 && !EsDesarrollador)) return Forbid();

            var (success, message) = await userService.ApproveEmployeeAsync(id, ObtenerEmpresaSegura());
            return success ? Ok(new { message }) : Conflict(new { error = message });
        }

        [HttpPut("{id}/firebase-installation-id")]
        public async Task<IActionResult> UpdateFirebaseInstallationId(int id, [FromBody] string firebaseInstallationId)
        {
            if (id != ObtenerUsuarioSeguro()) return Forbid();
            if (string.IsNullOrWhiteSpace(firebaseInstallationId))
                return BadRequest(new { mensaje = "El Firebase Installation ID (FID) no puede estar vacío." });

            return await userService.UpdateFcmTokenAsync(id, ObtenerEmpresaSegura(), firebaseInstallationId.Trim())
                ? Ok(new { mensaje = "Firebase Installation ID actualizado correctamente." })
                : NotFound(new { mensaje = "Usuario no encontrado." });
        }

        [HttpPost("test-push")]
        public async Task<IActionResult> TestPushNotification([FromBody] string firebaseInstallationId)
        {
            if (!EsDesarrollador) return Forbid();
            if (string.IsNullOrWhiteSpace(firebaseInstallationId))
                return BadRequest(new { mensaje = "El Firebase Installation ID (FID) es obligatorio." });
            if (FirebaseAdmin.FirebaseApp.DefaultInstance == null)
                return StatusCode(500, new { mensaje = "Firebase no está inicializado en el servidor." });

            bool result = await fcmNotificationService.SendPushNotificationAsync(
                firebaseInstallationId, "Prueba OmniStock", "Notificación enviada desde InventoryAPI.");

            return result
                ? Ok(new { mensaje = "Notificación enviada con éxito a Firebase." })
                : StatusCode(500, new { mensaje = "No se pudo enviar la notificación. Revisa los registros del servidor." });
        }

        [HttpPost("{id}/resend-welcome")]
        public IActionResult LegacyWelcomeDisabled(int id) =>
            StatusCode(410, new { error = "Utiliza POST /api/UserManagement/users/{id}/resend-credentials con permisos de gestión." });

        private async Task<bool> CanManageTargetAsync(int id)
        {
            if (!await userService.UserBelongsToCompanyAsync(id, ObtenerEmpresaSegura())) return false;
            return id == ObtenerUsuarioSeguro() || await access.HasAsync(User, InventoryPermissionCodes.ManageUsers);
        }

        public class SmtpTestRequest
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        public class PhotoUpdateDTO
        {
            public string Base64Image { get; set; } = string.Empty;
        }
    }
}