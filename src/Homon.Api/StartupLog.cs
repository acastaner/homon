namespace Homon.Api;

/// <summary>Source-generated log messages Program.cs emits at boot.</summary>
internal static partial class StartupLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "No administrator is configured, so nobody can sign in. Set Administrator:Email and "
            + "Administrator:PasswordHash — mint the hash with `dotnet run --project src/Homon.Api -- hash-password`.")]
    internal static partial void NoAdministratorConfigured(ILogger logger);
}
