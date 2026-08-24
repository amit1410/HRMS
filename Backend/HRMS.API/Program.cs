using HRMS.API.Common;
using HRMS.API.Filters;
using HRMS.API.Middleware;
using HRMS.API.Security;
using HRMS.Application;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Serilog;
using System.Text.Json.Serialization;

// Bootstrap logger: captures failures that occur before the host (and full Serilog config) is built.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Structured logging via Serilog, configured from appsettings ("Serilog" section).
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // Application + Infrastructure services (DbContext with configurable provider, password hasher,
    // token service, auth service, validators).
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // Tenant identity is resolved per-request from JWT claims (server-side, never from client input).
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

    // JWT bearer authentication plus one authorization policy per permission.
    builder.Services.AddJwtAuthentication(builder.Configuration);
    builder.Services.AddHrmsRateLimiting(builder.Configuration);

    builder.Services.AddControllers(options =>
        {
            // Validates every action argument that has a FluentValidation validator.
            options.Filters.Add<ValidationFilter>();
        })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

    // Model-binding failures (malformed JSON, wrong types) use the same envelope as validation failures,
    // including the same camelCase field names — a client should not have to know which of the two layers
    // rejected its request in order to find the field that was wrong.
    builder.Services.Configure<ApiBehaviorOptions>(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .SelectMany(entry => entry.Value!.Errors.Select(error =>
                    new ValidationError(
                        FieldNames.ToCamelCase(entry.Key),
                        "The value supplied for this field is not valid.")))
                .ToList();

            return new BadRequestObjectResult(
                ApiResponse.Fail("The request could not be read.", errors));
        };
    });

    // CORS for the client: configured exact origins plus every workspace address under a configured
    // whitelabel template, so onboarding an organization is not a CORS edit.
    builder.Services.AddHrmsCors(builder.Configuration);

    // Which proxy this API believes about the client address, the scheme and — now that the host chooses the
    // database — the host.
    builder.Services.AddHrmsForwardedHeaders(builder.Configuration);

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new()
        {
            Title = "HRMS API",
            Version = "v1",
            Description =
                "Multi-tenant HRMS API. JWT authentication with permission-based authorization, and the " +
                "employee, department and designation modules. Consumed by the React client in Frontend/HRMS.Web."
        });

        // "Authorize" button in Swagger UI: paste the accessToken returned by /api/auth/login.
        const string securitySchemeId = "Bearer";
        options.AddSecurityDefinition(securitySchemeId, new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter the access token from /api/auth/login. Swagger adds the 'Bearer ' prefix."
        });

        // Swashbuckle 10 / Microsoft.OpenApi 2.x builds the requirement from the host document, which is
        // what resolves the reference back to the scheme defined above.
        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(securitySchemeId, document)] = []
        });
    });

    var app = builder.Build();

    // Apply migrations (SQL Server) or create schema (SQLite dev fallback), then seed reference data + demo tenants.
    await DatabaseInitializer.InitializeAsync(app.Services);

    // Centralized exception handling must sit at the top of the pipeline.
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // Forwarded headers before anything that reads the scheme, the host or the client address — which now
    // means before request logging, before HTTPS redirection, and above all before host resolution picks a
    // database. Only the exception handler sits above it, because that has to wrap everything.
    app.UseForwardedHeaders();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseCors(CorsPolicies.Client);

    if (!app.Environment.IsDevelopment())
    {
        // Enforce HTTPS outside development (dev may run plain HTTP for tooling/tests).
        //
        // After UseCors, not before: a cross-origin request that arrives over http gets redirected, and a 307
        // carrying no Access-Control-Allow-Origin header is a CORS failure in the browser rather than a
        // redirect it will follow. The client sees "blocked by CORS policy" and the real cause — a scheme the
        // proxy did not forward — never appears anywhere.
        app.UseHttpsRedirection();
    }

    // Resolves the host to an organization before the rate limiter partitions, before authentication reads
    // claims, and before any controller can open a tenant database.
    app.UseTenantShardResolution();

    app.UseRateLimiter();

    // Authentication must run before authorization so the tenant context has verified claims to read.
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // Liveness probe: deliberately anonymous, and explicitly so because the fallback authorization
    // policy would otherwise close it along with every other endpoint that declares nothing.
    app.MapGet("/health", () => Results.Ok(new { status = "Healthy", utc = DateTime.UtcNow }))
        .AllowAnonymous();

    Log.Information("HRMS API starting up.");
    app.Run();
    return 0;
}
// A deliberate host abort is not a crash: tooling that only wants the built host (EF Core design-time
// commands, WebApplicationFactory in tests) stops the app this way, so it must not be logged as fatal
// or swallowed — rethrowing lets the caller capture the host it asked for.
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "HRMS API terminated unexpectedly during startup.");
    return 1; // non-zero so supervisors/CI see the failed start rather than a clean exit
}
finally
{
    Log.CloseAndFlush();
}

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program { }
