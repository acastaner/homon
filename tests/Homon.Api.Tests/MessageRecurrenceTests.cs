using Homon.Domain.Messaging;

namespace Homon.Api.Tests;

public class MessageRecurrenceTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 1, 4, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Nothing_declared_is_valid_and_means_never_overdue()
    {
        var result = MessageRecurrence.Resolve(null, null, ReceivedAt);

        Assert.True(result.IsValid);
        Assert.Null(result.NextExpectedAt);
        Assert.Null(result.Declaration);
    }

    [Theory]
    [InlineData("PT25H", 25 * 60)]
    [InlineData("P1D", 24 * 60)]
    [InlineData("PT90M", 90)]
    // XmlConvert counts a month as 30 days and a year as 365 — documented, not a bug, and the
    // reason a reporter needing a real calendar sends expectNextBy instead.
    [InlineData("P1M", 30 * 24 * 60)]
    [InlineData("P5Y", 5 * 365 * 24 * 60)]
    public void An_iso_duration_becomes_a_deadline_measured_from_arrival(string declared, int expectedMinutes)
    {
        var result = MessageRecurrence.Resolve(declared, null, ReceivedAt);

        Assert.True(result.IsValid);
        Assert.Equal(ReceivedAt.AddMinutes(expectedMinutes), result.NextExpectedAt);
        Assert.Equal(declared, result.Declaration);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_rather_than_refused()
    {
        var result = MessageRecurrence.Resolve("  PT25H\n", null, ReceivedAt);

        Assert.True(result.IsValid);
        Assert.Equal("PT25H", result.Declaration);
    }

    [Theory]
    [InlineData("25h")]
    [InlineData("tomorrow")]
    [InlineData("P")]
    [InlineData("PT")]
    public void A_malformed_duration_is_refused_against_the_recurrence_field(string declared)
    {
        var result = MessageRecurrence.Resolve(declared, null, ReceivedAt);

        Assert.False(result.IsValid);
        Assert.Equal("recurrence", result.ErrorField);
        Assert.Contains("ISO 8601", result.ErrorMessage, StringComparison.Ordinal);
        // The surprise has to be in the message the script author actually sees.
        Assert.Contains("30 days", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PT0S")]
    [InlineData("-PT1H")]
    [InlineData("-P1D")]
    public void A_non_positive_duration_is_refused(string declared)
    {
        var result = MessageRecurrence.Resolve(declared, null, ReceivedAt);

        Assert.False(result.IsValid);
        Assert.Equal("recurrence", result.ErrorField);
        Assert.Contains("positive", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duration_beyond_the_horizon_is_refused()
    {
        var result = MessageRecurrence.Resolve("P99Y", null, ReceivedAt);

        Assert.False(result.IsValid);
        Assert.Equal("recurrence", result.ErrorField);
        Assert.Contains($"{MessageRecurrence.MaxHorizonDays} days", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absurdly_long_declaration_is_refused_before_it_is_parsed()
    {
        var result = MessageRecurrence.Resolve(new string('P', 200), null, ReceivedAt);

        Assert.False(result.IsValid);
        Assert.Equal("recurrence", result.ErrorField);
        Assert.Contains($"{Message.DeclarationMaxLength} characters", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absolute_instant_is_taken_verbatim_and_kept_for_display()
    {
        var deadline = ReceivedAt.AddHours(25);

        var result = MessageRecurrence.Resolve(null, deadline, ReceivedAt);

        Assert.True(result.IsValid);
        Assert.Equal(deadline, result.NextExpectedAt);
        Assert.Equal("2026-10-02 05:30:00Z", result.Declaration);
        Assert.True(result.Declaration!.Length <= Message.DeclarationMaxLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_instant_that_is_not_in_the_future_is_refused_against_its_own_field(int offsetMinutes)
    {
        var result = MessageRecurrence.Resolve(null, ReceivedAt.AddMinutes(offsetMinutes), ReceivedAt);

        Assert.False(result.IsValid);
        Assert.Equal("expectNextBy", result.ErrorField);
        Assert.Contains("future", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void An_instant_beyond_the_horizon_is_refused()
    {
        var result = MessageRecurrence.Resolve(null, ReceivedAt.AddYears(99), ReceivedAt);

        Assert.False(result.IsValid);
        Assert.Equal("expectNextBy", result.ErrorField);
    }

    [Fact]
    public void Declaring_both_forms_is_refused_against_recurrence()
    {
        var result = MessageRecurrence.Resolve("PT25H", ReceivedAt.AddHours(25), ReceivedAt);

        Assert.False(result.IsValid);
        Assert.Equal("recurrence", result.ErrorField);
        Assert.Contains("not both", result.ErrorMessage, StringComparison.Ordinal);
    }
}
