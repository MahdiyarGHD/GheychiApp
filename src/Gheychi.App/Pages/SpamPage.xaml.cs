using Gheychi.App.Controls;
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

    private async void OnMoreTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if ((sender as BindableObject)?.BindingContext is SpamItem item && Vm is not null)
                await SpamMenu.ShowAsync(item, Vm);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spam menu failed: {ex}");
        }
    }

    private async void OnClearAllTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (Vm is not null)
                await SpamMenu.ConfirmClearAllAsync(Vm);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clear all failed: {ex}");
        }
    }
}
