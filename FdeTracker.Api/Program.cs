using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Middleware;
using FdeTracker.Api.Repositories;
using FdeTracker.Api.Services;
using FdeTracker.Api.Services.Lab;
using FdeTracker.Api.Services.Llm;
using FdeTracker.Api.Services.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Npgsql;
using Scalar.AspNetCore;

Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true; // column display_name -> property DisplayName

var builder = WebApplication.CreateBuilder(args);
var options = AppOptions.From(builder.Configuration);

builder.WebHost.ConfigureKestrel(k => { k.AddServerHeader = false; k.Limits.MaxRequestBodySize = SecurityMiddleware.MaxBody; });
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port) builder.WebHost.UseUrls($"http://0.0.0.0:{port}"); // Render tells us the port

// ---- data and model layer
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(NpgsqlDataSource.Create(ConnectionString.Normalize(options.DatabaseUrl, options.DbCommandTimeoutSeconds)));
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<CatalogRepository>();
builder.Services.AddSingleton<AuditRepository>();
builder.Services.AddSingleton<ContentRepository>();
builder.Services.AddSingleton<BatchRepository>();
builder.Services.AddSingleton<ReviewRepository>();
builder.Services.AddSingleton<ProgramRepository>();
builder.Services.AddSingleton<LlmCallRepository>();
builder.Services.AddSingleton<SubmissionRepository>();
builder.Services.AddSingleton<TaskRepository>();
builder.Services.AddSingleton<LabRunRepository>();
builder.Services.AddSingleton<ReportRepository>();
builder.Services.AddSingleton<LearningProgramRepository>();
builder.Services.AddSingleton<FeedPostRepository>();
builder.Services.AddSingleton<TaskTools>();
builder.Services.AddSingleton<PromptLibrary>();
builder.Services.AddSingleton<AnalysisPipeline>();
builder.Services.AddSingleton<LlmCallLedger>();
builder.Services.AddSingleton<LabData>();
// One HttpClient for every model call. The client the app uses is the server's configured one, except for a request
// that carries a person's own Gemini key (see OwnKeyMiddleware): that one talks to Gemini with that key.
builder.Services.AddSingleton(new LlmHttp(new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = Timeout.InfiniteTimeSpan }));
builder.Services.AddSingleton<ILlmClient>(sp =>
{
    var opts = sp.GetRequiredService<AppOptions>();
    var http = sp.GetRequiredService<LlmHttp>().Client;
    return new SwitchingLlmClient(LlmClientFactory.Create(opts.LlmProvider, http, opts), http, opts);
});
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<ContentService>();
builder.Services.AddSingleton<BatchService>();
builder.Services.AddSingleton<ReviewService>();
builder.Services.AddSingleton<ProgramService>();
builder.Services.AddSingleton<AssistantService>();
builder.Services.AddSingleton<SubmissionService>();
builder.Services.AddSingleton<TaskAssistantService>();
builder.Services.AddSingleton<SubmissionReviewService>();
builder.Services.AddSingleton<LabService>();
builder.Services.AddSingleton<LabRunService>();
builder.Services.AddSingleton<ReportService>();
builder.Services.AddSingleton<LearningProgramService>();
builder.Services.AddSingleton<ProgramStatusService>();
builder.Services.AddSingleton<FeedPostService>();

// ---- controllers and JSON (snake_case on the wire, unknown fields refused)
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        o.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    })
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
    {
        var issues = ctx.ModelState
            .Where(kv => kv.Value?.Errors.Count > 0)
            .SelectMany(kv => kv.Value!.Errors.Select(e => new
            {
                path = JsonNamingPolicy.SnakeCaseLower.ConvertName(kv.Key.Replace("$.", "")),
                message = e.Exception is null && e.ErrorMessage.Length > 0 ? e.ErrorMessage : "Invalid value",
            }));
        return new ObjectResult(new { error = "Invalid input", issues }) { StatusCode = 400 };
    });

// ---- API documentation page (Development only, see below): lets you try every endpoint with a pasted token
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "FDE Tracker API";
    doc.Components ??= new OpenApiComponents();
    doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    doc.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
    return Task.CompletedTask;
}));

// ---- security: sign-in token, CORS, rate limits, proxy headers
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<AppOptions>(SupabaseJwt.Configure);
builder.Services.AddAuthorization();
builder.Services.AddCors(c => c.AddDefaultPolicy(p => p
    .WithOrigins(options.CorsOrigins)
    .WithMethods("GET", "POST", "PATCH", "PUT", "DELETE")
    .AllowAnyHeader()
    .WithExposedHeaders("Content-Disposition") // the Excel downloads carry their file name
    .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));
builder.Services.AddRateLimiter(r =>
{
    r.RejectionStatusCode = 429;
    r.OnRejected = (ctx, _) => new ValueTask(ErrorBody.Write(ctx.HttpContext, 429, "Too many requests, slow down"));
    string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    r.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartition.GetFixedWindowLimiter("ip:" + Ip(c),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = options.RateLimitPerMinute, Window = TimeSpan.FromMinutes(1) })),
        PartitionedRateLimiter.Create<HttpContext, string>(c => HttpMethods.IsGet(c.Request.Method)
            ? RateLimitPartition.GetNoLimiter("reads")
            : RateLimitPartition.GetFixedWindowLimiter("writes:" + (c.User.FindFirst("sub")?.Value ?? Ip(c)),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = options.WriteLimitPerMinute, Window = TimeSpan.FromMinutes(1) })));
});
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear(); // we run behind Render's proxy
    o.KnownProxies.Clear();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await Migrator.RunAsync(scope.ServiceProvider.GetRequiredService<Db>(), app.Logger);

app.UseForwardedHeaders();
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<SecurityMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<CurrentUserMiddleware>();
app.UseMiddleware<OwnKeyMiddleware>(); // after sign-in: only a signed-in person's request is ever given a key scope
app.MapControllers();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(o => o.AddPreferredSecuritySchemes("Bearer")); // UI at /scalar
    app.MapGet("/", () => Results.Redirect("/scalar"));
}
app.MapFallback(ctx => ErrorBody.Write(ctx, 404, "Not found"));

await app.RunAsync();

public partial class Program; // lets the tests start the app
