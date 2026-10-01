using Homon.Domain.Weather;

namespace Homon.Api.Tests;

/// <summary>
/// The derived-advisory rules from plan 020, D3 and D4. Pure domain code: no host, no
/// database, no HTTP.
/// </summary>
public class WeatherWarningEvaluatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    // ---------------------------------------------------------------- thresholds, per kind

    [Theory]
    // Wind reads the hourly gust. Metric: caution 60, severe 90.
    [InlineData(59.9, null)]
    [InlineData(60, WeatherWarningSeverity.Caution)]
    [InlineData(89.9, WeatherWarningSeverity.Caution)]
    [InlineData(90, WeatherWarningSeverity.Severe)]
    public void Wind_grades_off_the_hourly_gust(double gust, WeatherWarningSeverity? expected)
    {
        var warnings = WeatherWarningEvaluator.Evaluate(
            [], [Hour("12:00", gusts: gust)], WeatherUnits.Metric);

        AssertSeverity(warnings, WeatherWarningKind.Wind, expected);
    }

    [Theory]
    // Imperial: caution 38, severe 56. 50 mph is a caution here and would be nothing at all
    // under the metric figures, which is what proves the units argument is read.
    [InlineData(37.9, null)]
    [InlineData(38, WeatherWarningSeverity.Caution)]
    [InlineData(50, WeatherWarningSeverity.Caution)]
    [InlineData(56, WeatherWarningSeverity.Severe)]
    public void Wind_uses_the_imperial_figures_under_imperial_units(double gust, WeatherWarningSeverity? expected)
    {
        var warnings = WeatherWarningEvaluator.Evaluate(
            [], [Hour("12:00", gusts: gust)], WeatherUnits.Imperial);

        AssertSeverity(warnings, WeatherWarningKind.Wind, expected);
    }

    [Theory]
    // The same number grades differently because the imperial thresholds are lower numbers
    // on a coarser scale — 40 is below the 60 km/h caution but above the 38 mph one, and 60
    // is a metric caution and an imperial severe. This is the test that would catch a
    // `units` argument that was accepted and then ignored.
    [InlineData(40, null, WeatherWarningSeverity.Caution)]
    [InlineData(60, WeatherWarningSeverity.Caution, WeatherWarningSeverity.Severe)]
    [InlineData(95, WeatherWarningSeverity.Severe, WeatherWarningSeverity.Severe)]
    public void One_gust_figure_grades_differently_under_each_unit_system(
        double gust, WeatherWarningSeverity? metric, WeatherWarningSeverity? imperial)
    {
        var hours = new[] { Hour("12:00", gusts: gust) };

        AssertSeverity(
            WeatherWarningEvaluator.Evaluate([], hours, WeatherUnits.Metric),
            WeatherWarningKind.Wind,
            metric);

        AssertSeverity(
            WeatherWarningEvaluator.Evaluate([], hours, WeatherUnits.Imperial),
            WeatherWarningKind.Wind,
            imperial);
    }

    [Theory]
    [InlineData(95, WeatherWarningSeverity.Caution)]
    [InlineData(96, WeatherWarningSeverity.Severe)]
    [InlineData(99, WeatherWarningSeverity.Severe)]
    [InlineData(82, null)]
    public void Thunderstorm_grades_off_the_raw_WMO_code(int wmoCode, WeatherWarningSeverity? expected)
    {
        var warnings = WeatherWarningEvaluator.Evaluate(
            [], [Hour("15:00", wmoCode: wmoCode)], WeatherUnits.Metric);

        AssertSeverity(warnings, WeatherWarningKind.Thunderstorm, expected);
    }

    [Fact]
    public void Thunderstorm_carries_no_figure()
    {
        var warnings = WeatherWarningEvaluator.Evaluate(
            [], [Hour("15:00", wmoCode: 99)], WeatherUnits.Metric);

        Assert.Null(Single(warnings, WeatherWarningKind.Thunderstorm).Value);
    }

    [Theory]
    // Snowfall is centimetres in metric — Open-Meteo's own asymmetry. Caution 1, severe 5.
    [InlineData(0.9, null)]
    [InlineData(1, WeatherWarningSeverity.Caution)]
    [InlineData(5, WeatherWarningSeverity.Severe)]
    public void Snow_grades_off_the_daily_snowfall(double snowfall, WeatherWarningSeverity? expected)
    {
        var warnings = WeatherWarningEvaluator.Evaluate(
            [Day(Today, snowfallSum: snowfall)], [], WeatherUnits.Metric);

        AssertSeverity(warnings, WeatherWarningKind.Snow, expected);
    }

    [Theory]
    [InlineData(0.3, null)]
    [InlineData(0.4, WeatherWarningSeverity.Caution)]
    [InlineData(2, WeatherWarningSeverity.Severe)]
    public void Snow_uses_the_imperial_figures_under_imperial_units(double snowfall, WeatherWarningSeverity? expected)
    {
        var warnings = WeatherWarningEvaluator.Evaluate(
            [Day(Today, snowfallSum: snowfall)], [], WeatherUnits.Imperial);

        AssertSeverity(warnings, WeatherWarningKind.Snow, expected);
    }

    [Theory]
    [InlineData(19.9, null)]
    [InlineData(20, WeatherWarningSeverity.Caution)]
    [InlineData(40, WeatherWarningSeverity.Severe)]
    public void Rain_grades_off_the_daily_precipitation(double millimetres, WeatherWarningSeverity? expected)
    {
        var warnings = WeatherWarningEvaluator.Evaluate(
            [Day(Today, precipitationSum: millimetres)], [], WeatherUnits.Metric);

        AssertSeverity(warnings, WeatherWarningKind.Rain, expected);
    }

    [Theory]
    [InlineData(0.7, null)]
    [InlineData(0.8, WeatherWarningSeverity.Caution)]
    [InlineData(1.6, WeatherWarningSeverity.Severe)]
    public void Rain_uses_the_imperial_figures_under_imperial_units(double inches, WeatherWarningSeverity? expected)
    {
        var warnings = WeatherWarningEvaluator.Evaluate(
            [Day(Today, precipitationSum: inches)], [], WeatherUnits.Imperial);

        AssertSeverity(warnings, WeatherWarningKind.Rain, expected);
    }

    [Theory]
    [InlineData(31.9, null)]
    [InlineData(32, WeatherWarningSeverity.Caution)]
    [InlineData(38, WeatherWarningSeverity.Severe)]
    public void Heat_grades_off_the_daily_high(double high, WeatherWarningSeverity? expected)
    {
        var warnings = WeatherWarningEvaluator.Evaluate(
            [Day(Today, high: high, low: high - 8)], [], WeatherUnits.Metric);

        AssertSeverity(warnings, WeatherWarningKind.Heat, expected);
    }

    [Theory]
    [InlineData(89, null)]
    [InlineData(90, WeatherWarningSeverity.Caution)]
    [InlineData(100, WeatherWarningSeverity.Severe)]
    public void Heat_uses_the_imperial_figures_under_imperial_units(double high, WeatherWarningSeverity? expected)
    {
        var warnings = WeatherWarningEvaluator.Evaluate(
            [Day(Today, high: high, low: high - 15)], [], WeatherUnits.Imperial);

        AssertSeverity(warnings, WeatherWarningKind.Heat, expected);
    }

    [Theory]
    // Cold is the inverted comparison: a lower number is worse.
    [InlineData(-9.9, null)]
    [InlineData(-10, WeatherWarningSeverity.Caution)]
    [InlineData(-18, WeatherWarningSeverity.Severe)]
    [InlineData(-30, WeatherWarningSeverity.Severe)]
    public void Cold_grades_off_the_daily_low_with_the_comparison_inverted(
        double low, WeatherWarningSeverity? expected)
    {
        var warnings = WeatherWarningEvaluator.Evaluate(
            [Day(Today, high: low + 5, low: low)], [], WeatherUnits.Metric);

        AssertSeverity(warnings, WeatherWarningKind.Cold, expected);
    }

    [Theory]
    [InlineData(15, null)]
    [InlineData(14, WeatherWarningSeverity.Caution)]
    [InlineData(0, WeatherWarningSeverity.Severe)]
    public void Cold_uses_the_imperial_figures_under_imperial_units(double low, WeatherWarningSeverity? expected)
    {
        var warnings = WeatherWarningEvaluator.Evaluate(
            [Day(Today, high: low + 10, low: low)], [], WeatherUnits.Imperial);

        AssertSeverity(warnings, WeatherWarningKind.Cold, expected);
    }

    // ---------------------------------------------------------------- runs and de-duplication

    [Fact]
    public void Six_consecutive_gusty_hours_collapse_into_one_warning()
    {
        var hours = new[]
        {
            Hour("12:00", gusts: 40),
            Hour("13:00", gusts: 65),
            Hour("14:00", gusts: 94),
            Hour("15:00", gusts: 71),
            Hour("16:00", gusts: 63),
            Hour("17:00", gusts: 30),
        };

        var warnings = WeatherWarningEvaluator.Evaluate([], hours, WeatherUnits.Metric);

        var wind = Single(warnings, WeatherWarningKind.Wind);
        Assert.Equal(WeatherWarningSeverity.Severe, wind.Severity);
        Assert.Equal("13:00", wind.FromTime);
        Assert.Equal("16:00", wind.ToTime);
        Assert.Equal(94, wind.Value);
    }

    [Fact]
    public void A_run_broken_by_a_calm_hour_still_yields_one_warning_and_it_is_the_worse_run()
    {
        var hours = new[]
        {
            Hour("09:00", gusts: 62),  // a caution run
            Hour("10:00", gusts: 30),  // break
            Hour("11:00", gusts: 95),  // a severe run — this is the one a reader needs
        };

        var warnings = WeatherWarningEvaluator.Evaluate([], hours, WeatherUnits.Metric);

        var wind = Single(warnings, WeatherWarningKind.Wind);
        Assert.Equal(WeatherWarningSeverity.Severe, wind.Severity);
        Assert.Equal("11:00", wind.FromTime);
        Assert.Equal("11:00", wind.ToTime);
    }

    [Fact]
    public void A_tie_on_severity_between_two_runs_goes_to_the_earlier_one()
    {
        var hours = new[]
        {
            Hour("09:00", gusts: 62),
            Hour("10:00", gusts: 30),
            Hour("11:00", gusts: 70),
        };

        var wind = Single(
            WeatherWarningEvaluator.Evaluate([], hours, WeatherUnits.Metric), WeatherWarningKind.Wind);

        Assert.Equal("09:00", wind.FromTime);
    }

    [Fact]
    public void A_run_never_spans_midnight()
    {
        var tomorrow = Today.AddDays(1);

        var hours = new[]
        {
            Hour("22:00", gusts: 65, date: Today),
            Hour("23:00", gusts: 66, date: Today),
            Hour("00:00", gusts: 67, date: tomorrow),
            Hour("01:00", gusts: 68, date: tomorrow),
        };

        var wind = Single(
            WeatherWarningEvaluator.Evaluate([], hours, WeatherUnits.Metric), WeatherWarningKind.Wind);

        // Both runs are cautions, so the earlier wins — and it stops at the date boundary
        // rather than describing 22:00–01:00 as one window on the wrong day.
        Assert.Equal(Today, wind.Date);
        Assert.Equal("22:00", wind.FromTime);
        Assert.Equal("23:00", wind.ToTime);
    }

    [Fact]
    public void A_kind_tripped_on_several_days_yields_one_warning_at_the_worst_day()
    {
        var days = new[]
        {
            Day(Today, precipitationSum: 22),
            Day(Today.AddDays(1), precipitationSum: 45),
        };

        var rain = Single(
            WeatherWarningEvaluator.Evaluate(days, [], WeatherUnits.Metric), WeatherWarningKind.Rain);

        Assert.Equal(WeatherWarningSeverity.Severe, rain.Severity);
        Assert.Equal(Today.AddDays(1), rain.Date);
        Assert.Equal(45, rain.Value);
    }

    // ---------------------------------------------------------------- ordering and the cap

    [Fact]
    public void Severe_sorts_before_caution_even_when_the_caution_starts_earlier()
    {
        var days = new[] { Day(Today, precipitationSum: 25) };          // caution, whole day
        var hours = new[] { Hour("23:00", gusts: 95) };                  // severe, late

        var warnings = WeatherWarningEvaluator.Evaluate(days, hours, WeatherUnits.Metric);

        Assert.Equal(WeatherWarningKind.Wind, warnings[0].Kind);
        Assert.Equal(WeatherWarningKind.Rain, warnings[1].Kind);
    }

    [Fact]
    public void At_most_three_warnings_survive_and_they_are_the_three_the_order_picks()
    {
        // Five kinds trip: Wind severe, then Snow / Rain / Heat / Cold as cautions on the
        // same day. Severity picks Wind first; the kind order then picks Snow and Rain.
        var days = new[]
        {
            Day(Today, high: 33, low: -11, precipitationSum: 25, snowfallSum: 2),
        };
        var hours = new[] { Hour("14:00", gusts: 95) };

        var warnings = WeatherWarningEvaluator.Evaluate(days, hours, WeatherUnits.Metric);

        Assert.Equal(WeatherWarningEvaluator.MaxWarnings, warnings.Count);
        Assert.Equal(
            [WeatherWarningKind.Wind, WeatherWarningKind.Snow, WeatherWarningKind.Rain],
            warnings.Select(warning => warning.Kind));
    }

    // ---------------------------------------------------------------- nulls and empties

    [Fact]
    public void A_null_figure_trips_nothing()
    {
        var days = new[] { Day(Today, precipitationSum: null, snowfallSum: null) };
        var hours = new[] { Hour("12:00", gusts: null) };

        Assert.Empty(WeatherWarningEvaluator.Evaluate(days, hours, WeatherUnits.Metric));
    }

    [Fact]
    public void An_empty_window_returns_an_empty_list()
    {
        Assert.Empty(WeatherWarningEvaluator.Evaluate([], [], WeatherUnits.Metric));
    }

    [Fact]
    public void A_calm_window_returns_an_empty_list()
    {
        var days = new[] { Day(Today, high: 18, low: 9, precipitationSum: 2, snowfallSum: 0) };
        var hours = new[] { Hour("12:00", gusts: 14), Hour("13:00", gusts: 11) };

        Assert.Empty(WeatherWarningEvaluator.Evaluate(days, hours, WeatherUnits.Metric));
    }

    // ---------------------------------------------------------------- helpers

    private static WeatherHour Hour(
        string time,
        double? gusts = 5,
        int wmoCode = 3,
        DateOnly? date = null) =>
        new(
            Date: date ?? Today,
            Time: time,
            Condition: WmoWeatherCodeMap.Map(wmoCode),
            WmoCode: wmoCode,
            Temperature: 16,
            ApparentTemperature: 15,
            WindSpeed: 12,
            WindGusts: gusts,
            PrecipitationProbability: 20);

    private static WeatherDay Day(
        DateOnly date,
        double high = 18,
        double low = 9,
        double? precipitationSum = 0,
        double? snowfallSum = 0) =>
        new(
            Date: date,
            Condition: WeatherCondition.Cloudy,
            High: high,
            Low: low,
            PrecipitationSum: precipitationSum,
            SnowfallSum: snowfallSum,
            WindSpeedMax: 20,
            WindGustsMax: 30,
            Sunrise: "07:01",
            Sunset: "18:38");

    private static WeatherWarning Single(IReadOnlyList<WeatherWarning> warnings, WeatherWarningKind kind) =>
        Assert.Single(warnings, warning => warning.Kind == kind);

    private static void AssertSeverity(
        IReadOnlyList<WeatherWarning> warnings, WeatherWarningKind kind, WeatherWarningSeverity? expected)
    {
        if (expected is null)
        {
            Assert.DoesNotContain(warnings, warning => warning.Kind == kind);
            return;
        }

        Assert.Equal(expected, Single(warnings, kind).Severity);
    }
}
