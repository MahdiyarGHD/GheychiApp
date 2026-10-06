using Gheychi.App.Controls;
using Gheychi.App.ViewModels;

namespace Gheychi.App.Pages;

public partial class SpamPage : ContentPage
{
    public SpamPage()
    {
        InitializeComponent();
        ListTuning.UseFixedSize(SpamList);

        if (IPlatformApplication.Current?.Services.GetService<SpamViewModel>() is { } viewModel)
        {
            BindingContext = viewModel;
            _ = viewModel.LoadAsync();
        }
    }
}
