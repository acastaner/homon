using Homon.Domain.Weather;

namespace Homon.Api.Tests;

public class WmoWeatherCodeMapTests
{
    [Theory]
    [InlineData(0, WeatherCondition.Clear)]
    [InlineData(1, WeatherCondition.Clear)]
    [InlineData(2, WeatherCondition.PartlyCloudy)]
    [InlineData(3, WeatherCondition.Cloudy)]
    [InlineData(45, WeatherCondition.Fog)]
    [InlineData(48, WeatherCondition.Fog)]
    [InlineData(51, WeatherCondition.Drizzle)]
    [InlineData(53, WeatherCondition.Drizzle)]
    [InlineData(55, WeatherCondition.Drizzle)]
    [InlineData(56, WeatherCondition.Drizzle)]
    [InlineData(57, WeatherCondition.Drizzle)]
    [InlineData(61, WeatherCondition.Rain)]
    [InlineData(63, WeatherCondition.Rain)]
    [InlineData(65, WeatherCondition.Rain)]
    [InlineData(66, WeatherCondition.Rain)]
    [InlineData(67, WeatherCondition.Rain)]
    [InlineData(80, WeatherCondition.Rain)]
    [InlineData(81, WeatherCondition.Rain)]
    [InlineData(82, WeatherCondition.Rain)]
    [InlineData(71, WeatherCondition.Snow)]
    [InlineData(73, WeatherCondition.Snow)]
    [InlineData(75, WeatherCondition.Snow)]
    [InlineData(77, WeatherCondition.Snow)]
    [InlineData(85, WeatherCondition.Snow)]
    [InlineData(86, WeatherCondition.Snow)]
    [InlineData(95, WeatherCondition.Thunderstorm)]
    [InlineData(96, WeatherCondition.Thunderstorm)]
    [InlineData(99, WeatherCondition.Thunderstorm)]
    [InlineData(4, WeatherCondition.Unknown)]
    [InlineData(-1, WeatherCondition.Unknown)]
    [InlineData(1000, WeatherCondition.Unknown)]
    public void Map_follows_the_WMO_table(int wmoCode, WeatherCondition expected)
    {
        Assert.Equal(expected, WmoWeatherCodeMap.Map(wmoCode));
    }
}
