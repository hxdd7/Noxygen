using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Noxygen.Models;
using Noxygen.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Noxygen.ViewModels;

public enum StatsTimePeriod
{
    LastWeek = 0,
    LastMonth = 1,
    LastYear = 2,
    AllTime = 3
}

public partial class StatsViewModel : ObservableObject
{
    private readonly PlaytimeTrackerService _trackerService = new();
    private readonly GameDataService _dataService = new();

    private List<PlaytimeSession> _allSessions = new();
    private List<Game> _allGames = new();
    private ElementTheme _currentTheme = ElementTheme.Default;

    [ObservableProperty]
    private StatsTimePeriod _selectedTimePeriod = StatsTimePeriod.LastWeek;

    [ObservableProperty]
    private string _totalPlaytimeFormatted = "0 mins";

    [ObservableProperty]
    private int _totalSessionsCount;

    [ObservableProperty]
    private string _favoriteGameTitle = "None yet";

    [ObservableProperty]
    private string _periodFavoriteGameTitle = "None yet";

    [ObservableProperty]
    private string _heatmapSummaryText = "0 playtime in the last year";

    public ObservableCollection<HeatmapDayCell> HeatmapDays { get; } = new();
    public ObservableCollection<string> HeatmapMonths { get; } = new();
    public ObservableCollection<GamePlayStatItem> TopPlayedGames { get; } = new();

    partial void OnSelectedTimePeriodChanged(StatsTimePeriod value)
    {
        RecalculateStats();
    }

    public async Task InitializeAsync(ElementTheme theme = ElementTheme.Default)
    {
        _currentTheme = theme;
        _allSessions = await _trackerService.LoadSessionsAsync() ?? new List<PlaytimeSession>();
        _allGames = await _dataService.LoadGamesAsync() ?? new List<Game>();

        RecalculateStats();
        BuildRollingLastYearHeatmap();
    }

    public void RefreshHeatmapTheme(ElementTheme theme)
    {
        _currentTheme = theme;
        if (_allSessions.Count > 0 || HeatmapDays.Count > 0)
        {
            BuildRollingLastYearHeatmap();
        }
    }

    private void BuildRollingLastYearHeatmap()
    {
        HeatmapDays.Clear();
        HeatmapMonths.Clear();

        DateTime endDate = DateTime.Today;
        DateTime startDate = endDate.AddYears(-1);

        var rangeSessions = _allSessions
            .Where(s => s != null && s.Duration.TotalMinutes >= 1)
            .Where(s => s.StartTime.Date >= startDate && s.StartTime.Date <= endDate)
            .ToList();

        double totalMinutesInRange = rangeSessions.Sum(s => s.Duration.TotalMinutes);
        HeatmapSummaryText = $"{FormatPlaytime(totalMinutesInRange)} in the last year";

        var minutesByDate = rangeSessions
            .GroupBy(s => s.StartTime.Date)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Duration.TotalMinutes));

        DateTime gridStart = startDate.AddDays(-(int)startDate.DayOfWeek);
        DateTime gridEnd = endDate.AddDays(6 - (int)endDate.DayOfWeek);

        int lastLabeledMonth = -1;
        for (DateTime weekStart = gridStart; weekStart <= gridEnd; weekStart = weekStart.AddDays(7))
        {
            DateTime representativeDay = weekStart < startDate
                ? startDate
                : (weekStart > endDate ? endDate : weekStart);

            if (representativeDay.Month != lastLabeledMonth)
            {
                HeatmapMonths.Add(representativeDay.ToString("MMM", CultureInfo.InvariantCulture));
                lastLabeledMonth = representativeDay.Month;
            }
            else
            {
                HeatmapMonths.Add(string.Empty);
            }
        }

        bool isDark = _currentTheme == ElementTheme.Dark ||
            (_currentTheme == ElementTheme.Default && Application.Current?.RequestedTheme == ApplicationTheme.Dark);

        Brush emptyBrush = isDark
            ? new SolidColorBrush(Color.FromArgb(255, 23, 28, 34))
            : new SolidColorBrush(Color.FromArgb(255, 228, 232, 238));

        Color accentColor = GetCurrentAccentColor();

        for (DateTime weekStart = gridStart; weekStart <= gridEnd; weekStart = weekStart.AddDays(7))
        {
            for (int dayOffset = 0; dayOffset < 7; dayOffset++)
            {
                DateTime date = weekStart.AddDays(dayOffset);
                bool inRange = date >= startDate && date <= endDate;
                double mins = inRange && minutesByDate.TryGetValue(date, out var m) ? m : 0;

                string tooltip = !inRange
                    ? string.Empty
                    : mins > 0
                        ? $"{FormatPlaytime(mins)} on {date:MMM d, yyyy}"
                        : $"No playtime on {date:MMM d, yyyy}";

                Brush fillBrush = !inRange
                    ? new SolidColorBrush(Colors.Transparent)
                    : (mins <= 0 ? emptyBrush : CreateTierBrush(mins, accentColor, isDark));

                HeatmapDays.Add(new HeatmapDayCell
                {
                    Date = date,
                    Minutes = mins,
                    IsInSelectedYear = inRange,
                    FillBrush = fillBrush,
                    CellOpacity = !inRange ? 0.0 : 1.0,
                    TooltipText = tooltip
                });
            }
        }
    }

    private static Color GetCurrentAccentColor()
    {
        if (Application.Current?.Resources.TryGetValue("SystemAccentColor", out var resource) == true &&
            resource is Color resourceColor)
        {
            return resourceColor;
        }

        var uiSettings = new UISettings();
        return uiSettings.GetColorValue(UIColorType.Accent);
    }

    private static SolidColorBrush CreateTierBrush(double minutes, Color accentColor, bool isDark)
    {
        byte alpha = minutes switch
        {
            < 30 => 145,
            < 90 => 190,
            < 180 => 228,
            _ => 255
        };

        double factor = (isDark && minutes < 30) ? 0.72 : 1.0;
        byte r = (byte)(accentColor.R * factor);
        byte g = (byte)(accentColor.G * factor);
        byte b = (byte)(accentColor.B * factor);

        return new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
    }

    private List<PlaytimeSession> FilterSessionsByPeriod(IEnumerable<PlaytimeSession> sessions, StatsTimePeriod period)
    {
        var validSessions = sessions
            .Where(s => s != null && s.Duration.TotalMinutes >= 1)
            .ToList();

        if (validSessions.Count == 0 || period == StatsTimePeriod.AllTime)
        {
            return validSessions;
        }

        DateTime now = DateTime.Now;
        DateTime cutoff = period switch
        {
            StatsTimePeriod.LastWeek => now.AddDays(-7),
            StatsTimePeriod.LastMonth => now.AddMonths(-1),
            StatsTimePeriod.LastYear => now.AddYears(-1),
            _ => DateTime.MinValue
        };

        DateTime firstPlaytime = validSessions.Min(s => s.StartTime);
        DateTime effectiveStart = cutoff < firstPlaytime ? firstPlaytime : cutoff;

        return validSessions
            .Where(s => s.StartTime >= effectiveStart && s.StartTime <= now)
            .ToList();
    }

    private void RecalculateStats()
    {
        var allTimeGrouped = _allSessions
            .Where(s => s != null && s.Duration.TotalMinutes >= 1)
            .GroupBy(s => s.GameTitle)
            .Select(g => new
            {
                Title = g.Key,
                TotalMinutes = g.Sum(s => s.Duration.TotalMinutes)
            })
            .OrderByDescending(x => x.TotalMinutes)
            .FirstOrDefault();

        FavoriteGameTitle = allTimeGrouped?.Title ?? "No sessions recorded";

        var filteredSessions = FilterSessionsByPeriod(_allSessions, SelectedTimePeriod);

        if (filteredSessions.Count == 0)
        {
            TotalPlaytimeFormatted = "0 mins";
            TotalSessionsCount = 0;
            PeriodFavoriteGameTitle = "No sessions in period";
            TopPlayedGames.Clear();
            return;
        }

        TotalSessionsCount = filteredSessions.Count;

        var totalMinutes = filteredSessions.Sum(s => s.Duration.TotalMinutes);
        TotalPlaytimeFormatted = FormatPlaytime(totalMinutes);

        var grouped = filteredSessions
            .GroupBy(s => s.GameTitle)
            .Select(g =>
            {
                var totalMins = g.Sum(s => s.Duration.TotalMinutes);
                var matchedGame = _allGames.FirstOrDefault(
                    game => string.Equals(game.Title, g.Key, StringComparison.OrdinalIgnoreCase));

                string iconPath = !string.IsNullOrWhiteSpace(matchedGame?.IconUrl)
                    ? matchedGame.IconUrl
                    : string.Empty;

                return new GamePlayStatItem
                {
                    Title = g.Key,
                    TotalMinutes = totalMins,
                    TotalHoursFormatted = FormatPlaytime(totalMins),
                    SessionCount = g.Count(),
                    IconUrl = iconPath
                };
            })
            .OrderByDescending(x => x.TotalMinutes)
            .ToList();

        PeriodFavoriteGameTitle = grouped.FirstOrDefault()?.Title ?? "No sessions in period";

        TopPlayedGames.Clear();
        foreach (var item in grouped)
        {
            TopPlayedGames.Add(item);
        }
    }

    public static string FormatPlaytime(double totalMinutes)
    {
        if (totalMinutes <= 0)
        {
            return "0 mins";
        }

        if (totalMinutes < 60)
        {
            return $"{Math.Max(1, Math.Round(totalMinutes))} mins";
        }

        double totalHours = totalMinutes / 60.0;

        if (totalHours >= 24)
        {
            int days = (int)(totalHours / 24);
            int remainingHours = (int)(totalHours % 24);
            return remainingHours > 0
                ? $"{days} {(days == 1 ? "day" : "days")} {remainingHours} hrs"
                : $"{days} {(days == 1 ? "day" : "days")}";
        }

        return $"{Math.Round(totalHours, 1)} hrs";
    }
}

public class HeatmapDayCell
{
    public DateTime Date { get; set; }
    public double Minutes { get; set; }
    public bool IsInSelectedYear { get; set; }
    public Brush FillBrush { get; set; } = new SolidColorBrush(Colors.Transparent);
    public double CellOpacity { get; set; } = 1.0;
    public string TooltipText { get; set; } = string.Empty;
}

public class GamePlayStatItem
{
    public string Title { get; set; } = string.Empty;
    public double TotalMinutes { get; set; }
    public string TotalHoursFormatted { get; set; } = "0 mins";
    public int SessionCount { get; set; }
    public string IconUrl { get; set; } = string.Empty;
    public string SessionsFormatted => $"{SessionCount} {(SessionCount == 1 ? "session" : "sessions")}";
}