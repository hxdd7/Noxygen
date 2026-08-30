using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Noxygen.Controls;

public sealed partial class GameCardControl : UserControl
{
    public static readonly DependencyProperty GameTitleProperty =
        DependencyProperty.Register(
            nameof(GameTitle),
            typeof(string),
            typeof(GameCardControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LauncherNameProperty =
        DependencyProperty.Register(
            nameof(LauncherName),
            typeof(string),
            typeof(GameCardControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty InstalledSizeGbProperty =
        DependencyProperty.Register(
            nameof(InstalledSizeGb),
            typeof(double),
            typeof(GameCardControl),
            new PropertyMetadata(0.0));

    public string GameTitle
    {
        get => (string)GetValue(GameTitleProperty);
        set => SetValue(GameTitleProperty, value);
    }

    public string LauncherName
    {
        get => (string)GetValue(LauncherNameProperty);
        set => SetValue(LauncherNameProperty, value);
    }

    public double InstalledSizeGb
    {
        get => (double)GetValue(InstalledSizeGbProperty);
        set => SetValue(InstalledSizeGbProperty, value);
    }

    public string SizeFormatted => $"{InstalledSizeGb:F2} GB";

    public GameCardControl()
    {
        this.InitializeComponent();
    }
}