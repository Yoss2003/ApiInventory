using InventoryAPI.Models.DTO;
using InventoryAPI.Security;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

public sealed class UserManagementController(
    IUserManagementService userManagement,
    InventoryAccessPolicy permissions,
    IManagedUserRegistration registration) : BaseApiController
{
    private bool IsManager => InventoryAccessPolicy.IsManager(User);
    private async Task<bool> CanViewAsync() => IsManager && await permissions.HasAsync(User, InventoryPermissionCodes.ViewUsers);
    private async Task<bool> CanEditAsync() => IsManager && await permissions.HasAsync(User, InventoryPermissionCodes.ManageUsers);

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        if (!await CanViewAsync()) return Forbid();
        return Ok(await userManagement.ListUsersAsync(ObtenerEmpresaSegura()));
    }

    [HttpGet("roles")]
    public async Task<IActionResult> Roles()
    {
        if (!await CanViewAsync()) return Forbid();
        return Ok(await userManagement.GetAssignableRolesAsync());
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] ManagedUserInput input)
    {
        if (!await CanEditAsync()) return Forbid();
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (!await permissions.CanAssignRoleAsync(User, input.RoleId)) return Forbid();

        var result = await registration.RegisterAsync(ObtenerEmpresaSegura(), input, firstAdministrator: false);
        return result.Success ? Ok(result.Created) : Conflict(new { error = result.Error });
    }

    [HttpPatch("users/{id:int}/details")]
    public async Task<IActionResult> UpdateDetails(int id, [FromBody] UserDetailsUpdate input)
    {
        if (!await CanEditAsync()) return Forbid();
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return FromServiceResult(await userManagement.UpdateDetailsAsync(id, input, ObtenerEmpresaSegura(), EsDesarrollador));
    }

    [HttpGet("users/{id:int}/permissions")]
    public async Task<IActionResult> UserPermissions(int id)
    {
        if (!await CanViewAsync()) return Forbid();
        return FromServiceResult(await userManagement.GetUserPermissionsAsync(id, ObtenerEmpresaSegura()));
    }

    [HttpPut("users/{id:int}/permissions")]
    public async Task<IActionResult> UpdateUserPermissions(int id, [FromBody] UserPermissionUpdate input)
    {
        if (!await CanEditAsync()) return Forbid();
        return FromServiceResult(await userManagement.UpdateUserPermissionsAsync(id, input, ObtenerEmpresaSegura(), ObtenerUsuarioSeguro()));
    }

    [HttpPost("users/{id:int}/resend-credentials")]
    public async Task<IActionResult> ResendCredentials(int id, [FromBody] ResendCredentialsInput input)
    {
        if (!await CanEditAsync()) return Forbid();
        return FromServiceResult(await userManagement.ResendCredentialsAsync(id, input, ObtenerEmpresaSegura(), ObtenerUsuarioSeguro(), EsDesarrollador));
    }

    [HttpPatch("users/{id:int}/access")]
    public async Task<IActionResult> UpdateAccess(int id, [FromBody] UserAccessUpdate input)
    {
        if (!await CanEditAsync()) return Forbid();
        return FromServiceResult(await userManagement.UpdateAccessAsync(
            id, input, ObtenerEmpresaSegura(), ObtenerUsuarioSeguro(),
            roleId => permissions.CanAssignRoleAsync(User, roleId)));
    }
}