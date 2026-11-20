using InventoryAPI.Models;
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
        InventoryAccessPolicy access) : BaseApiController
    {
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserDTO>>> GetUsers()
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();
            int companyId = ObtenerEmpresaSegura();
            var users = await userService.GetUsersDtoAsync();
            return Ok(users.Where(u => u.CompanyId == companyId).ToList());
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<UserDTO>> GetUser(int id)
        {
            if (id != ObtenerUsuarioSeguro() && !await access.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();
            int companyId = ObtenerEmpresaSegura();
            var userDto = await userService.GetUserDtoByIdAsync(id);
            if (userDto == null || userDto.CompanyId != companyId)
                return NotFound(new { mensaje = "El usuario no fue localizado o no tienes permisos." });
            return Ok(userDto);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutUser(int id, [FromBody] User userActualizado)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();
            int companyId = ObtenerEmpresaSegura();
            userActualizado.CompanyId = companyId;

            var existingUser = await userService.GetUserDtoByIdAsync(id);
            if (existingUser == null || existingUser.CompanyId != companyId)
                return NotFound(new { mensaje = "El usuario no existe o no pertenece a tu sucursal." });

            if (existingUser.RoleId == 1 || (existingUser.RoleId == 2 && !EsDesarrollador)) return Forbid();
            if (userActualizado.RoleId is 1 or 2) return Forbid();
            if (userActualizado.RoleId > 0 && !await access.CanAssignRoleAsync(User, userActualizado.RoleId)) return Forbid();
            if (id == ObtenerUsuarioSeguro() && userActualizado.RoleId > 0 && userActualizado.RoleId != existingUser.RoleId)
                return Conflict(new { error = "No puedes cambiar tu propio rol." });

            userActualizado.Password = null;
            var (Success, Message) = await userService.UpdateUserAsync(id, userActualizado);
            if (!Success)
            {
                if (Message.Contains("no existe")) return NotFound(new { mensaje = Message });
                if (Message.Contains("no coincide")) return BadRequest(new { mensaje = Message });
                return StatusCode(500, new { error = "Error al editar", detalle = Message });
            }
            return NoContent();
        }

        [HttpPost]
        public async Task<ActionResult<User>> PostUser([FromBody] User user)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageUsers)) return Forbid();
            if (user.RoleId is 1 or 2)
                return Conflict(new { error = "Usa Gestión Dev para registrar el administrador inicial. El rol desarrollador está protegido." });
            if (!await access.CanAssignRoleAsync(User, user.RoleId)) return Forbid();
            if (string.IsNullOrWhiteSpace(user.Password) || user.Password.Length < 12)
                return BadRequest(new { error = "Se requiere una contraseña de al menos 12 caracteres." });

            int companyId = ObtenerEmpresaSegura();
            user.CompanyId = companyId;

            var (allowed, licenseError) = await licenseService.CanCreateUserAsync(companyId);
            if (!allowed) return Conflict(new { error = licenseError });

            var result = await userService.CreateUserAsync(user, env.ContentRootPath);
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
            var existingUser = await userService.GetUserDtoByIdAsync(id);
            if (existingUser == null || existingUser.CompanyId != companyId) return NotFound();
            if (existingUser.RoleId == 1 || (existingUser.RoleId == 2 && !EsDesarrollador)) return Forbid();
            if (existingUser.RoleId == 2 && await userService.CountActiveAdminsAsync(companyId) <= 1)
                return Conflict(new { error = "No se puede desactivar el último administrador." });

            var success = await userService.DeleteAsync(id, ObtenerUsuarioSeguro().ToString());
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
            return Ok(new { u.Id, u.Username, u.Email, u.RoleId, companyId = request.CompanyId, u.Token });
        }

        [HttpPost("ChangeInitialPassword")]
        [AllowAnonymous]
        public IActionResult ChangeInitialPassword() =>
            StatusCode(410, new { error = "Utiliza la recuperación aprobada en Gestión Dev." });

        [HttpPut("{id}/UpdatePhoto")]
        public async Task<IActionResult> UpdatePhoto(int id, [FromBody] PhotoUpdateDTO request)
        {
            if (!await CanManageTargetAsync(id)) return Forbid();
            var result = await userService.UpdatePhotoAsync(id, request.Base64Image, env.ContentRootPath);
            if (!result.Success)
            {
                if (result.Message.Contains("no encontrado")) return NotFound(new { mensaje = result.Message });
                if (result.Message.Contains("ninguna imagen")) return BadRequest(new { mensaje = result.Message });
                return StatusCode(500, new { mensaje = "Fallo al guardar imagen en Somee.", detalle = result.Message });
            }
            return Ok(new { result.Url, mensaje = result.Message });
        }

        [HttpPost("{id}/generate-2fa")]
        public async Task<IActionResult> Generate2FA(int id)
        {
            if (id != ObtenerUsuarioSeguro()) return Forbid();
            var (Success, Secret, QrUri) = await userService.Generate2FAAsync(id);
            return Success ? Ok(new { secret = Secret, qrUri = QrUri }) : NotFound();
        }

        [HttpPost("{id}/enable-2fa")]
        public async Task<IActionResult> Enable2FA(int id, [FromBody] string code)
        {
            if (id != ObtenerUsuarioSeguro()) return Forbid();
            return await userService.Enable2FAAsync(id, code)
                ? Ok(new { mensaje = "Activado" })
                : BadRequest(new { mensaje = "Inválido" });
        }

        [HttpPost("{id}/disable-2fa")]
        public async Task<IActionResult> Disable2FA(int id)
        {
            if (id != ObtenerUsuarioSeguro()) return Forbid();
            return await userService.Disable2FAAsync(id) ? Ok(new { mensaje = "Desactivado" }) : NotFound();
        }

        [HttpPost("TestEmailConfiguration")]
        public async Task<IActionResult> TestEmailConfiguration([FromBody] SmtpTestRequest request)
        {
            if (!EsDesarrollador) return Forbid();
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { mensaje = "El correo y la contraseña son obligatorios." });

            var (Success, Message) = await userService.TestEmailConnectionAsync(request.Email, request.Password);
            return Success ? Ok(new { mensaje = Message }) : StatusCode(500, new { error = "Fallo de conexión", detalle = Message });
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

            var (success, message) = await userService.ApproveEmployeeAsync(id);
            return success ? Ok(new { message }) : Conflict(new { error = message });
        }

        [HttpPut("{id}/fcm-token")]
        public async Task<IActionResult> UpdateFcmToken(int id, [FromBody] string fcmToken)
        {
            if (id != ObtenerUsuarioSeguro()) return Forbid();
            if (string.IsNullOrWhiteSpace(fcmToken)) return BadRequest(new { mensaje = "El token no puede estar vacío." });
            return await userService.UpdateFcmTokenAsync(id, fcmToken)
                ? Ok(new { mensaje = "Token de notificaciones actualizado correctamente." })
                : NotFound(new { mensaje = "Usuario no encontrado." });
        }

        [HttpPost("test-push")]
        public async Task<IActionResult> TestPushNotification([FromBody] string fcmToken)
        {
            if (!EsDesarrollador) return Forbid();
            var path = Path.Combine(env.ContentRootPath, "firebase-key.json");
            if (!System.IO.File.Exists(path))
                return StatusCode(500, new { mensaje = $"ERROR 1: Archivo clave no encontrado en {path}" });
            if (FirebaseAdmin.FirebaseApp.DefaultInstance == null)
                return StatusCode(500, new { mensaje = "ERROR 2: Firebase no inicializado." });

            bool result = await fcmNotificationService.SendPushNotificationAsync(
                fcmToken, "¡Prueba de Fuego!", "Notificación enviada desde InventoryAPI.");
            return result
                ? Ok(new { mensaje = "¡Notificación enviada con éxito a Firebase!" })
                : StatusCode(500, new { mensaje = "ERROR 3: Falló el envío en FcmNotificationService." });
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