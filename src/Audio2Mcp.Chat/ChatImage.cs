namespace Audio2Mcp.Chat;

/// <summary>An in-memory image attachment. The caller retains ownership of the input stream.</summary>
public sealed class ChatImage
{
    /// <summary>Maximum attachment size before base64 encoding.</summary>
    public const int MaximumBytes = 10 * 1024 * 1024;

    /// <summary>Display name; no filesystem path is sent to the model.</summary>
    public string Name { get; }

    /// <summary>MIME type identified from the image signature.</summary>
    public string MediaType { get; }

    /// <summary>Encoded image bytes retained for subsequent conversation turns.</summary>
    public BinaryData Data { get; }

    private ChatImage(string name, string mediaType, byte[] data)
    {
        Name = name;
        MediaType = mediaType;
        Data = BinaryData.FromBytes(data);
    }

    /// <summary>Reads at most 10 MiB and identifies supported images by their file signature.</summary>
    public static async Task<ChatImage> ReadAsync(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        var block = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(block, cancellationToken)) > 0)
        {
            if (buffer.Length + count > MaximumBytes)
            {
                throw new InvalidOperationException("Chaque image doit faire au maximum 10 Mo.");
            }

            buffer.Write(block, 0, count);
        }

        var data = buffer.ToArray();
        var mediaType = IdentifyMediaType(data);
        return new ChatImage(name, mediaType, data);
    }

    private static string IdentifyMediaType(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            return "image/png";
        }

        if (data.StartsWith(new byte[] { 255, 216, 255 }))
        {
            return "image/jpeg";
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        throw new InvalidOperationException("Format d’image non reconnu. Utilisez PNG, JPEG ou WebP.");
    }
}
