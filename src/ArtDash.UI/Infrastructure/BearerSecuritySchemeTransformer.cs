using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ArtDash.UI.Infrastructure;

/// <summary>Declares the JWT bearer scheme so Scalar can send an access token (same as artstore-fs).</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "ArtDash API",
            Version = "v1",
            Description = "API multiempresa (white-label) de pedidos, entregas e pagamentos. " +
                          "Informe a empresa pelo host ou pelo cabeçalho X-Tenant (slug)."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Access token retornado por POST /api/auth/login."
        };

        return Task.CompletedTask;
    }
}
