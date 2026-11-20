using FdeTracker.Api.Middleware;
using FdeTracker.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

/// <summary>Base for every signed-in endpoint: needs a valid token; exposes the loaded app user.</summary>
[ApiController, Authorize, Route("api")]
public abstract class ApiControllerBase : ControllerBase
{
    protected AppUser CurrentUser => HttpContext.GetUser();
}
