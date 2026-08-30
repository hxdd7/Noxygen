using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Noxygen.Models;
using Noxygen.Services;
using Noxygen.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;
using Windows.UI;
using WinRT.Interop;

namespace Noxygen;

public partial class MainWindow : Window
{
    private readonly GameDataService _dataService = new();
    private bool _isWindowVisible = true;
    private MenuFlyoutItem? _toggleMenuItem;
    private FontIcon? _toggleMenuIcon;
    private List<Game> _cachedGames = new();

    public MainWindow()
    {
        this.InitializeComponent();

        ExtendsContentIntoTitleBar = true;

        SetTitleBar(AppTitleBar);
        MaximizeWindow();
        RefreshGlobalGamesCache();

        if (this.Content is FrameworkElement rootElement)
        {
            rootElement.Loaded += (s, e) => ApplySavedTheme(rootElement);
        }
    }

    private void MaximizeWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private void TitleBarBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void TitleBarPaneToggleButton_Click(object sender, RoutedEventArgs e)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        TitleBarBackButton.Visibility = (ContentFrame.CanGoBack && ContentFrame.BackStackDepth > 0)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // 1. Find the Library menu item by Tag
        var libraryItem = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "LibraryPage");

        if (libraryItem != null)
        {
            // 2. Setting SelectedItem automatically draws the accent indicator bar
            NavView.SelectedItem = libraryItem;

            // 3. Navigate to the page
            ContentFrame.Navigate(typeof(Views.LibraryPage));

            // 4. Ensure back stack starts clean without showing back button on boot
            ContentFrame.BackStack.Clear();
            TitleBarBackButton.Visibility = Visibility.Collapsed;
        }
    }

    public void RefreshGlobalGamesCache()
    {
        _cachedGames = _dataService.LoadGamesSync();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavigateToPage(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            Type? pageType = item.Tag?.ToString() switch
            {
                "LibraryPage" => typeof(LibraryPage),
                _ => null
            };

            if (pageType != null)
            {
                NavigateToPage(pageType);
            }
        }
    }

    private void NavigateToPage(Type pageType)
    {
        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    #region Global Search Handlers (Dropdown Only)
    private void NavSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            string query = sender.Text.Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                sender.ItemsSource = null;
                return;
            }

            // Always pull fresh cache in case games were newly scanned
            if (_cachedGames.Count == 0)
            {
                RefreshGlobalGamesCache();
            }

            // Populates strictly the dropdown menu (up to 8 results)
            sender.ItemsSource = _cachedGames
                .Where(g => g.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(g => g.Title)
                .Take(8)
                .ToList();
        }
    }

    private void NavSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string chosenTitle)
        {
            sender.Text = chosenTitle;
        }
    }

    private void NavSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // Here you can trigger actions when a dropdown item is clicked (e.g., launch game, open detail dialog)
    }
    #endregion

    private void ApplySavedTheme(FrameworkElement rootElement)
    {
        var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
        if (localSettings.Values.TryGetValue("AppSelectedTheme", out var savedTheme))
        {
            int themeIndex = (int)savedTheme;

            // Set XAML Theme
            rootElement.RequestedTheme = themeIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            // Synchronize Native Title Bar Caption Buttons
            UpdateTitleBarTheme(rootElement.ActualTheme);
        }
    }

    public void UpdateTitleBarTheme(ElementTheme currentTheme)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        if (appWindow != null && appWindow.TitleBar != null)
        {
            bool isDark = currentTheme == ElementTheme.Dark ||
                         (currentTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

            // Set button background to transparent so it blends with your title bar
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            // Adjust button icon color based on active theme
            if (isDark)
            {
                appWindow.TitleBar.ButtonForegroundColor = Colors.White;
                appWindow.TitleBar.ButtonHoverForegroundColor = Colors.White;
                appWindow.TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(20, 255, 255, 255);
            }
            else
            {
                appWindow.TitleBar.ButtonForegroundColor = Colors.Black;
                appWindow.TitleBar.ButtonHoverForegroundColor = Colors.Black;
                appWindow.TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(20, 0, 0, 0);
            }
        }
    }
}