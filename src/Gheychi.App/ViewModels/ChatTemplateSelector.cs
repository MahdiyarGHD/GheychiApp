using Microsoft.Maui.Controls;

namespace Gheychi.App.ViewModels;

public sealed class ChatTemplateSelector : DataTemplateSelector
{
    public DataTemplate? IncomingTemplate { get; set; }
    public DataTemplate? OutgoingTemplate { get; set; }
    public DataTemplate? DateSeparatorTemplate { get; set; }
    public DataTemplate? UnreadSeparatorTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
        item switch
        {
            UnreadSeparatorItem => UnreadSeparatorTemplate ?? throw new InvalidOperationException(nameof(UnreadSeparatorTemplate)),
            DateSeparatorItem => DateSeparatorTemplate ?? throw new InvalidOperationException(nameof(DateSeparatorTemplate)),
            ChatMessage { IsOutgoing: true } => OutgoingTemplate ?? throw new InvalidOperationException(nameof(OutgoingTemplate)),
            _ => IncomingTemplate ?? throw new InvalidOperationException(nameof(IncomingTemplate))
        };
}
