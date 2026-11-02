using LefteriaFc.Application.PublicContent;

namespace LefteriaFc.Api.PublicContent;

public static class PublicSectionsEndpoints
{
    public static IEndpointRouteBuilder MapPublicSections(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/public/sections", async (string? organization_id, IPublicSectionsRepository repository, HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var organization = organization_id ?? "lefteria_fc";
            if (!PublicContentService.IsOrganization(organization))
                return Results.Json(new PublicContentError("Μη έγκυρος οργανισμός.", "invalid_organization"), statusCode: 400);
            try
            {
                var result = await repository.ReadAsync(organization, token);
                return result is null ? Unavailable() : Results.Ok(result);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { return Unavailable(); }
        }).AllowAnonymous().WithTags("Public website").WithName("GetPublicSections")
            .Produces<PublicSectionsResult>().Produces<PublicContentError>(400).Produces<PublicContentError>(503);
        return endpoints;
    }
    private static IResult Unavailable() => Results.Json(new PublicContentError("Το περιεχόμενο δεν είναι προσωρινά διαθέσιμο.", "public_sections_unavailable"), statusCode: 503);
}
