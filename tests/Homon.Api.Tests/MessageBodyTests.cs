using System.Text;
using Homon.Domain.Messaging;

namespace Homon.Api.Tests;

public class MessageBodyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_to_truncate_passes_through(string? body) =>
        Assert.Equal(body, MessageBody.Truncate(body));

    [Fact]
    public void A_body_that_fits_is_returned_unchanged()
    {
        var body = new string('x', Message.BodyMaxBytes);

        Assert.Same(body, MessageBody.Truncate(body));
    }

    [Fact]
    public void An_oversized_body_keeps_its_tail_behind_the_marker()
    {
        // The end is the half worth keeping: that is where a shell command puts its summary and
        // its errors (plan 021, Decision 10).
        var body = new string('x', Message.BodyMaxBytes) + "snapshot a1b2c3 saved";

        var truncated = MessageBody.Truncate(body)!;

        Assert.StartsWith(MessageBody.TruncationMarker, truncated, StringComparison.Ordinal);
        Assert.EndsWith("snapshot a1b2c3 saved", truncated, StringComparison.Ordinal);
        Assert.Equal(
            Message.BodyMaxBytes,
            Encoding.UTF8.GetByteCount(truncated[MessageBody.TruncationMarker.Length..]));
    }

    [Fact]
    public void Truncation_never_splits_a_multi_byte_character()
    {
        // Every character here is three UTF-8 bytes, so a byte-exact cut lands mid-sequence on
        // two cuts out of three. A replacement character in the output would mean the walk off
        // the continuation bytes is wrong.
        for (var padding = 0; padding < 3; padding++)
        {
            var body = new string('x', padding) + string.Concat(Enumerable.Repeat("家", Message.BodyMaxBytes));

            var truncated = MessageBody.Truncate(body)!;

            Assert.DoesNotContain('�', truncated);
            Assert.True(Encoding.UTF8.GetByteCount(truncated[MessageBody.TruncationMarker.Length..]) <= Message.BodyMaxBytes);
        }
    }
}
