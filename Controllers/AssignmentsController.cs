using InventoryAPI.Services;
using InventoryAPI.Services.IServices;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Controllers;

[Route("api/Assignments")]
public sealed class AssignmentsController(IAssignmentService assignmentService) : BaseApiController
{
    [HttpGet("outgoing")]
    public async Task<IActionResult> Outgoing()
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return Ok(await assignmentService.GetOutgoingAsync(companyId, userId, EsDesarrollador));
    }

    [HttpGet("received")]
    public async Task<IActionResult> Received()
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return Ok(await assignmentService.GetReceivedAsync(companyId, userId));
    }

    [HttpGet("{id:int}/events")]
    public async Task<IActionResult> Events(int id)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await assignmentService.GetEventsAsync(id, companyId, userId, EsDesarrollador));
    }

    [HttpGet("recipients")]
    public async Task<IActionResult> Recipients([FromQuery] string? query)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return Ok(await assignmentService.GetRecipientsAsync(companyId, userId, query));
    }

    [HttpPost]
    public async Task<IActionResult> Assign([FromBody] AssignRequest request)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        var result = await assignmentService.AssignAsync(request, companyId, userId, EsDesarrollador);
        if (result.Status == ServiceStatus.Created && result.Payload is CreatedAssignmentResponse created)
            return CreatedAtAction(nameof(Events), new { id = created.Id }, new { id = created.Id });

        return FromServiceResult(result);
    }

    [HttpPost("{id:int}/evidence")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadEvidence(int id, IFormFile file, CancellationToken ct)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await assignmentService.UploadEvidenceAsync(id, file, companyId, userId, ct));
    }

    [HttpGet("{id:int}/evidence")]
    public async Task<IActionResult> GetEvidence(int id, CancellationToken ct)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        var result = await assignmentService.GetEvidenceAsync(id, companyId, userId, EsDesarrollador, ct);
        if (result.Error is not null) return FromServiceResult(result.Error);

        Response.Headers.CacheControl = "private, no-store";
        if (result.Bytes is not null)
            return File(result.Bytes, result.ContentType!);

        return PhysicalFile(result.PhysicalPath!, result.ContentType!);
    }

    [HttpPost("{id:int}/return")]
    public async Task<IActionResult> Return(int id, [FromBody] ReturnRequest request)
    {
        if (!TryObtenerAmbito(out var companyId, out var userId)) return Unauthorized();
        return FromServiceResult(await assignmentService.ReturnAsync(id, request, companyId, userId, EsDesarrollador));
    }
}