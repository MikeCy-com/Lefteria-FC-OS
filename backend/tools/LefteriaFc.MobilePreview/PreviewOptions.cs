using System.Globalization;

namespace LefteriaFc.MobilePreview;

internal sealed class PreviewUsageException(string message) : Exception(message);

internal sealed record PreviewOptions(int Port, Uri WebOrigin, bool Smoke)
{
    public Uri Origin => new($"http://127.0.0.1:{Port}");

    public static PreviewOptions Parse(string[] args)
    {
        var port = 4190;
        var origin = "http://127.0.0.1:4188";
        var smoke = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--smoke" when !smoke: smoke = true; break;
                case "--port" when index + 1 < args.Length:
                    if (!int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
                        port is < 1024 or > 65535)
                        throw new PreviewUsageException("The preview port must be between 1024 and 65535.");
                    break;
                case "--web-origin" when index + 1 < args.Length: origin = args[++index]; break;
                default: throw new PreviewUsageException("Usage: MobilePreview [--port 4190] [--web-origin http://127.0.0.1:4188] [--smoke]");
            }
        }
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var parsed) || parsed.Scheme != "http" ||
            parsed.Host != "127.0.0.1" || parsed.Port is < 1024 or > 65535 || parsed.Port == port ||
            parsed.UserInfo.Length != 0 || parsed.AbsolutePath != "/" ||
            origin.IndexOfAny(['?', '#', '@', '\\']) >= 0)
            throw new PreviewUsageException("The web origin must be an HTTP 127.0.0.1 origin on a separate unprivileged port.");
        return new(port, parsed, smoke);
    }
}
