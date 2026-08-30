using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace Noxygen.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string SelectedThemeKey = "AppSelectedTheme";

    [ObservableProperty]
    private int _selectedThemeIndex;

    public SettingsViewModel()
    {
        var localSettings = ApplicationData.Current.LocalSettings;

        // Load saved theme preference (0 = Default, 1 = Light, 2 = Dark)
        if (localSettings.Values.TryGetValue(SelectedThemeKey, out var savedTheme))
        {
            _selectedThemeIndex = (int)savedTheme;
        }
        else
        {
            _selectedThemeIndex = 0;
        }
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {// Save choice to local settings
        ApplicationData.Current.LocalSettings.Values[SelectedThemeKey] = value;

        if (App.MainWindow is MainWindow mainWindow && mainWindow.Content is FrameworkElement rootElement)
        {
            ElementTheme newTheme = value switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            rootElement.RequestedTheme = newTheme;

            // Update the native caption buttons to match the new theme
            mainWindow.UpdateTitleBarTheme(rootElement.ActualTheme);
        }
    }

    public static void ApplyTheme(int themeIndex)
    {
        if (App.MainWindow?.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = themeIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }
}