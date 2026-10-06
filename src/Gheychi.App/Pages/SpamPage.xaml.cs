using Gheychi.App.Controls;
using Gheychi.App.Localization;
using Gheychi.App.ViewModels;

namespace Gheychi.App.Pages;

public partial class SpamPage : ContentPage
{
    private SpamViewModel? Vm => BindingContext as SpamViewModel;

    public SpamPage()
    {
        InitializeComponent();
        ListTuning.UseFixedSize(SpamList);

        if (IPlatformApplication.Current?.Services.GetService<SpamViewModel>() is { } viewModel)
        {
            BindingContext = viewModel;
            _ = viewModel.EnsureLoadedAsync();
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (Overlay.HandleBack())
            return true;

        if (!SearchBar.IsVisible)
            return base.OnBackButtonPressed();

        CloseSearch();
        return true;
    }

    private void OnSearchTapped(object? sender, TappedEventArgs e)
    {
        AppHeader.IsVisible = false;
        SearchBar.IsVisible = true;
        SearchEntry.Focus();
    }

    private void OnSearchCloseTapped(object? sender, TappedEventArgs e) => CloseSearch();

    private void CloseSearch()
    {
        SearchEntry.Unfocus();
        if (Vm is not null)
            Vm.SearchText = string.Empty;
        SearchBar.IsVisible = false;
        AppHeader.IsVisible = true;
    }

    private void OnMoreTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is SpamItem item)
        {
            SearchEntry.Unfocus();
            Overlay.ShowMenu(item);
        }
    }

    private async void OnClearAllTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var loc = LocalizationManager.Instance;
            if (Vm is not null && await Overlay.ConfirmAsync(loc["Spam_ClearAllTitle"], loc["Spam_ClearAllMessage"], loc["Spam_ClearAll"]))
                await Vm.ClearAllAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clear all failed: {ex}");
        }
    }
}
