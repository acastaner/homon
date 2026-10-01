using System.Text;

namespace Homon.Domain.Messaging;

/// <summary>
/// Keeps a message body inside <see cref="Message.BodyMaxBytes"/>. Truncating rather than
/// refusing is plan 021's Decision 10: a reporter that has just finished a two-hour backup
/// must not have its proof thrown away over its length, and the half worth keeping is the
/// <em>end</em> — where a shell command puts its summary and its errors.
/// </summary>
public static class MessageBody
{
    /// <summary>
    /// Prepended to a truncated body so nobody reads the remainder as the whole output. Part
    /// of the stored text, which is therefore marginally longer than
    /// <see cref="Message.BodyMaxBytes"/>.
    /// </summary>
    public const string TruncationMarker = "[truncated, showing the last 64 KiB]\n";

    /// <summary>
    /// The body as it should be stored: unchanged when it fits, otherwise the marker followed
    /// by its last <see cref="Message.BodyMaxBytes"/> bytes. Null and empty pass through
    /// untouched.
    /// </summary>
    public static string? Truncate(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return body;
        }

        var bytes = Encoding.UTF8.GetBytes(body);

        if (bytes.Length <= Message.BodyMaxBytes)
        {
            return body;
        }

        var start = bytes.Length - Message.BodyMaxBytes;

        // Measuring in bytes rather than characters is the point — a 64 KiB cap on a column
        // sized in bytes cannot be enforced by counting UTF-16 chars. Cutting at an arbitrary
        // byte can land mid-sequence, so walk forward off any continuation byte (10xxxxxx)
        // before decoding; at most three are skipped.
        while (start < bytes.Length && (bytes[start] & 0b1100_0000) == 0b1000_0000)
        {
            start++;
        }

        return string.Concat(TruncationMarker, Encoding.UTF8.GetString(bytes, start, bytes.Length - start));
    }
}
