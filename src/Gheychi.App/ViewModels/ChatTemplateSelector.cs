using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Gheychi.App.ViewModels;

public enum ChatRowKind
{
    Incoming,
    IncomingLink,
    Outgoing,
    OutgoingLink,
    DateSeparator,
    UnreadSeparator
}

/// <summary>
/// Picks the row template of a chat item. The message list uses it as a row factory (see ChatList): a row built for a
/// kind is bound to another item of the same kind instead of being rebuilt, so scrolling only re-binds.
/// </summary>
public sealed class ChatTemplateSelector : IDataTemplate
{
    private static readonly object[] Keys = Enum.GetValues<ChatRowKind>().Select(kind => (object)kind).ToArray();

    public IDataTemplate? IncomingTemplate { get; set; }
    public IDataTemplate? IncomingLinkTemplate { get; set; }
    public IDataTemplate? OutgoingTemplate { get; set; }
    public IDataTemplate? OutgoingLinkTemplate { get; set; }
    public IDataTemplate? DateSeparatorTemplate { get; set; }
    public IDataTemplate? UnreadSeparatorTemplate { get; set; }

    // A message's link never changes after it is parsed, so link rows get their own template instead
    // of every row carrying a hidden formatted-text label.
    public static ChatRowKind KindOf(object? item) =>
        item switch
        {
            UnreadSeparatorItem => ChatRowKind.UnreadSeparator,
            DateSeparatorItem => ChatRowKind.DateSeparator,
            ChatMessage { IsOutgoing: true, HasLink: true } => ChatRowKind.OutgoingLink,
            ChatMessage { IsOutgoing: true } => ChatRowKind.Outgoing,
            ChatMessage { HasLink: true } => ChatRowKind.IncomingLink,
            _ => ChatRowKind.Incoming
        };

    /// <summary>The pool key of a kind: rows are only recycled between items that share a template.</summary>
    public static object KeyOf(object? item) => Keys[(int)KindOf(item)];

    public bool Match(object? data) => data is ChatMessage or DateSeparatorItem or UnreadSeparatorItem;

    public Control? Build(object? param)
    {
        var row = BuildRow(KindOf(param));
        row.DataContext = param;
        return row;
    }

    public Control BuildRow(ChatRowKind kind)
    {
        var template = kind switch
        {
            ChatRowKind.UnreadSeparator => UnreadSeparatorTemplate ?? throw new InvalidOperationException(nameof(UnreadSeparatorTemplate)),
            ChatRowKind.DateSeparator => DateSeparatorTemplate ?? throw new InvalidOperationException(nameof(DateSeparatorTemplate)),
            ChatRowKind.OutgoingLink => OutgoingLinkTemplate ?? throw new InvalidOperationException(nameof(OutgoingLinkTemplate)),
            ChatRowKind.Outgoing => OutgoingTemplate ?? throw new InvalidOperationException(nameof(OutgoingTemplate)),
            ChatRowKind.IncomingLink => IncomingLinkTemplate ?? throw new InvalidOperationException(nameof(IncomingLinkTemplate)),
            _ => IncomingTemplate ?? throw new InvalidOperationException(nameof(IncomingTemplate))
        };

        return template.Build(null) ?? throw new InvalidOperationException($"{kind} template built nothing");
    }
}
