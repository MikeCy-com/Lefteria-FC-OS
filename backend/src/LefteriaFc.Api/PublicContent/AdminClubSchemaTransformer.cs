using LefteriaFc.Application.PublicContent;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace LefteriaFc.Api.PublicContent;

public sealed class AdminClubSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken token)
    {
        if (context.JsonTypeInfo.Type == typeof(AdminClubEditable) && schema.Properties is not null)
        {
            schema.Description = "Complete safe public club fields only. Null clears optional fields. Unknown keys and duplicate JSON keys are rejected.";
            schema.AdditionalPropertiesAllowed = false;
            foreach (var (field, maximum) in new[] { ("name", 200), ("greek_name", 200), ("description", 20000), ("motto", 1000),
                ("history", 100000), ("logo_url", 2048), ("stadium_image_url", 2048), ("city", 200), ("country", 100),
                ("address", 1000), ("email", 254), ("phone", 40), ("website", 2048) })
                schema.Properties[field] = new OpenApiSchema
                { Type = field == "name" ? JsonSchemaType.String : JsonSchemaType.String | JsonSchemaType.Null, MaxLength = maximum, MinLength = field == "name" ? 1 : 0 };
            // Nullable numeric JSON metadata otherwise inherits the web JSON
            // string-number convention, unlike our strict command parser.
            schema.Properties["founded"] = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null, Format = "int32" };
            schema.Properties["social_links"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object, MaxProperties = 5,
                Description = "Only facebook, instagram, twitter, youtube and tiktok; values are nonempty safe HTTPS URLs.",
                AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 2048 }
            };
        }
        return Task.CompletedTask;
    }
}

public sealed class AdminClubOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken token)
    {
        // Minimal API descriptions can retain the route group's leading slash.
        // Match the exact normalized route rather than missing the required scope
        // contract when the description is "/api/admin/club".
        if (context.Description.RelativePath?.Split('?')[0].Trim('/') == "api/admin/club" && operation.Parameters is not null)
            foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>().Where(value => value.Name == "organization_id"))
            {
                parameter.Required = true;
                parameter.Description = "One current authorized owner; no implicit or global organization.";
                parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.String,
                    Enum = [JsonValue.Create("lefteria_fc"), JsonValue.Create("lefteria_fc_academy")] };
            }
        return Task.CompletedTask;
    }
}
