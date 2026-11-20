using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

[Route("api/password-recovery")]
public sealed class PasswordRecoveryController(IPasswordRecoveryService recoveryService) : BaseApiController
{
    private bool IsValidDeveloper => EsDesarrollador && ObtenerUsuarioSeguro() > 0;

    [AllowAnonymous]
    [HttpPost("request")]
    public async Task<IActionResult> RequestReset([FromBody] PasswordRecoveryRequestDto input) =>
        Accepted(await recoveryService.RequestResetAsync(input));

    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        if (!IsValidDeveloper) return Forbid();
        return Ok(await recoveryService.GetPendingAsync());
    }

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        if (!IsValidDeveloper) return Forbid();
        return FromServiceResult(await recoveryService.ApproveAsync(id, ObtenerUsuarioSeguro()));
    }

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id)
    {
        if (!IsValidDeveloper) return Forbid();
        return FromServiceResult(await recoveryService.RejectAsync(id));
    }

    [AllowAnonymous]
    [HttpPost("complete")]
    public async Task<IActionResult> Complete([FromBody] PasswordRecoveryCompleteDto input) =>
        FromServiceResult(await recoveryService.CompleteAsync(input));
}