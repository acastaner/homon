namespace Homon.Domain.Messaging;

/// <summary>
/// What a reporter claims about the run it is reporting. A closed vocabulary, unlike
/// <see cref="Message.Category"/>: the dashboard has to colour it and plan 009 has to decide
/// whether to mail about it, so it cannot be free text. All five members ship together —
/// the value is persisted as its name (see <c>MessageConfiguration</c>), so a later addition
/// renumbers nothing, but a reporter in the field should never meet a status Homon rejects.
/// </summary>
public enum MessageStatus
{
    /// <summary>No claim either way. The reporter checked in; that is all it is saying.</summary>
    None,

    /// <summary>The run finished and did what it set out to do.</summary>
    Success,

    /// <summary>The run finished, but something about it deserves a look.</summary>
    Warning,

    /// <summary>The run did not do what it set out to do.</summary>
    Failure,

    /// <summary>The reporter ran but cannot tell whether it succeeded.</summary>
    Unknown,
}
