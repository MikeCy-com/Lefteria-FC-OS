using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LefteriaFc.Api.PublicContent;

/// <summary>Each private draft read/write has one explicit authorized owner.</summary>
public sealed class AdminNewsDraftOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation,
        OpenApiOperationTransformerContext context, CancellationToken token)
    {
        var route = context.Description.RelativePath?.Split('?')[0].Trim('/') ?? "";
        if (route is not ("api/admin/website/news-drafts" or
            "api/admin/website/news-drafts/{id}") || operation.Parameters is null)
            return Task.CompletedTask;
        foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>()
            .Where(parameter => parameter.Name == "organization_id"))
        {
            parameter.Required = true;
            parameter.Description = "Exact granted Club or Academy owner. Never a global draft feed.";
            parameter.Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [JsonValue.Create("lefteria_fc"), JsonValue.Create("lefteria_fc_academy")]
            };
        }
        return Task.CompletedTask;
    }
}
