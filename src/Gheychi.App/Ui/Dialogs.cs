namespace Gheychi.App.Ui;

/// <summary>Questions and lists, shown as the app's own <see cref="Sheet"/>s, as MAUI's DisplayAlert and DisplayActionSheet did.</summary>
public static class Dialogs
{
    /// <summary>True when the user chose <paramref name="accept"/>. Without a <paramref name="cancel"/> text it is a plain notice.</summary>
    public static Task<bool> AlertAsync(string title, string message, string accept, string? cancel = null, bool danger = false) =>
        Sheet.AlertAsync(title, message, accept, cancel, danger);

    /// <summary>The chosen button's text, <paramref name="cancel"/> when the user backed out.</summary>
    public static Task<string?> ActionSheetAsync(string? title, string cancel, string? destruction, params string[] buttons) =>
        Sheet.ListAsync(title, cancel, destruction, buttons);
}
