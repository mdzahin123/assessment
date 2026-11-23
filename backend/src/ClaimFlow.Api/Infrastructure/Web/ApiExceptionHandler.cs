using ClaimFlow.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Api.Infrastructure.Web;

/// <summary>
/// Turns "known" exceptions into the right HTTP status code with a standard ProblemDetails JSON body.
/// This is the only place that maps business-rule failures to HTTP, so controllers stay one-liners
/// and the rules themselves live only in the domain.
/// Anything not listed here is a real bug and falls through to a generic 500.
/// </summary>
public class ApiExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        // Order matters: the more specific exception types must come before their base class (DomainException).
        (int Status, string Title, string Detail)? mapped = exception switch
        {
            NotFoundException e => (StatusCodes.Status404NotFound, "Not found", e.Message),
            InvalidStatusTransitionException e => (StatusCodes.Status409Conflict, "Action not allowed in the claim's current status", e.Message),
            ForbiddenActionException e => (StatusCodes.Status403Forbidden, "Not allowed", e.Message),
            DomainException e => (StatusCodes.Status400BadRequest, "Business rule violated", e.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrent update",
                "This claim was changed by someone else at the same time. Reload it and try again."),
            _ => null
        };

        if (mapped is not { } m)
            return false;

        httpContext.Response.StatusCode = m.Status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = m.Status, Title = m.Title, Detail = m.Detail }
        });
    }
}
