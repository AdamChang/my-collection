using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MyCollection.Application.Media;
using MyCollection.Application.Transfer;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Api;

/// <summary>
/// 唯一的錯誤轉換點：所有層都不寫 try-catch，例外一律冒泡到這裡轉成 RFC 9457 ProblemDetails。
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, detail, errors) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request failed with {Status}: {Title}", status, title);
        }

        httpContext.Response.StatusCode = status;

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
        };

        if (errors is not null)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    private static (int Status, string Title, string? Detail, IDictionary<string, string[]>? Errors) Map(Exception exception) =>
        exception switch
        {
            ValidationException v => (
                StatusCodes.Status400BadRequest,
                "One or more validation errors occurred.",
                null,
                (IDictionary<string, string[]>)v.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),

            InvalidImageException i => (StatusCodes.Status400BadRequest, "Invalid image.", i.Message, null),

            InvalidArchiveException a => (StatusCodes.Status400BadRequest, "Invalid archive.", a.Message, null),

            NotFoundException n => (StatusCodes.Status404NotFound, "Resource not found.", n.Message, null),

            ForbiddenException f => (StatusCodes.Status403Forbidden, "Forbidden.", f.Message, null),

            ConflictException c => (StatusCodes.Status409Conflict, "Conflict.", c.Message, null),

            UnreadableCredentialException u => (
                StatusCodes.Status409Conflict,
                "Stored credential could not be decrypted.",
                u.Message,
                null),

            ProviderException p => (
                StatusCodes.Status502BadGateway,
                $"Provider '{p.ProviderKey}' failed.",
                p.Message,
                null),

            // 未知例外絕不外洩內部訊息或堆疊
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null, null)
        };
}
