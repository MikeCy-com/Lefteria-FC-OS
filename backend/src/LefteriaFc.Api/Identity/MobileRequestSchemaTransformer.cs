using System.Text.Json.Nodes;
using LefteriaFc.Application.MobileAuth;
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
        if (context.JsonTypeInfo.Type == typeof(MobileProfilePreferenceBody) && schema.Properties is not null)
        {
            schema.Properties["favorite_team"] = new OpenApiSchema
            {
                AnyOf = [
                    new OpenApiSchema { Type = JsonSchemaType.String,
                        Enum = [JsonValue.Create("first_team"), JsonValue.Create("academy"), JsonValue.Create("both")] },
                    new OpenApiSchema { Type = JsonSchemaType.Null }
                ]
            };
        }
        if (context.JsonTypeInfo.Type == typeof(MobileProfileUpdateBody) && schema.Properties is not null)
        {
            schema.Description = "Updates accepted non-null own-profile fields. Null fields are ignored; at least one accepted field is required. Unknown fields are ignored.";
            schema.Properties["name"] = NullableString(MobileProfileLimits.Name, 1);
            schema.Properties["email"] = NullableString(MobileProfileLimits.Email,
                description: "A plain email address without a display name; empty string clears it.");
            schema.Properties["first_name"] = NullableString(MobileProfileLimits.NamePart);
            schema.Properties["last_name"] = NullableString(MobileProfileLimits.NamePart);
            schema.Properties["birthday"] = new OpenApiSchema
            {
                AnyOf = [new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" },
                    new OpenApiSchema { Type = JsonSchemaType.Null }]
            };
            schema.Properties["favourite_player_id"] = NullableString(MobileProfileLimits.FavouritePlayerId,
                description: "Opaque legacy player preference; empty string clears it. This value grants no roster access.");
            schema.Properties["notification_prefs"] = new OpenApiSchema
            {
                Description = "Replaces the whole preference object; up to 64 keys, each 1..64 characters without controls, with boolean values. Null is ignored; an empty object clears preferences.",
                AnyOf = [new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object, MaxProperties = MobileProfileLimits.NotificationKeys,
                        AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.Boolean }
                    },
                    new OpenApiSchema { Type = JsonSchemaType.Null }]
            };
        }
        return Task.CompletedTask;
    }

    private static OpenApiSchema NullableString(int maximum, int minimum = 0, string? description = null) => new()
    {
        Description = description,
        AnyOf = [new OpenApiSchema { Type = JsonSchemaType.String, MinLength = minimum, MaxLength = maximum },
            new OpenApiSchema { Type = JsonSchemaType.Null }]
    };
}
