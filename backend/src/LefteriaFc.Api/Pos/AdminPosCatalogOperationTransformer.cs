using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LefteriaFc.Api.Pos;

public sealed class AdminPosCatalogOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken token)
    {
        var route = context.Description.RelativePath?.Split('?')[0].Trim('/');
        if (route is not ("api/admin/pos/catalog" or "api/admin/pos/categories" or
            "api/admin/pos/categories/{id}") || operation.Parameters is null)
            return Task.CompletedTask;

        foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>()
            .Where(item => item.Name == "organization_id"))
        {
            parameter.Required = true;
            parameter.Description = "Exact current admin-assigned POS catalog owner; no global or inferred owner.";
            parameter.Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [JsonValue.Create("lefteria_fc"), JsonValue.Create("lefteria_fc_academy")]
            };
        }
        return Task.CompletedTask;
    }
}
