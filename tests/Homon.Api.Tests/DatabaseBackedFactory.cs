namespace Homon.Api.Tests;

/// <summary>
/// A <see cref="HomonApiFactory"/> whose host talks to a PostgreSQL database of its own,
/// cloned from the suite's template and dropped when the class that held it finishes.
/// </summary>
/// <remarks>
/// One clone per fixture instance, and xUnit constructs an <c>IClassFixture</c> once per
/// test class — so "per fixture" is "per class", which is the granularity the suite needs.
/// The clone happens in the constructor because xUnit v2 gives no asynchronous hook there.
/// </remarks>
public abstract class DatabaseBackedFactory : HomonApiFactory
{
    /// <summary>
    /// This fixture's own database. Private to the test class that holds the fixture, and
    /// dropped on disposal.
    /// </summary>
    public string ConnectionString { get; }

    protected DatabaseBackedFactory() => ConnectionString = TestDatabase.CreateClone(GetType().Name);

    public override async ValueTask DisposeAsync()
    {
        // The base disposes the host, which closes whatever it still has open. Dropping first
        // would be dropping a database with a live server attached to it.
        await base.DisposeAsync();

        TestDatabase.Drop(ConnectionString);

        GC.SuppressFinalize(this);
    }
}
