using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Homon.Api.Tests;

/// <summary>
/// One PostgreSQL <i>server</i> for the whole suite, and one <i>database</i> per test class,
/// cloned from a migrated template.
/// </summary>
/// <remarks>
/// <para>
/// The server is not this class's business — <c>HOMON_TEST_CONNECTION</c> names one that is
/// already running (<c>ci/compose.ci.yaml</c>'s container, or a developer's own). What this
/// class owns is everything above it: a template built once per test process, a clone handed
/// to each <see cref="DatabaseBackedFactory"/>, and the sweep that keeps a killed run from
/// leaving its databases behind.
/// </para>
/// <para>
/// <b>Why per-class databases.</b> Sharing one database means every class that writes races
/// every class that reads, and the only cure is serialising the whole suite. A clone of the
/// migrated template costs tens of milliseconds, so isolation is cheaper than serialisation
/// and <c>xunit.runner.json</c> keeps the collections parallel. What is shared is the server
/// process, never any data.
/// </para>
/// <para>
/// <b>This creates and drops databases</b>, so the role the connection string names needs
/// <c>CREATEDB</c>. The CI container's role is the image's superuser and has it; a
/// development role must be granted it — see <c>docs/postgres-setup-dev.md</c>.
/// </para>
/// </remarks>
internal static class TestDatabase
{
    /// <summary>
    /// The prefix every database this suite creates carries, and that nothing else on the
    /// server may. <see cref="SweepAsync"/> matches on it and drops what it finds, so this
    /// constant is the whole fence between a test run and a server it shares with other work.
    /// </summary>
    private const string Prefix = "homon_test_";

    private const string TemplateName = Prefix + "template";

    /// <summary>
    /// Serialises the clones. Concurrent <c>CREATE DATABASE … TEMPLATE</c> against one
    /// template collide, and there is nothing to win by overlapping them.
    /// </summary>
    private static readonly SemaphoreSlim CloneLock = new(1, 1);

    /// <summary>Built once per test process, on whichever class asks for a database first.</summary>
    private static readonly Lazy<Task> Template = new(BuildTemplateAsync);

    private static int _counter;

    /// <summary>
    /// Creates a database cloned from the template and returns its connection string.
    /// Blocking, because xUnit v2 constructs an <c>IClassFixture</c> synchronously and there
    /// is nowhere else to put this.
    /// </summary>
    /// <param name="label">
    /// The fixture's type name, folded into the database name so that <c>\l</c> on a run that
    /// died mid-suite says which class was holding what.
    /// </param>
    public static string CreateClone(string label)
    {
        Template.Value.GetAwaiter().GetResult();

        var name = $"{Prefix}{Sanitise(label)}_{Interlocked.Increment(ref _counter)}";

        CloneLock.Wait();
        try
        {
            ExecuteAsync(AdminConnectionString, $"""CREATE DATABASE "{name}" TEMPLATE "{TemplateName}";""")
                .GetAwaiter().GetResult();
        }
        finally
        {
            CloneLock.Release();
        }

        return ConnectionStringFor(name);
    }

    /// <summary>
    /// Drops a database created by <see cref="CreateClone"/>. Silent when it is already gone,
    /// so a fixture that failed halfway through construction still disposes cleanly.
    /// </summary>
    /// <remarks>
    /// <c>WITH (FORCE)</c> rather than a bare drop: the host this database served has just
    /// been disposed, and a connection that outlives its host by a few milliseconds would
    /// otherwise turn tidying up into a test failure.
    /// </remarks>
    public static void Drop(string connectionString)
    {
        var name = new NpgsqlConnectionStringBuilder(connectionString).Database;

        if (string.IsNullOrEmpty(name) || !name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to drop '{name}' — this suite only ever drops databases it created, "
                + $"which are the ones named {Prefix}*.");
        }

        ExecuteAsync(AdminConnectionString, $"""DROP DATABASE IF EXISTS "{name}" WITH (FORCE);""")
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// The connection string as configured, naming the server and the database DDL is issued
    /// over. Nothing ever connects to the template or to another class's clone through it.
    /// </summary>
    private static string AdminConnectionString =>
        DatabaseFactAttribute.ConnectionString
        ?? throw new InvalidOperationException(
            $"{DatabaseFactAttribute.ConnectionVariable} is not set. A [DatabaseFact] should have "
            + "skipped before reaching here — see DatabaseFactAttribute.");

    private static async Task BuildTemplateAsync()
    {
        // A run that was killed leaves its databases behind, and the next run must not inherit
        // them — least of all a template built against an older migration set. Sweeping on
        // START rather than only on finish is what makes that true.
        await SweepAsync();

        await ExecuteAsync(AdminConnectionString, $"""CREATE DATABASE "{TemplateName}";""");

        var templateConnectionString = ConnectionStringFor(TemplateName);

        await using (var database = new HomonDbContext(
            new DbContextOptionsBuilder<HomonDbContext>()
                .UseNpgsql(templateConnectionString)
                .Options))
        {
            await database.Database.MigrateAsync();
        }

        // MANDATORY. `CREATE DATABASE … TEMPLATE` fails outright while ANY session is connected
        // to the source, so a single leaked connection here would break every clone that
        // follows. ConnectionStringFor disables pooling, so disposing the context above already
        // closed it — this is the belt to that braces, and it must stay even if pooling is ever
        // turned back on.
        NpgsqlConnection.ClearPool(new NpgsqlConnection(templateConnectionString));
    }

    /// <summary>Drops every database this suite has ever created on this server.</summary>
    private static async Task SweepAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();

        var stale = new List<string>();

        await using (var query = new NpgsqlCommand(
            "select datname from pg_database where datname like @pattern", connection))
        {
            query.Parameters.AddWithValue("pattern", Prefix + "%");

            await using var reader = await query.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                stale.Add(reader.GetString(0));
            }
        }

        foreach (var name in stale)
        {
            await using var drop = new NpgsqlCommand(
                $"""DROP DATABASE IF EXISTS "{name}" WITH (FORCE);""", connection);

            await drop.ExecuteNonQueryAsync();
        }
    }

    private static string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = database,

            // NO POOLING, and it is load-bearing. Every database is its own connection string
            // and therefore its own Npgsql pool, and a pool outlives the class that created it
            // until the pruner reclaims it — so the open-connection count tracks test *history*
            // rather than test *concurrency*, and a pooled connection to the template outlives
            // the context that opened it, which blocks `CREATE DATABASE … TEMPLATE` and hangs
            // the suite. Off, the count tracks the classes actually running, at the price of
            // one handshake per operation over loopback. Do not reinstate pooling.
            Pooling = false,
        }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Folds a fixture's type name into something legal and readable as a database name.
    /// Identifiers are lower-cased and capped at 63 bytes by PostgreSQL, and a name that
    /// silently truncates is a name that can collide.
    /// </summary>
    private static string Sanitise(string label)
    {
        var folded = new string([.. label.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit)]);

        return folded.Length <= 32 ? folded : folded[..32];
    }
}
