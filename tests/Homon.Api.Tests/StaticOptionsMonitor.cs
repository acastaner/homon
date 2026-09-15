using Microsoft.Extensions.Options;

namespace Homon.Api.Tests;

/// <summary>
/// An <see cref="IOptionsMonitor{TOptions}"/> that never changes — enough for any test that
/// constructs a service by hand instead of through a <see cref="HomonApiFactory"/> host.
/// </summary>
internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
