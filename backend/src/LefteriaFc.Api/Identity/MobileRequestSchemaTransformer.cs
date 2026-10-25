using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LefteriaFc.Api.Identity;

public sealed class MobileRequestSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(OtpVerificationBody) && schema.Properties is not null)
        {
            // JsonElement preserves missing vs null at runtime; publish its actual
            // restricted wire contract instead of an unconstrained JSON value.
            schema.Properties["remember_days"] = new OpenApiSchema
            {
                AnyOf = [
                    new OpenApiSchema { Type = JsonSchemaType.Integer,
                        Enum = [JsonValue.Create(30), JsonValue.Create(60), JsonValue.Create(90)] },
                    new OpenApiSchema { Type = JsonSchemaType.Null }
                ]
            };
        }
        return Task.CompletedTask;
    }
}
