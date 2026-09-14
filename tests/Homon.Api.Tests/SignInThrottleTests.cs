using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Homon.Api.Tests;

public class SignInThrottleTests
{
    [Fact]
    public async Task Sign_in_is_throttled_per_address_after_the_permit_limit()
    {
        using var factory = new ThrottledFactory();
        using var client = TestClient.Create(factory);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var refused = await client.SignInAsync(password: "wrong");
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        var throttled = await client.SignInAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
    }

    private sealed class ThrottledFactory : HomonApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SignInThrottle:PermitLimit"] = "3",
                    ["SignInThrottle:WindowMinutes"] = "5",
                }));
        }
    }
}
