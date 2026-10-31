using System.Globalization;

namespace LefteriaFc.MobilePreview;

internal sealed record WebPlatformPreviewOptions(int Port, Uri WebOrigin, bool Smoke)
{
    public Uri Origin => new($"http://localhost:{Port}");

    public static WebPlatformPreviewOptions Parse(string[] args)
    {
        var port = 4329;
        var webOrigin = "http://127.0.0.1:5183";
        var smoke = false;
        var portProvided = false;
        var webProvided = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--smoke" when !smoke: smoke = true; break;
                case "--port" when !portProvided && index + 1 < args.Length:
                    portProvided = true;
                    if (!int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1024 or > 65535)
                        throw new PreviewUsageException("The web-platform preview port must be between 1024 and 65535.");
                    break;
                case "--web-origin" when !webProvided && index + 1 < args.Length:
                    webProvided = true; webOrigin = args[++index]; break;
                default: throw new PreviewUsageException("Usage: MobilePreview --web-platform [--port 4329] [--web-origin http://127.0.0.1:5183] [--smoke]");
            }
        }
        if (!Uri.TryCreate(webOrigin, UriKind.Absolute, out var web) || web.Scheme != "http" || web.Host != "127.0.0.1" ||
            web.Port is < 1024 or > 65535 || web.Port == port || web.UserInfo.Length != 0 || web.AbsolutePath != "/" ||
            webOrigin.IndexOfAny(['?', '#', '@', '\\']) >= 0)
            throw new PreviewUsageException("The web server must use HTTP 127.0.0.1 on a separate unprivileged port.");
        return new(port, web, smoke);
    }
}
