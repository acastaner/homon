namespace Homon.Api.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself when no test database is configured.
/// </summary>
/// <remarks>
/// <para>
/// The repository's default is that no test touches a database — <see cref="HomonApiFactory"/>
/// deliberately stands up none. That default is worth keeping: it makes <c>dotnet test</c>
/// work on a fresh clone with nothing installed. <c>ci/run-ci.sh</c> then asserts that
/// <i>nothing</i> was skipped, so the gate is only green when the database-backed tests ran.
/// </para>
/// <para>
/// <see cref="ConnectionVariable"/> names a <i>server</i>, not the database under test. Each
/// test class gets a database of its own, cloned from a template and dropped afterwards, so
/// the role needs <c>CREATEDB</c> and the suite will create and drop databases named
/// <c>homon_test_*</c> on whatever server this points at. Nothing outside that prefix is
/// ever touched — <see cref="TestDatabase"/> refuses to drop a name that does not carry it —
/// but point this at a development server, not anything precious.
/// </para>
/// </remarks>
public sealed class DatabaseFactAttribute : FactAttribute
{
    /// <summary>The environment variable holding the test connection string.</summary>
    public const string ConnectionVariable = "HOMON_TEST_CONNECTION";

    public DatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            Skip = $"Set {ConnectionVariable} to run the database-backed tests. "
                + "See docs/postgres-setup-dev.md.";
        }
    }

    /// <summary>The configured connection string, or null when there is none.</summary>
    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionVariable);
}

/// <summary>
/// <see cref="DatabaseFactAttribute"/> for a table-driven test. Same switch, same reasons.
/// </summary>
public sealed class DatabaseTheoryAttribute : TheoryAttribute
{
    public DatabaseTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(DatabaseFactAttribute.ConnectionString))
        {
            Skip = $"Set {DatabaseFactAttribute.ConnectionVariable} to run the database-backed "
                + "tests. See docs/postgres-setup-dev.md.";
        }
    }
}
