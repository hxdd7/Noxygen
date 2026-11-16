using CommunityToolkit.Mvvm.ComponentModel;
using System.Runtime.CompilerServices;

namespace Noxygen.Models;

public partial class Game : ObservableObject
{

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinMenuText))]
    private bool _isPinned;

    public string Title { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string SteamId { get; set; } = string.Empty;
    public string AppId
    {
        get => SteamId;
        set => SteamId = value;
    }
    public double InstalledSizeGb { get; set; }
    public string DisplaySize => $"{InstalledSizeGb:F1} GB";
    public string Launcher { get; set; } = "Custom";

    public string Company { get; set; } = string.Empty;
    public string BackgroundUrl { get; set; } = string.Empty;
    public string CoverArtUrl { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PinMenuText => IsPinned ? "Unpin from Sidebar" : "Pin to Sidebar";
}