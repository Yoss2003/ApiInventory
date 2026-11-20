using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAPI.Security;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Error no controlado en {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        var (statusCode, title, detail) = exception switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, "Solicitud inválida",
                "La solicitud contiene datos inválidos."),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Recurso no encontrado",
                "No se encontró el recurso solicitado."),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Acceso denegado",
                "No tienes permiso para realizar esta operación."),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Conflicto de operación",
                "La operación no puede completarse en el estado actual."),
            _ => (StatusCodes.Status500InternalServerError, "Error interno del servidor",
                "Ocurrió un error inesperado al procesar la solicitud.")
        };

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}