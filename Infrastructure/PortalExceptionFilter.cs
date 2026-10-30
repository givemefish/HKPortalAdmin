using HKPortalAdmin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;

namespace HKPortalAdmin.Infrastructure;

public sealed class PortalExceptionFilter(IStringLocalizer<SharedResource> text,
    ILogger<PortalExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var known = context.Exception as PortalException;
        if (known is null) logger.LogError(context.Exception, "Portal request failed: {Path}", context.HttpContext.Request.Path);
        context.Result = new ObjectResult(new { error = text[known?.Key ?? "UnexpectedError"].Value })
        { StatusCode = known?.Status ?? 500 };
        context.ExceptionHandled = true;
    }
}
