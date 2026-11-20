namespace InventoryAPI.Services.IServices;

public sealed record AssignRequest(
    int ArticleId,
    decimal Quantity,
    string RecipientName,
    int? RecipientUserId,
    string? Observation,
    Guid OperationKey,
    List<int>? DetailIds);

public sealed record ReturnRequest(
    decimal Quantity,
    string? Observation,
    Guid OperationKey,
    List<int>? DetailIds);

public sealed record AssignmentSummary(
    int Id,
    int ArticleId,
    int InventoryId,
    string RecipientName,
    int? RecipientUserId,
    int AssignedByUserId,
    DateTime AssignedAt,
    decimal Quantity,
    decimal ReturnedQuantity,
    decimal RemainingQuantity,
    string Status,
    string? Observation,
    bool HasEvidence,
    string? ArticleName,
    string? ArticleCode,
    string? MeasurementUnit);

public sealed record CreatedAssignmentResponse(int Id);

public interface IAssignmentService
{
    Task<List<AssignmentSummary>> GetOutgoingAsync(int companyId, int userId, bool isDeveloper);
    Task<List<AssignmentSummary>> GetReceivedAsync(int companyId, int userId);
    Task<ServiceResult> GetEventsAsync(int id, int companyId, int userId, bool isDeveloper);
    Task<object> GetRecipientsAsync(int companyId, int userId, string? query);
    Task<ServiceResult> AssignAsync(AssignRequest request, int companyId, int userId, bool isDeveloper);
    Task<ServiceResult> UploadEvidenceAsync(int id, IFormFile file, int companyId, int userId, CancellationToken ct);
    Task<MediaFileResult> GetEvidenceAsync(int id, int companyId, int userId, bool isDeveloper, CancellationToken ct);
    Task<ServiceResult> ReturnAsync(int id, ReturnRequest request, int companyId, int userId, bool isDeveloper);
}