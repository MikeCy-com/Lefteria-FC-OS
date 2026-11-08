using LefteriaFc.Api.Identity;
using LefteriaFc.Application.MobileChat;

namespace LefteriaFc.Api.MobileChat;

public static class MobileChatDirectoryEndpoints
{
    public static IEndpointRouteBuilder MapMobileChatGroupDirectory(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/mobile/chat/groups",
            (HttpContext context,MobileChatDirectoryService service,CancellationToken token)=>
                MobileIdentityEndpoints.Execute(()=>
                    service.ReadAsync(MobilePrincipal.Read(context.User),token),context))
            .RequireAuthorization(IdentityServices.Policy)
            .WithName("GetMobileChatGroups")
            .WithTags("Mobile chat")
            .WithDescription("Scoped group directory only. Returns current canonical FC/Academy group IDs and names, never private participants or messages. Fan role denied; no chat send/delete endpoints are enabled by this read.")
            .Produces<MobileChatDirectoryDto>()
            .WithMobileErrors();
        return app;
    }
}
