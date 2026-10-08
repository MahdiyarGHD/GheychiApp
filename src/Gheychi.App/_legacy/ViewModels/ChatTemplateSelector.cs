using Microsoft.Maui.Controls;

namespace Gheychi.App.ViewModels;

public sealed class ChatTemplateSelector : DataTemplateSelector
{
    public DataTemplate? IncomingTemplate { get; set; }
    public DataTemplate? IncomingLinkTemplate { get; set; }
    public DataTemplate? OutgoingTemplate { get; set; }
    public DataTemplate? OutgoingLinkTemplate { get; set; }
    public DataTemplate? DateSeparatorTemplate { get; set; }
    public DataTemplate? UnreadSeparatorTemplate { get; set; }

    // A message's link never changes after it is parsed, so link rows get their own template instead
    // of every row carrying a hidden formatted-text label.
    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
        item switch
        {
            UnreadSeparatorItem => UnreadSeparatorTemplate ?? throw new InvalidOperationException(nameof(UnreadSeparatorTemplate)),
            DateSeparatorItem => DateSeparatorTemplate ?? throw new InvalidOperationException(nameof(DateSeparatorTemplate)),
            ChatMessage { IsOutgoing: true, HasLink: true } => OutgoingLinkTemplate ?? throw new InvalidOperationException(nameof(OutgoingLinkTemplate)),
            ChatMessage { IsOutgoing: true } => OutgoingTemplate ?? throw new InvalidOperationException(nameof(OutgoingTemplate)),
            ChatMessage { HasLink: true } => IncomingLinkTemplate ?? throw new InvalidOperationException(nameof(IncomingLinkTemplate)),
            _ => IncomingTemplate ?? throw new InvalidOperationException(nameof(IncomingTemplate))
        };
}
