using System;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace Noxygen;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App()
    {
        this.InitializeComponent();

        // Catch ALL unhandled background thread and XAML exceptions
        this.UnhandledException += async (sender, e) =>
        {
            // 1. Prevent the immediate crash so you can read the output
            e.Handled = true;

            // 2. Print exact error details to Visual Studio Output Window (Ctrl + Alt + O)
            Debug.WriteLine("==================================================");
            Debug.WriteLine($"[EXACT ERROR MESSAGE]: {e.Exception?.Message}");
            Debug.WriteLine($"[EXCEPTION TYPE]: {e.Exception?.GetType().FullName}");
            Debug.WriteLine($"[STACK TRACE]:\n{e.Exception?.StackTrace}");
            Debug.WriteLine("==================================================");

            // 3. Show a UI Dialog Box with the exact error details
            if (MainWindow?.Content is FrameworkElement root)
            {
                var dialog = new ContentDialog
                {
                    Title = "Scan Crash Caught!",
                    Content = $"Error: {e.Exception?.Message}\n\nType: {e.Exception?.GetType().Name}",
                    CloseButtonText = "OK",
                    XamlRoot = root.XamlRoot
                };

                try
                {
                    await dialog.ShowAsync();
                }
                catch
                {
                    // Fallback if XamlRoot is not ready
                }
            }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        ApplyThemeOnStartup();
        MainWindow.Activate();
    }

    private void ApplyThemeOnStartup()
    {
        var localSettings = ApplicationData.Current.LocalSettings;
        if (localSettings.Values.TryGetValue("AppSelectedTheme", out var savedTheme))
        {
            int themeIndex = (int)savedTheme;
            if (MainWindow?.Content is FrameworkElement rootElement)
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
}