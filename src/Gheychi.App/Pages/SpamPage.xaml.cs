using Gheychi.App.ViewModels;

namespace Gheychi.App.Pages;

public partial class SpamPage : ContentPage
{
    public SpamPage()
    {
        InitializeComponent();
        BindingContext = new SpamViewModel();
    }
}
