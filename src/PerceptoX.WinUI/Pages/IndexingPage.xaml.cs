using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PerceptoX.Presentation.ViewModels;

namespace PerceptoX.WinUI.Pages;

public sealed partial class IndexingPage : Page
{
    public IndexingPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DataContext = e.Parameter as IndexingViewModel;
    }
}
