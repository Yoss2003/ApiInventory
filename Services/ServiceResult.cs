namespace InventoryAPI.Services;

public enum ServiceStatus
{
    Ok,
    Created,
    NoContent,
    BadRequest,
    Forbid,
    NotFound,
    Conflict,
    ServiceUnavailable,
    BadGateway,
    GatewayTimeout
}

public sealed record ServiceResult(ServiceStatus Status, object? Payload = null, string? Error = null)
{
    public static ServiceResult Ok(object? payload = null) => new(ServiceStatus.Ok, payload);
    public static ServiceResult Created(object? payload = null) => new(ServiceStatus.Created, payload);
    public static ServiceResult NoContent() => new(ServiceStatus.NoContent);
    public static ServiceResult BadRequest(string error) => new(ServiceStatus.BadRequest, Error: error);
    public static ServiceResult Forbid() => new(ServiceStatus.Forbid);
    public static ServiceResult NotFound(string? error = null) => new(ServiceStatus.NotFound, Error: error);
    public static ServiceResult Conflict(string error, object? payload = null) => new(ServiceStatus.Conflict, payload, error);
    public static ServiceResult ServiceUnavailable(string error) => new(ServiceStatus.ServiceUnavailable, Error: error);
    public static ServiceResult BadGateway(string error) => new(ServiceStatus.BadGateway, Error: error);
    public static ServiceResult GatewayTimeout(string error) => new(ServiceStatus.GatewayTimeout, Error: error);
}