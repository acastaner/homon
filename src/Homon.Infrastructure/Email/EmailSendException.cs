namespace Homon.Infrastructure.Email;

/// <summary>
/// The transport could not deliver a message. Callers decide whether that is fatal; an
/// alert that could not be mailed is logged and the poll that raised it still stands.
/// </summary>
public sealed class EmailSendException : Exception
{
    public EmailSendException()
    {
    }

    public EmailSendException(string message)
        : base(message)
    {
    }

    public EmailSendException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
