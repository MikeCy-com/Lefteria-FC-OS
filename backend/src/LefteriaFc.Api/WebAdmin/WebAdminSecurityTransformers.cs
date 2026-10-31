using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LefteriaFc.Api.WebAdmin;

public sealed class WebAdminSecurityDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken token)
    {
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[WebAdminServices.Scheme] = new OpenApiSecurityScheme
        { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Cookie, Name = WebAdminServices.CookieName };
        return Task.CompletedTask;
    }
}
public sealed class WebAdminSecurityOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken token)
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any(value => value.Policy == WebAdminServices.Policy))
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(WebAdminServices.Scheme, context.Document)] = [] });
        }
        return Task.CompletedTask;
    }
}
