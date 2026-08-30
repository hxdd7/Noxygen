using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Noxygen.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Noxygen.Views;

public sealed partial class AddGameDialog : ContentDialog
{
    public Game? CreatedGame { get; private set; }

    public AddGameDialog()
    {
        this.InitializeComponent();
    }

    private async void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();

        IntPtr hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add(".exe");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            ExePathTextBox.Text = file.Path;

            if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
            {
                TitleTextBox.Text = Path.GetFileNameWithoutExtension(file.Name);
            }
        }
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        bool isValid = true;

        if (string.IsNullOrWhiteSpace(ExePathTextBox.Text))
        {
            ExeErrorText.Visibility = Visibility.Visible;
            isValid = false;
        }
        else
        {
            ExeErrorText.Visibility = Visibility.Collapsed;
        }

        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            TitleErrorText.Visibility = Visibility.Visible;
            isValid = false;
        }
        else
        {
            TitleErrorText.Visibility = Visibility.Collapsed;
        }

        if (!isValid)
        {
            args.Cancel = true;
            return;
        }

        CreatedGame = new Game
        {
            Title = TitleTextBox.Text.Trim(),
            Company = CompanyTextBox.Text.Trim(),
            ExecutablePath = ExePathTextBox.Text.Trim(),
            InstallPath = Path.GetDirectoryName(ExePathTextBox.Text.Trim()) ?? string.Empty,
            Launcher = "Custom",
            CoverArtUrl = CoverArtTextBox.Text.Trim(),
            BackgroundUrl = BackgroundUrlTextBox.Text.Trim(),
            IconUrl = IconUrlTextBox.Text.Trim()
        };
    }
}