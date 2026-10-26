using System.Buffers.Binary;
using LefteriaFc.Application.MobileAuth;
using SkiaSharp;

namespace LefteriaFc.Infrastructure.Identity;

public sealed class SkiaAvatarImageSanitizer : IAvatarImageSanitizer
{
    // Native decoders cannot be cancelled mid-call. Never start an unbounded
    // background task/queue or imply a timeout stopped native work.
    private readonly SemaphoreSlim slots = new(2, 2);

    public SanitizedMobileAvatar Sanitize(ReadOnlyMemory<byte> input, string? declaredContentType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input.IsEmpty || input.Length > MobileAvatarLimits.InputBytes) throw Invalid();
        if (!slots.Wait(0, cancellationToken))
            throw new MobileAuthException(MobileAuthFaultKind.TooManyRequests, MobileAuthErrorCode.AvatarProcessingBusy,
                "Image processing is busy. Try again shortly.", 1);
        try
        {
            using var stream = new MemoryStream(input.ToArray(), writable: false);
            using var codec = SKCodec.Create(stream, out var created);
            if (codec is null || created != SKCodecResult.Success) throw Invalid();
            var actualMime = codec.EncodedFormat switch
            {
                SKEncodedImageFormat.Jpeg => "image/jpeg", SKEncodedImageFormat.Png => "image/png",
                SKEncodedImageFormat.Webp => "image/webp", SKEncodedImageFormat.Gif => "image/gif", _ => null
            };
            if (actualMime is null || (!string.IsNullOrEmpty(declaredContentType) &&
                !string.Equals(actualMime, declaredContentType, StringComparison.OrdinalIgnoreCase)) ||
                !CompleteContainer(input.Span, codec.EncodedFormat)) throw Invalid();
            var info = codec.Info;
            if (info.Width < 1 || info.Height < 1 || info.Width > MobileAvatarLimits.InputSide ||
                info.Height > MobileAvatarLimits.InputSide || (long)info.Width * info.Height > MobileAvatarLimits.InputPixels)
                throw new MobileAuthException(MobileAuthFaultKind.Validation, MobileAuthErrorCode.InvalidAvatar, "The image dimensions are too large.");
            cancellationToken.ThrowIfCancellationRequested();
            using var srgb = SKColorSpace.CreateSrgb();
            var decodedInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul, srgb);
            using var decoded = new SKBitmap();
            if (!decoded.TryAllocPixels(decodedInfo)) throw Unavailable();
            if (codec.GetPixels(decodedInfo, decoded.GetPixels(), decoded.RowBytes, new SKCodecOptions(0)) != SKCodecResult.Success)
                throw Invalid();
            cancellationToken.ThrowIfCancellationRequested();
            var rotated = (int)codec.EncodedOrigin is >= 5 and <= 8;
            var orientedWidth = rotated ? info.Height : info.Width;
            var orientedHeight = rotated ? info.Width : info.Height;
            var ratio = Math.Min(1d, (double)MobileAvatarLimits.OutputSide / Math.Max(orientedWidth, orientedHeight));
            var width = Math.Max(1, (int)Math.Round(orientedWidth * ratio));
            var height = Math.Max(1, (int)Math.Round(orientedHeight * ratio));
            using var destination = new SKBitmap();
            if (!destination.TryAllocPixels(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque, srgb)))
                throw Unavailable();
            using (var canvas = new SKCanvas(destination))
            using (var image = SKImage.FromBitmap(decoded))
            {
                canvas.Clear(SKColors.White);
                var matrix = Orientation(codec.EncodedOrigin, info.Width, info.Height,
                    (float)width / orientedWidth, (float)height / orientedHeight);
                canvas.SetMatrix(in matrix);
                canvas.DrawImage(image, 0f, 0f, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                canvas.Flush();
            }
            cancellationToken.ThrowIfCancellationRequested();
            using var clean = SKImage.FromBitmap(destination);
            using var encoded = clean.Encode(SKEncodedImageFormat.Jpeg, 86);
            if (encoded is null || encoded.Size is < 4 or > MobileAvatarLimits.OutputBytes) throw Invalid();
            return new(encoded.ToArray(), width, height);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
        {
            throw Unavailable();
        }
        catch (ArgumentException) { throw Invalid(); }
        finally { slots.Release(); }
    }

    private static bool CompleteContainer(ReadOnlySpan<byte> bytes, SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => bytes.Length >= 4 && bytes[^2] == 0xff && bytes[^1] == 0xd9,
        SKEncodedImageFormat.Png => bytes.Length >= 12 && bytes[^12..].SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }),
        SKEncodedImageFormat.Webp => bytes.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]) == bytes.Length - 8,
        SKEncodedImageFormat.Gif => bytes.Length >= 14 && bytes[^1] == 0x3b,
        _ => false
    };

    private static SKMatrix Orientation(SKEncodedOrigin origin, int width, int height, float sx, float sy)
    {
        var (a, c, e, b, d, f) = origin switch
        {
            SKEncodedOrigin.TopLeft => (1, 0, 0, 0, 1, 0),
            SKEncodedOrigin.TopRight => (-1, 0, width, 0, 1, 0),
            SKEncodedOrigin.BottomRight => (-1, 0, width, 0, -1, height),
            SKEncodedOrigin.BottomLeft => (1, 0, 0, 0, -1, height),
            SKEncodedOrigin.LeftTop => (0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => (0, -1, height, 1, 0, 0),
            SKEncodedOrigin.RightBottom => (0, -1, height, -1, 0, width),
            SKEncodedOrigin.LeftBottom => (0, 1, 0, -1, 0, width),
            _ => throw Invalid()
        };
        return new SKMatrix(sx * a, sx * c, sx * e, sy * b, sy * d, sy * f, 0, 0, 1);
    }

    private static MobileAuthException Invalid() => new(MobileAuthFaultKind.BadRequest,
        MobileAuthErrorCode.InvalidAvatar, "Choose a complete JPEG, PNG, WebP or GIF image.");
    private static MobileAuthException Unavailable() => new(MobileAuthFaultKind.ServiceUnavailable,
        MobileAuthErrorCode.InvalidAvatar, "Image processing is temporarily unavailable.");
}

public static class AvatarCodecProbe
{
    // Operator/container packaging probe: generated pixels only, no DB or auth.
    public static bool Check()
    {
        try
        {
            using var bitmap = new SKBitmap(1, 1);
            bitmap.Erase(SKColors.White);
            using var image = SKImage.FromBitmap(bitmap);
            using var input = image.Encode(SKEncodedImageFormat.Png, 100);
            var output = new SkiaAvatarImageSanitizer().Sanitize(input.ToArray(), "image/png", CancellationToken.None);
            return output.Width == 1 && output.Height == 1 && output.JpegBytes.Length > 4;
        }
        catch { return false; }
    }
}
