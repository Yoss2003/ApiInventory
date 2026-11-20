using System.ComponentModel.DataAnnotations;

namespace InventoryAPI.Services.IServices;

public sealed record RegisterUnitRequest(
    [Required] string SerialNumber,
    string? Attr1,
    string? Attr2,
    string? Attr3,
    string? Attr4,
    string? Attr5,
    string? Attr6);

public sealed record RegisterUnitBatchRequest(List<RegisterUnitRequest> Units);

public sealed record DecommissionUnitRequest([Required] int DetailId, [Required] string Observation);

public interface ISerialUnitService
{
    Task<ServiceResult> GetAvailableUnitsAsync(int articleId, int companyId, int userId, bool isDeveloper);
    Task<ServiceResult> GetPendingAssignmentUnitsAsync(int assignmentId, int companyId, int userId, bool isDeveloper);
    Task<ServiceResult> AddUnitAsync(int articleId, RegisterUnitRequest input, int companyId, int userId, bool isDeveloper, string username);
    Task<ServiceResult> AddUnitsBatchAsync(int articleId, RegisterUnitBatchRequest input, int companyId, int userId, bool isDeveloper, int? employeeId, string username);
    Task<ServiceResult> DecommissionUnitAsync(int articleId, DecommissionUnitRequest input, int companyId, int userId, bool isDeveloper, int? employeeId, string username);
}