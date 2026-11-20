using InventoryAPI.Models.DTO;
using InventoryAPI.Security;
using InventoryAPI.Services;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers
{
    public class ProfilesController(
        IProfileService profileService,
        InventoryAccessPolicy access) : BaseApiController
    {
        [HttpGet]
        public async Task<IActionResult> GetProfiles()
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ViewMaster)) return Forbid();
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            return Ok(await profileService.GetProfilesByCompanyAsync(companyId));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetProfile(int id)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ViewMaster)) return Forbid();
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            var profile = await profileService.GetProfileByIdAsync(id, companyId);
            return profile is null ? NotFound() : Ok(profile);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> PutProfile(int id, [FromBody] ProfileWriteDto profile)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            return FromServiceResult(await profileService.UpdateProfileAsync(id, profile, companyId));
        }

        [HttpPost]
        public async Task<IActionResult> PostProfile([FromBody] ProfileWriteDto profile)
        {
            if (!await access.HasAsync(User, InventoryPermissionCodes.ManageMaster)) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            var result = await profileService.CreateProfileAsync(profile, companyId);
            if (result.Status == ServiceStatus.Created && result.Payload is ProfileReadDto created)
                return CreatedAtAction(nameof(GetProfile), new { id = created.Id }, created);

            return FromServiceResult(result);
        }

        [HttpGet("user/{username}")]
        public async Task<IActionResult> GetProfileByUsername(string username)
        {
            var companyId = ObtenerEmpresaSegura();
            if (companyId <= 0) return Unauthorized();

            var currentUsername = User.Identity?.Name ?? User.FindFirst("Username")?.Value;
            var isOwnProfile = !string.IsNullOrWhiteSpace(currentUsername) &&
                string.Equals(currentUsername, username, StringComparison.OrdinalIgnoreCase);

            if (!isOwnProfile && !await access.HasAsync(User, InventoryPermissionCodes.ViewMaster))
                return Forbid();

            var profile = await profileService.GetProfileByUsernameAsync(username, companyId);
            return profile is null ? NotFound() : Ok(profile);
        }
    }
}