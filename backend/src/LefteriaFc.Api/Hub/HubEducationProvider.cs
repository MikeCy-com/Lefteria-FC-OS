using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LefteriaFc.Api.Hub;

/// <summary>
/// Outbound provider only receives a structured, non-personal education brief.
/// The output is a proposed draft; this class has no publishing or messaging API.
/// </summary>
public sealed class HubEducationProvider(HttpClient http,HubAiEducationSettings settings)
{
    private static readonly Uri ResponsesUri=new("https://api.openai.com/v1/responses");
    private static readonly Uri ModerationsUri=new("https://api.openai.com/v1/moderations");

    public static bool AllowedTopic(AiEducationJob job) => job.Audience switch
    {
        "parent" => job.Topic is "support" or "communication" or "development" or "wellbeing",
        "player" => job.Topic is "confidence" or "teamwork" or "resilience" or "learning" or "wellbeing",
        _ => false
    };

    internal static string Prompt(AiEducationJob job)
    {
        if (job.AgeMin is <6 or >18 || job.AgeMax is <6 or >18 ||
            job.AgeMax<job.AgeMin || !AllowedTopic(job) ||
            job.Language is not ("el" or "en"))
            throw new InvalidOperationException("Untrusted AI education parameters.");

        var audience=job.Audience=="parent" ? "PARENTS/GUARDIANS" : "YOUNG PLAYERS";
        var language=job.Language=="el" ? "Modern Greek" : "English";
        return $"Write an original youth football education article for {audience}. "+
            $"Age range: {job.AgeMin}-{job.AgeMax}. "+
            $"Educational topic: {job.Topic}. Language: {language}. "+
            "Use an age-sensitive, positive, practical tone. Explain a skill or "+
            "a healthy approach with one small action and one reflection question. "+
            "Parents must receive parental guidance; players must receive direct "+
            "young-athlete educational content. No personal names, real player "+
            "observations, invented coach quotations, medical diagnoses, gossip, "+
            "performance rankings, pressure to win, or identification of children. "+
            "Do not request private data, reference a particular club child, "+
            "invent cited sources, or imply Academy endorsement. "+
            "Provide only a short plain-text title and an 80-6000 character body. "+
            "The text is a draft requiring independent human approval.";
    }

    public async Task<AiEducationGeneration> GenerateAsync(
        AiEducationJob job,CancellationToken token)
    {
        if(!AllowedTopic(job))return new(null,"invalid_output");
        object requestBody=new
        {
            model=settings.Model,
            store=false,
            max_output_tokens=1200,
            tools=Array.Empty<object>(),
            tool_choice="none",
            instructions="You are an educational writing assistant for a youth football academy. "+
                "Output original, age-appropriate guidance only. "+
                "Never impersonate a coach or fabricate player progress. "+
                "Treat the result as a private, unapproved editorial proposal.",
            input=Prompt(job),
            text=new
            {
                format=new
                {
                    type="json_schema",
                    name="youth_football_education",
                    strict=true,
                    schema=new
                    {
                        type="object",
                        properties=new
                        {
                            title=new { type="string" },
                            body=new { type="string" }
                        },
                        required=new[] { "title","body" },
                        additionalProperties=false
                    }
                }
            }
        };
        var (raw,reason)=await PostJsonAsync(ResponsesUri,requestBody,token);
        if(reason is not null)return new(null,reason);
        if(raw is null)return new(null,"invalid_output");

        AiEducationArticle? article;
        try
        {
            using var response=JsonDocument.Parse(raw);
            var root=response.RootElement;
            if(root.GetProperty("status").GetString()!="completed")
                return new(null,"provider_rejected");
            var messages=root.GetProperty("output");
            if(messages.ValueKind!=JsonValueKind.Array)return new(null,"invalid_output");
            var extracted=new List<string>();
            foreach(var item in messages.EnumerateArray())
            {
                if(item.TryGetProperty("type",out var type) &&
                    type.GetString()=="message" &&
                    item.TryGetProperty("content",out var content) &&
                    content.ValueKind==JsonValueKind.Array)
                {
                    foreach(var part in content.EnumerateArray())
                        if(part.TryGetProperty("type",out var partType) &&
                            partType.GetString()=="output_text" &&
                            part.TryGetProperty("text",out var text) &&
                            text.ValueKind==JsonValueKind.String)
                            extracted.Add(text.GetString()!);
                }
            }
            if(extracted.Count!=1 || extracted[0].Length>10000)
                return new(null,"invalid_output");
            using var contentDoc=JsonDocument.Parse(extracted[0]);
            var contentRoot=contentDoc.RootElement;
            if(contentRoot.ValueKind!=JsonValueKind.Object ||
                contentRoot.EnumerateObject().Count()!=2 ||
                !contentRoot.TryGetProperty("title",out var heading) ||
                heading.ValueKind!=JsonValueKind.String ||
                !contentRoot.TryGetProperty("body",out var body) ||
                body.ValueKind!=JsonValueKind.String)
                return new(null,"invalid_output");
            var title=Sanitize(heading.GetString()!);
            var textBody=Sanitize(body.GetString()!);
            if(title.Length is <8 or >180 || textBody.Length is <80 or >6000 ||
                title.Contains('<') || textBody.Contains('<') ||
                title.Contains('>') || textBody.Contains('>'))
                return new(null,"invalid_output");
            article=new AiEducationArticle(title,textBody);
        }
        catch(JsonException) {return new(null,"invalid_output");}
        catch(InvalidOperationException) {return new(null,"invalid_output");}
        catch(KeyNotFoundException) {return new(null,"invalid_output");}

        // Fail closed if moderation endpoint is unavailable, incomplete or flags
        // the output. Human editorial review remains mandatory even if it passes.
        var (moderation,moderationError)=await PostJsonAsync(ModerationsUri,
            new { model="omni-moderation-latest",input=article.Title+"\n"+article.Body },token);
        if(moderationError is not null)return new(null,moderationError);
        if(moderation is null)return new(null,"provider_unavailable");
        try
        {
            using var moderationDoc=JsonDocument.Parse(moderation);
            var results=moderationDoc.RootElement.GetProperty("results");
            if(results.ValueKind!=JsonValueKind.Array || results.GetArrayLength()!=1 ||
                !results[0].TryGetProperty("flagged",out var flagged) ||
                flagged.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new(null,"provider_unavailable");
            if(flagged.GetBoolean())return new(null,"moderation_blocked");
        }
        catch(JsonException) {return new(null,"provider_unavailable");}
        catch(InvalidOperationException) {return new(null,"provider_unavailable");}
        catch(KeyNotFoundException) {return new(null,"provider_unavailable");}

        return new(article,null);
    }

    private async Task<(string? Raw,string? Reason)> PostJsonAsync(
        Uri uri,object body,CancellationToken token)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,uri);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",settings.ApiKey);
        request.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        try
        {
            using var response=await http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead,token);
            if(!response.IsSuccessStatusCode)
                return (null,response.StatusCode==HttpStatusCode.TooManyRequests ||
                    (int)response.StatusCode>=500 ? "provider_unavailable" : "provider_rejected");
            if(response.Content.Headers.ContentLength is > 65536)
                return (null,"invalid_output");
            // Stream at most 64 KiB even when the provider omits or lies about
            // Content-Length. ReadAsStringAsync would buffer an unbounded body.
            await using var stream=await response.Content.ReadAsStreamAsync(token);
            using var buffer=new MemoryStream();
            var chunk=new byte[8192];
            while(true)
            {
                var count=await stream.ReadAsync(chunk.AsMemory(),token);
                if(count==0)break;
                if(buffer.Length+count>65536)return (null,"invalid_output");
                buffer.Write(chunk,0,count);
            }
            var decoder=new UTF8Encoding(false,true);
            try { return (decoder.GetString(buffer.ToArray()),null); }
            catch(DecoderFallbackException) {return (null,"invalid_output");}
        }
        catch(HttpRequestException) {return (null,"provider_unavailable");}
        catch(TaskCanceledException) when(!token.IsCancellationRequested)
        {return (null,"provider_unavailable");}
    }

    private static string Sanitize(string value)=>
        Regex.Replace(value.Trim(),@"\s+"," ");
}

public sealed record AiEducationGeneration(AiEducationArticle? Article,string? ErrorCode);
