using Microsoft.UI.Xaml.Controls;
using Noxygen.Models;

namespace Noxygen.Views;

public sealed partial class EditGameDialog : ContentDialog
{
    private readonly Game _targetGame;

    public EditGameDialog(Game game)
    {
        this.InitializeComponent();
        _targetGame = game;

        // Populate locked fields
        TitleTextBox.Text = game.Title;
        ExePathTextBox.Text = string.IsNullOrEmpty(game.ExecutablePath) ? game.InstallPath : game.ExecutablePath;

        // Populate editable metadata
        CompanyTextBox.Text = game.Company;
        CoverArtTextBox.Text = game.CoverArtUrl;
        BackgroundUrlTextBox.Text = game.BackgroundUrl;
        IconUrlTextBox.Text = game.IconUrl;
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Save ONLY allowed metadata properties (Title and Executable/Install paths are unmodified)
        _targetGame.Company = CompanyTextBox.Text.Trim();
        _targetGame.CoverArtUrl = CoverArtTextBox.Text.Trim();
        _targetGame.BackgroundUrl = BackgroundUrlTextBox.Text.Trim();
        _targetGame.IconUrl = IconUrlTextBox.Text.Trim();
    }
}