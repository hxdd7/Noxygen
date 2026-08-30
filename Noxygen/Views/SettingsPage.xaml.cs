using Microsoft.UI.Xaml.Controls;
using Noxygen.ViewModels;

namespace Noxygen.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        this.InitializeComponent();
    }
}