using FdeTracker.Api.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FdeTracker.Api.Filters;

// Authorization filters run before model binding, so a refused person gets 403 even if their body is invalid.

/// <summary>The signed-in user must have been approved.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireApprovedAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.GetUser();
        if (!user.IsApproved)
            context.Result = new ObjectResult(new { error = "Your access is waiting for approval", access = user.AccessStatus }) { StatusCode = 403 };
    }
}

/// <summary>The signed-in user must have one of the given roles.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireRoleAttribute(params string[] roles) : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!roles.Contains(context.HttpContext.GetUser().Role))
            context.Result = new ObjectResult(new { error = "Not allowed" }) { StatusCode = 403 };
    }
}
