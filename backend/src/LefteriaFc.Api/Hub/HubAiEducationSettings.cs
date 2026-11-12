using System.Text.RegularExpressions;

namespace LefteriaFc.Api.Hub;

/// <summary>
/// Explicit, disabled-by-default settings. There are no defaults for
/// credentials or paid model selection. Never log these values.
/// </summary>
public sealed class HubAiEducationSettings
{
    public bool Enabled { get; }
    public string ApiKey { get; }
    public string Model { get; }
    public string WorkerConnection { get; }

    public HubAiEducationSettings(IConfiguration configuration)
    {
        Enabled=string.Equals(configuration["LEFTERIA_AI_ENABLED"],
            "true",StringComparison.OrdinalIgnoreCase);
        ApiKey=configuration["LEFTERIA_AI_API_KEY"]??"";
        Model=configuration["LEFTERIA_AI_MODEL"]??"";
        WorkerConnection=configuration["LEFTERIA_AI_WORKER_CONNECTION"]??"";
        if(Enabled && (ApiKey.Length<20 || ApiKey.Length>256 ||
            !Regex.IsMatch(Model,@"\A[A-Za-z0-9_.-]{1,80}\z") ||
            string.IsNullOrWhiteSpace(WorkerConnection)))
            throw new InvalidOperationException(
                "AI worker requires explicit dedicated credentials and model.");
    }
}
