using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LefteriaFc.Api.PublicContent;

/// <summary>Admin news never has an implicit/global organization owner.</summary>
public sealed class AdminNewsOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken token)
    {
        if (context.Description.RelativePath?.Split('?')[0].Trim('/') != "api/admin/website/news" ||
            operation.Parameters is null) return Task.CompletedTask;

        foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>()
            .Where(parameter => parameter.Name == "organization_id"))
        {
            parameter.Required = true;
            parameter.Description = "Exact current authorized FC or Academy owner; never a global feed.";
            parameter.Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [JsonValue.Create("lefteria_fc"), JsonValue.Create("lefteria_fc_academy")]
            };
        }
        return Task.CompletedTask;
    }
}
