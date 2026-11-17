namespace Api.Middleware;

using Application.PaymentManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

/// <summary>
/// Maps <see cref="TontineOccupeeException"/> (the per-tontine lock was not obtained within its bounded wait,
/// nothing was written) to 503 with a retry message, for every controller, the Orange Money webhook
/// included (the operator retries on 5xx).
/// </summary>
public sealed class TontineOccupeeExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not TontineOccupeeException)
            return;
        context.HttpContext.Response.Headers.RetryAfter = "2";
        context.Result = new ObjectResult(new { error = TontineOccupeeException.MessageUtilisateur })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
        };
        context.ExceptionHandled = true;
    }
}
