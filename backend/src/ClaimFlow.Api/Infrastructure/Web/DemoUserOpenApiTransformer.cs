using ClaimFlow.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ClaimFlow.Api.Infrastructure.Web;

/// <summary>
/// Adds the X-User-Id header to the OpenAPI document as a security scheme, so Swagger UI shows an
/// "Authorize" button where you paste a demo user's ID once and it is sent with every request.
/// </summary>
public class DemoUserOpenApiTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info.Title = "ClaimFlow API";
        document.Info.Description =
            "Demo authentication: send a seeded user's ID in the X-User-Id header (see GET /api/users). NOT secure.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[DemoUserAuthenticationHandler.SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = DemoUserAuthenticationHandler.HeaderName,
            Description = "A user ID from GET /api/users, e.g. 11111111-0000-0000-0000-000000000001 (Alex Tan, claimant)."
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(DemoUserAuthenticationHandler.SchemeName, document)] = []
        });

        return Task.CompletedTask;
    }
}
