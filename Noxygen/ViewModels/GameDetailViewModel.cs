using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Noxygen.Models;
using Noxygen.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Noxygen.ViewModels;

public partial class GameDetailViewModel : ObservableObject
{
    private const int DefaultSessionLimit = 3;
    private const int ExpandedSessionLimit = 10;

    private readonly PlaytimeTrackerService _trackerService = new();
    private readonly List<GameSessionDetailItem> _allSessionsCache = new();
    private string _notesStoragePath = string.Empty;
    private Dictionary<string, string> _notesByGameKey = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private Game? _game;

    [ObservableProperty]
    private ImageSource? _coverArtSource;

    [ObservableProperty]
    private string _headerSubtitleText = "Not specified";

    [ObservableProperty]
    private string _descriptionText = "No description available for this game.";

    [ObservableProperty]
    private string _totalPlaytimeFormatted = "No playtime yet";

    [ObservableProperty]
    private int _sessionsCount;

    [ObservableProperty]
    private string _averageSessionFormatted = "—";

    [ObservableProperty]
    private string _longestSessionFormatted = "—";

    [ObservableProperty]
    private string _lastPlayedFormatted = "Never played";

    [ObservableProperty]
    private string _mostActiveDayFormatted = "No activity yet";

    [ObservableProperty]
    private string _totalDaysPlayedFormatted = "0 days";

    [ObservableProperty]
    private string _activityScaleMaxText = "2 hrs";

    [ObservableProperty]
    private string _activityScaleMidText = "1 hr";

    [ObservableProperty]
    private string _activityScaleLowText = "0.5 hrs";

    [ObservableProperty]
    private string _developerText = "Not specified";

    [ObservableProperty]
    private string _genreText = "Not specified";

    [ObservableProperty]
    private string _gameVersionText = "Not specified";

    [ObservableProperty]
    private string _installationText = "Not specified";

    [ObservableProperty]
    private string _personalNotes = string.Empty;

    [ObservableProperty]
    private bool _hasSessions;

    [ObservableProperty]
    private bool _isSessionsEmpty = true;

    [ObservableProperty]
    private bool _isActivityEmpty = true;

    [ObservableProperty]
    private bool _isExpandedToTen;

    [ObservableProperty]
    private bool _canToggleSessions;

    [ObservableProperty]
    private string _sessionToggleText = "Show more (up to 10)";

    public ObservableCollection<GameDayBarItem> LastSevenDaysBars { get; } = new();
    public ObservableCollection<GameSessionDetailItem> VisibleSessions { get; } = new();

    public GameDetailViewModel()
    {
        InitializeNotesStore();
        for (int i = 0; i < 7; i++)
        {
            LastSevenDaysBars.Add(new GameDayBarItem
            {
                Label = string.Empty,
                Minutes = 0,
                BarHeight = 0,
                TooltipText = "0 mins"
            });
        }
    }

    partial void OnPersonalNotesChanged(string value)
    {
        if (Game == null) return;
        string key = GetGameKey(Game);
        _notesByGameKey[key] = value ?? string.Empty;
        SaveNotesStore();
    }

    public void ToggleSessionHistoryLimit()
    {
        IsExpandedToTen = !IsExpandedToTen;
        RefreshVisibleSessions();
    }

    public async Task LoadGameAsync(Game game)
    {
        Game = game ?? throw new ArgumentNullException(nameof(game));
        IsExpandedToTen = false;

        CoverArtSource = CreateSafeImageSource(game.CoverArtUrl);

        DeveloperText = string.IsNullOrWhiteSpace(game.Company) ? "Not specified" : game.Company.Trim();
        GenreText = "Not specified";
        GameVersionText = game.InstalledSizeGb > 0 ? $"{game.InstalledSizeGb:0.##} GB" : "Not specified";
        InstallationText = string.IsNullOrWhiteSpace(game.Launcher) ? "Not specified" : game.Launcher.Trim();
        DescriptionText = string.IsNullOrWhiteSpace(game.Description) ? "No description available for this game." : game.Description.Trim();

        HeaderSubtitleText = DeveloperText;

        string noteKey = GetGameKey(game);
        PersonalNotes = _notesByGameKey.TryGetValue(noteKey, out var savedNote) ? savedNote : string.Empty;

        var allSessions = await _trackerService.LoadSessionsAsync() ?? new List<PlaytimeSession>();
        var gameSessions = allSessions
            .Where(s => s != null && s.Duration.TotalMinutes >= 1 &&
                       string.Equals(s.GameTitle?.Trim(), game.Title?.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.StartTime)
            .ToList();

        PopulateStatistics(gameSessions);
        PopulateLastSevenDaysChart(gameSessions);
        PopulateSessions(gameSessions);
    }

    private static ImageSource? CreateSafeImageSource(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            if (Uri.TryCreate(url.Trim(), UriKind.RelativeOrAbsolute, out var uri))
            {
                return new BitmapImage(uri);
            }

            if (File.Exists(url.Trim()))
            {
                return new BitmapImage(new Uri(Path.GetFullPath(url.Trim())));
            }
        }
        catch
        {
            // Ignore malformed image paths/URIs so missing metadata doesn't crash the page
        }

        return null;
    }

    private void PopulateStatistics(List<PlaytimeSession> sessions)
    {
        SessionsCount = sessions.Count;

        if (sessions.Count == 0)
        {
            TotalPlaytimeFormatted = "No playtime yet";
            AverageSessionFormatted = "—";
            LongestSessionFormatted = "—";
            LastPlayedFormatted = "Never played";
            MostActiveDayFormatted = "No activity yet";
            TotalDaysPlayedFormatted = "0 days";
            return;
        }

        double totalMins = sessions.Sum(s => s.Duration.TotalMinutes);
        double avgMins = totalMins / sessions.Count;
        double maxMins = sessions.Max(s => s.Duration.TotalMinutes);

        TotalPlaytimeFormatted = FormatShortDuration(totalMins);
        AverageSessionFormatted = FormatShortDuration(avgMins);
        LongestSessionFormatted = FormatShortDuration(maxMins);

        DateTime latestDate = sessions.Max(s => s.StartTime.Date);
        LastPlayedFormatted = FormatRelativeDay(latestDate);

        int distinctDays = sessions.Select(s => s.StartTime.Date).Distinct().Count();
        TotalDaysPlayedFormatted = distinctDays == 1 ? "1 day" : $"{distinctDays} days";

        var topDay = sessions
            .GroupBy(s => s.StartTime.DayOfWeek)
            .Select(g => new { Day = g.Key, Minutes = g.Sum(s => s.Duration.TotalMinutes) })
            .OrderByDescending(x => x.Minutes)
            .FirstOrDefault();

        MostActiveDayFormatted = topDay != null
            ? CultureInfo.InvariantCulture.DateTimeFormat.GetDayName(topDay.Day)
            : "No activity yet";
    }

    private void PopulateLastSevenDaysChart(List<PlaytimeSession> sessions)
    {
        DateTime today = DateTime.Today;
        var days = Enumerable.Range(0, 7)
            .Select(offset => today.AddDays(-6 + offset))
            .ToList();

        var minutesByDay = sessions
            .Where(s => s.StartTime.Date >= days[0] && s.StartTime.Date <= today)
            .GroupBy(s => s.StartTime.Date)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Duration.TotalMinutes));

        double maxMins = Math.Max(120.0, days.Max(d => minutesByDay.TryGetValue(d, out var m) ? m : 0));
        double maxHours = maxMins / 60.0;

        ActivityScaleMaxText = $"{Math.Round(maxHours, 1)} hrs";
        ActivityScaleMidText = $"{Math.Round(maxHours * 0.5, 1)} hrs";
        ActivityScaleLowText = $"{Math.Round(maxHours * 0.25, 1)} hrs";

        const double maxBarHeight = 148.0;

        while (LastSevenDaysBars.Count < 7)
        {
            LastSevenDaysBars.Add(new GameDayBarItem());
        }

        bool anyBarHasMinutes = false;

        for (int i = 0; i < 7; i++)
        {
            DateTime date = days[i];
            double mins = minutesByDay.TryGetValue(date, out var m) ? m : 0;
            if (mins > 0) anyBarHasMinutes = true;

            double height = mins > 0 ? Math.Max(8.0, (mins / maxMins) * maxBarHeight) : 0.0;
            string dayName = date.ToString("ddd", CultureInfo.InvariantCulture);

            var bar = LastSevenDaysBars[i];
            bar.Label = dayName[..Math.Min(3, dayName.Length)];
            bar.Minutes = mins;
            bar.BarHeight = height;
            bar.TooltipText = $"{FormatRelativeDay(date)}: {FormatShortDuration(mins)}";
        }

        IsActivityEmpty = !anyBarHasMinutes;
    }

    private void PopulateSessions(List<PlaytimeSession> sessions)
    {
        _allSessionsCache.Clear();

        foreach (var session in sessions.Take(ExpandedSessionLimit))
        {
            DateTime start = session.StartTime;
            DateTime end = start + session.Duration;

            _allSessionsCache.Add(new GameSessionDetailItem
            {
                DayLabel = FormatRelativeDay(start.Date),
                TimeWindowText = $"{start:h:mm tt} – {end:h:mm tt}",
                DurationFormatted = FormatShortDuration(session.Duration.TotalMinutes)
            });
        }

        HasSessions = _allSessionsCache.Count > 0;
        IsSessionsEmpty = !HasSessions;
        CanToggleSessions = _allSessionsCache.Count > DefaultSessionLimit;

        RefreshVisibleSessions();
    }

    private void RefreshVisibleSessions()
    {
        int limit = IsExpandedToTen ? ExpandedSessionLimit : DefaultSessionLimit;
        VisibleSessions.Clear();

        foreach (var item in _allSessionsCache.Take(limit))
        {
            VisibleSessions.Add(item);
        }

        SessionToggleText = IsExpandedToTen ? "Show less" : "Show more (up to 10)";
    }

    private static string FormatRelativeDay(DateTime date)
    {
        if (date == DateTime.Today) return "Today";
        if (date == DateTime.Today.AddDays(-1)) return "Yesterday";
        if (date >= DateTime.Today.AddDays(-6))
        {
            return date.ToString("dddd", CultureInfo.InvariantCulture);
        }
        return date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }

    public static string FormatShortDuration(double totalMinutes)
    {
        if (totalMinutes <= 0) return "No playtime yet";
        if (totalMinutes < 60) return $"{Math.Max(1, Math.Round(totalMinutes))} mins";

        double hours = totalMinutes / 60.0;
        int wholeHours = (int)Math.Floor(hours);
        int remMins = (int)Math.Round((hours - wholeHours) * 60);

        if (remMins == 60)
        {
            wholeHours++;
            remMins = 0;
        }

        if (remMins == 0) return $"{wholeHours} {(wholeHours == 1 ? "hr" : "hrs")}";
        if (hours >= 2) return $"{Math.Round(hours, 1)} hrs";
        return $"{wholeHours} hr {remMins} mins";
    }

    private static string GetGameKey(Game game) =>
        !string.IsNullOrWhiteSpace(game.ExecutablePath)
            ? game.ExecutablePath.Trim()
            : (game.Title ?? string.Empty).Trim();

    private void InitializeNotesStore()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "Noxygen");
            Directory.CreateDirectory(folder);
            _notesStoragePath = Path.Combine(folder, "game_notes.json");

            if (File.Exists(_notesStoragePath))
            {
                string json = File.ReadAllText(_notesStoragePath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (loaded != null)
                {
                    _notesByGameKey = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // Ignore note storage errors
        }
    }

    private void SaveNotesStore()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_notesStoragePath)) return;
            string json = JsonSerializer.Serialize(_notesByGameKey);
            File.WriteAllText(_notesStoragePath, json);
        }
        catch
        {
            // Ignore note save errors
        }
    }
}

public partial class GameDayBarItem : ObservableObject
{
    public double Minutes { get; set; }

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _tooltipText = "0 mins";

    [ObservableProperty]
    private double _barHeight;
}

public class GameSessionDetailItem
{
    public string DayLabel { get; set; } = string.Empty;
    public string TimeWindowText { get; set; } = string.Empty;
    public string DurationFormatted { get; set; } = "0 mins";
}