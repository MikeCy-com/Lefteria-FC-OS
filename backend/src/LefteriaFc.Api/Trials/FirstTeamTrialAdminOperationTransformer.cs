using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LefteriaFc.Api.Trials;

/// <summary>
/// The private route requires an explicit, fixed FC selector. OpenAPI must
/// never suggest that Academy, an omitted scope or a global query is allowed.
/// </summary>
public sealed class FirstTeamTrialAdminOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken token)
    {
        if (context.Description.RelativePath?.Split('?')[0].Trim('/') != "api/admin/trials/settings" ||
            operation.Parameters is null) return Task.CompletedTask;

        foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>()
            .Where(parameter => parameter.Name == "organization_id"))
        {
            parameter.Required = true;
            parameter.Description = "Exact first-team owner only; no Academy or global selector.";
            parameter.Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [JsonValue.Create("lefteria_fc")]
            };
        }
        return Task.CompletedTask;
    }
}
