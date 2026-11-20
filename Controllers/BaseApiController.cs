using InventoryAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public abstract class BaseApiController : ControllerBase
    {
        protected int ObtenerEmpresaSegura()
        {
            var companyClaim = User.FindFirst("CompanyId")?.Value;
            return int.TryParse(companyClaim, out int companyId) && companyId > 0 ? companyId : 0;
        }

        protected int ObtenerUsuarioSeguro()
        {
            var userClaim = User.FindFirst("UserId")?.Value;
            return int.TryParse(userClaim, out int userId) && userId > 0 ? userId : 0;
        }

        protected int ObtenerRolSeguro()
        {
            var roleClaim = User.FindFirst("RoleId")?.Value;
            return int.TryParse(roleClaim, out int roleId) && roleId > 0 ? roleId : 0;
        }

        protected bool EsDesarrollador => ObtenerRolSeguro() == 1;
        protected bool EsAdministrador => ObtenerRolSeguro() == 2;

        protected bool TryObtenerAmbito(out int companyId, out int userId)
        {
            companyId = ObtenerEmpresaSegura();
            userId = ObtenerUsuarioSeguro();
            return companyId > 0 && userId > 0;
        }

        protected IActionResult FromServiceResult(ServiceResult result) => result.Status switch
        {
            ServiceStatus.Ok => result.Payload is null ? Ok() : Ok(result.Payload),
            ServiceStatus.NoContent => NoContent(),
            ServiceStatus.BadRequest => BadRequest(result.Payload ?? new { error = result.Error }),
            ServiceStatus.Forbid => Forbid(),
            ServiceStatus.NotFound => result.Error is null ? NotFound() : NotFound(new { error = result.Error }),
            ServiceStatus.Conflict => Conflict(result.Payload ?? new { error = result.Error }),
            ServiceStatus.ServiceUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Error }),
            ServiceStatus.BadGateway => StatusCode(StatusCodes.Status502BadGateway, new { error = result.Error }),
            ServiceStatus.GatewayTimeout => StatusCode(StatusCodes.Status504GatewayTimeout, new { error = result.Error }),
            _ => Ok(result.Payload)
        };
    }
}