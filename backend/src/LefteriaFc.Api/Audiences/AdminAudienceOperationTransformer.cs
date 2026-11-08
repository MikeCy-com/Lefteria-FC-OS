using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LefteriaFc.Api.Audiences;

/// <summary>The admin audience owner is always an exact FC or Academy id;
/// an omitted owner must never produce a union across children/teams.</summary>
public sealed class AdminAudienceOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation,
        OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.RelativePath?.Split('?')[0].Trim('/') is not
            ("api/admin/football/audiences" or "api/admin/football/groups" or
              "api/admin/football/players" or "api/admin/football/assignments" or "api/admin/football/coaches" or "api/admin/football/coach-assignments" or "api/admin/communications/announcement-drafts") ||
            operation.Parameters is null)
            return Task.CompletedTask;
        foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>()
            .Where(item => item.Name == "organization_id"))
        {
            parameter.Required = true;
            parameter.Description = "Exact current assigned football or Academy owner; never all or inferred.";
            parameter.Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [JsonValue.Create("lefteria_fc"),JsonValue.Create("lefteria_fc_academy")]
            };
        }
        return Task.CompletedTask;
    }
}
