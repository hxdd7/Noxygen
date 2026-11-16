using CommunityToolkit.Mvvm.ComponentModel;
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
using Noxygen.ViewModels;
using Noxygen.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.Storage;
using Windows.UI;
using Windows.UI.StartScreen;
using WinRT.Interop;

namespace Noxygen;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();
    private readonly GameDataService _dataService = new();
    private bool _isWindowVisible = true;
    private MenuFlyoutItem? _toggleMenuItem;
    private FontIcon? _toggleMenuIcon;
    private List<Game> _cachedGames = new();

    // Dynamic game sidebar tab
    private NavigationViewItem? _dynamicGameNavItem;

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

        // Auto-remove dynamic tab if navigating away from GameDetailsPage
        if (e.SourcePageType == typeof(LibraryPage) || e.SourcePageType == typeof(SettingsPage))
        {
            RemoveGameDetailsTab();
        }
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        SyncPinnedSidebarItems();

        var libraryItem = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "LibraryPage");

        if (libraryItem != null)
        {
            NavView.SelectedItem = libraryItem;
            ContentFrame.Navigate(typeof(Views.LibraryPage));
            ContentFrame.BackStack.Clear();
            TitleBarBackButton.Visibility = Visibility.Collapsed;
        }
    }

    public void RefreshGlobalGamesCache()
    {
        _cachedGames = _dataService.LoadGamesSync();
        ViewModel.RefreshPinnedGames(_cachedGames);

        if (NavView != null)
        {
            SyncPinnedSidebarItems();
        }
    }

    /// <summary>
    /// Creates and shows the dynamic navigation tab with game title and custom cover image icon.
    /// </summary>
    public void OpenGameDetailsTab(Game game)
    {
        if (game == null) return;

        // Remove existing item if present
        if (_dynamicGameNavItem != null)
        {
            NavView.MenuItems.Remove(_dynamicGameNavItem);
            _dynamicGameNavItem = null;
        }

        // Build image icon
        IconElement iconElement;
        string imagePath = !string.IsNullOrWhiteSpace(game.CoverArtUrl) ? game.CoverArtUrl : game.BackgroundUrl;

        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            iconElement = new ImageIcon
            {
                Source = new BitmapImage(new Uri(imagePath))
            };
        }
        else
        {
            iconElement = new FontIcon { Glyph = "\uE7FC" };
        }

        // Create item
        _dynamicGameNavItem = new NavigationViewItem
        {
            Content = game.Title,
            Icon = iconElement,
            Tag = game
        };

        // Add item to menu
        NavView.MenuItems.Add(_dynamicGameNavItem);

        // Set selection & Navigate
        NavView.SelectedItem = _dynamicGameNavItem;
        ContentFrame.Navigate(typeof(GameDetailsPage), game);
    }

    /// <summary>
    /// Removes the dynamic game tab from sidebar menu.
    /// </summary>
    public void RemoveGameDetailsTab()
    {
        if (_dynamicGameNavItem != null)
        {
            NavView.MenuItems.Remove(_dynamicGameNavItem);
            _dynamicGameNavItem = null;
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            RemoveGameDetailsTab();
            NavigateToPage(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            // Handle both pinned games and the temporary dynamic game tab
            if (item.Tag is Game game)
            {
                if (item != _dynamicGameNavItem)
                {
                    RemoveGameDetailsTab();
                }

                ContentFrame.Navigate(typeof(GameDetailsPage), game);
                return;
            }

            Type? pageType = item.Tag?.ToString() switch
            {
                "LibraryPage" => typeof(LibraryPage),
                _ => null
            };

            if (pageType != null)
            {
                RemoveGameDetailsTab();
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

    #region Global Search Handlers
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

            if (_cachedGames.Count == 0)
            {
                RefreshGlobalGamesCache();
            }

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
        if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
            var matchedGame = _cachedGames.FirstOrDefault(g =>
                g.Title.Equals(args.QueryText, StringComparison.OrdinalIgnoreCase));

            if (matchedGame != null)
            {
                OpenGameDetailsTab(matchedGame);
            }
        }
    }
    #endregion

    private void ApplySavedTheme(FrameworkElement rootElement)
    {
        var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
        if (localSettings.Values.TryGetValue("AppSelectedTheme", out var savedTheme))
        {
            int themeIndex = (int)savedTheme;

            rootElement.RequestedTheme = themeIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

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

            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

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

    private void SyncPinnedSidebarItems()
    {
        if (PinnedGamesHeader == null) return;

        // 1. Remove old pinned game items (excluding the temporary dynamic tab)
        var oldItems = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .Where(i => i.Tag is Game && i != _dynamicGameNavItem)
            .ToList();

        foreach (var item in oldItems)
        {
            NavView.MenuItems.Remove(item);
        }

        // 2. Toggle subtitle header visibility
        PinnedGamesHeader.Visibility = ViewModel.HasPinnedGames ? Visibility.Visible : Visibility.Collapsed;

        // 3. Populate updated pinned games with the default Game icon
        foreach (var game in ViewModel.PinnedGames)
        {
            var navItem = new NavigationViewItem
            {
                Content = game.Title,
                Tag = game,
                Icon = new FontIcon { Glyph = "\uE7FC" }
            };

            // Insert right after PinnedGamesHeader so temporary dynamic tabs stay at the bottom
            int headerIndex = NavView.MenuItems.IndexOf(PinnedGamesHeader);
            if (headerIndex >= 0)
            {
                NavView.MenuItems.Insert(headerIndex + 1, navItem);
            }
            else
            {
                NavView.MenuItems.Add(navItem);
            }
        }
        
        // 4. Sync the Windows taskbar right-click Jump List
        UpdateTaskbarJumpList();
    }

    public async void UpdateTaskbarJumpList()
    {
        if (!JumpList.IsSupported()) return;

        try
        {
            var jumpList = await JumpList.LoadCurrentAsync();
            jumpList.SystemGroupKind = JumpListSystemGroupKind.None;
            jumpList.Items.Clear();

            // 1. Required anchor in the default "Tasks" section
            var libraryTask = JumpListItem.CreateWithArguments("--open-library", "Open Library");
            libraryTask.Description = "Open Noxygen Library";
            libraryTask.Logo = new Uri("ms-appx:///Assets/Noxygen.png");
            jumpList.Items.Add(libraryTask);

            // 2. Read saved mode (0 = Pinned Games, 1 = Recent Games)
            int jumpListMode = 0;
            var localSettings = ApplicationData.Current.LocalSettings;
            if (localSettings.Values.TryGetValue("JumpListDisplayMode", out var modeObj) && modeObj is int modeVal)
            {
                jumpListMode = modeVal;
            }

            if (jumpListMode == 1)
            {
                var recentTitles = RecentGamesService.GetRecentGames().Take(3);

                foreach (var title in recentTitles)
                {
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    // Check if the recent game is also in PinnedGames to grab its custom icon if available
                    var matchingGame = ViewModel.PinnedGames.FirstOrDefault(g => string.Equals(g.Title, title, StringComparison.OrdinalIgnoreCase));

                    var item = JumpListItem.CreateWithArguments($"--launch-game \"{title}\"", title);
                    item.Description = $"Launch Recent: {title}";
                    item.Logo = GetGameIconUri(matchingGame?.IconUrl);
                    jumpList.Items.Add(item);
                }
            }
            else
            {
                foreach (var game in ViewModel.PinnedGames)
                {
                    if (game == null || string.IsNullOrWhiteSpace(game.Title)) continue;

                    var item = JumpListItem.CreateWithArguments($"--launch-game \"{game.Title}\"", game.Title);
                    item.Description = $"Launch {game.Title}";
                    item.Logo = GetGameIconUri(game.IconUrl);
                    jumpList.Items.Add(item);
                }
            }

            await jumpList.SaveAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[JumpList] ERROR: {ex.Message}");
        }
    }

    private Uri GetGameIconUri(string? iconPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                // 1. If it's already a valid ms-appx or ms-appdata URI, use it directly
                if (Uri.TryCreate(iconPath, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "ms-appx" || uri.Scheme == "ms-appdata"))
                {
                    return uri;
                }

                // 2. If it's a local file path anywhere on disk
                if (System.IO.File.Exists(iconPath))
                {
                    string localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;

                    if (iconPath.StartsWith(localFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        string relativePath = iconPath.Substring(localFolder.Length).TrimStart('\\', '/');
                        return new Uri($"ms-appdata:///local/{relativePath.Replace('\\', '/')}");
                    }
                    else
                    {
                        string fileName = System.IO.Path.GetFileName(iconPath);
                        string destPath = System.IO.Path.Combine(localFolder, "Cache", fileName);

                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destPath)!);
                        System.IO.File.Copy(iconPath, destPath, true);

                        return new Uri($"ms-appdata:///local/Cache/{fileName}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[JumpList Icon Error] {ex.Message}");
        }

        // Fallback: If no custom icon URL is set, use the default app logo (Noxygen.png)
        return new Uri("ms-appx:///Assets/Noxygen.png");
    }
}