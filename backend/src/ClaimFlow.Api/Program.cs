using System.Text.Json.Serialization;
using ClaimFlow.Api.Features.Claims;
using ClaimFlow.Api.Infrastructure.Auth;
using ClaimFlow.Api.Infrastructure.Persistence;
using ClaimFlow.Api.Infrastructure.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- API basics -------------------------------------------------------------
// Enums travel as readable strings ("InReview"), matching the database and the Angular types.
// Configured in BOTH places: controllers use the MVC JSON options to write responses, but the OpenAPI
// generator reads the "HTTP" JSON options. Setting only one made the contract say "integer" while the
// API actually sent strings (caught during Stage 2 verification).
static void UseStringEnums(System.Text.Json.JsonSerializerOptions o) => o.Converters.Add(new JsonStringEnumConverter());
builder.Services.AddControllers().AddJsonOptions(o => UseStringEnums(o.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(o => UseStringEnums(o.SerializerOptions));
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<DemoUserOpenApiTransformer>());

// --- Errors: every error response is a standard ProblemDetails JSON body ------
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// --- Data -------------------------------------------------------------------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("ClaimFlow")));
builder.Services.AddSingleton(TimeProvider.System); // injectable clock (tests can replace it)

// --- Features ---------------------------------------------------------------
builder.Services.AddScoped<ClaimService>();

// --- Demo authentication (X-User-Id header) — NOT SECURE, see DemoUserAuthenticationHandler
builder.Services.AddAuthentication(DemoUserAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DemoUserAuthenticationHandler>(DemoUserAuthenticationHandler.SchemeName, null);
// Locked by default: any endpoint without [AllowAnonymous] requires a known user,
// so forgetting an [Authorize] attribute can never accidentally expose an endpoint.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();

app.UseExceptionHandler();  // known exceptions -> 400/403/404/409, anything else -> 500
app.UseStatusCodePages();   // gives empty 401/403 responses a ProblemDetails body too

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();  // contract: /openapi/v1.json
    app.UseSwaggerUI(o =>               // clickable UI: /swagger
    {
        o.SwaggerEndpoint("/openapi/v1.json", "ClaimFlow API v1");
        o.EnablePersistAuthorization();  // remember the X-User-Id between page reloads
    });

    // Local-dev convenience: apply pending migrations and load demo data on startup.
    // In production, migrations would be a separate, deliberate deployment step instead.
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DevDataSeeder.SeedAsync(db);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Lets the integration tests start this app in-memory (WebApplicationFactory<Program>).
public partial class Program;
