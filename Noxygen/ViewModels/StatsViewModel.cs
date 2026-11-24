using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
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
    private static readonly DayOfWeek[] OrderedWeekdays =
    {
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    };

    private static readonly string[] ShortWeekdayLabels =
    {
        "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"
    };

    private readonly PlaytimeTrackerService _trackerService = new();
    private readonly GameDataService _dataService = new();
    private readonly UISettings _uiSettings = new();
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    private List<PlaytimeSession> _allSessions = new();
    private List<Game> _allGames = new();
    private ElementTheme _currentTheme = ElementTheme.Default;
    private DateTime _inspectedDate = DateTime.Today;
    private DateTime? _pinnedDate;

    [ObservableProperty]
    private StatsTimePeriod _selectedTimePeriod = StatsTimePeriod.LastWeek;

    [ObservableProperty]
    private string _totalPlaytimeFormatted = "0 mins";

    [ObservableProperty]
    private string _averageDailyFormatted = "0 mins";

    [ObservableProperty]
    private int _totalSessionsCount;

    [ObservableProperty]
    private string _favoriteGameTitle = "None yet";

    [ObservableProperty]
    private string _allTimeFavoriteSubtitle = "All-time top: None yet";

    [ObservableProperty]
    private string _periodFavoriteGameTitle = "None yet";

    [ObservableProperty]
    private string _averageSessionFormatted = "0 mins";

    [ObservableProperty]
    private string _longestSessionFormatted = "0 mins";

    [ObservableProperty]
    private string _gamingStreakFormatted = "0 days";

    [ObservableProperty]
    private string _peakWeekdayText = "Peak: —";

    [ObservableProperty]
    private string _weekdayScaleText = "Scale: 0 mins max";

    [ObservableProperty]
    private string _heatmapSummaryText = "0 playtime in the last year";

    [ObservableProperty]
    private string _selectedDayTitle = "Today";

    [ObservableProperty]
    private string _selectedDaySubtitle = DateTime.Today.ToString("dddd, MMM d, yyyy", CultureInfo.InvariantCulture);

    [ObservableProperty]
    private string _selectedDayTotalFormatted = "0 mins";

    [ObservableProperty]
    private bool _isSelectedDayEmpty = true;

    [ObservableProperty]
    private bool _showTodayButton;

    public ObservableCollection<HeatmapDayCell> HeatmapDays { get; } = new();
    public ObservableCollection<string> HeatmapMonths { get; } = new();
    public ObservableCollection<DayGamePlayItem> SelectedDayGames { get; } = new();
    public ObservableCollection<GamePlayStatItem> TopPlayedGames { get; } = new();
    public ObservableCollection<WeekdayBarItem> WeekdayBars { get; } = new();
    public ObservableCollection<RecentSessionItem> RecentSessions { get; } = new();

    public StatsViewModel()
    {
        for (int i = 0; i < OrderedWeekdays.Length; i++)
        {
            WeekdayBars.Add(new WeekdayBarItem
            {
                DayLabel = ShortWeekdayLabels[i],
                Minutes = 0,
                BarHeight = 0,
                TooltipText = $"{ShortWeekdayLabels[i]}: 0 mins"
            });
        }

        _uiSettings.ColorValuesChanged += OnSystemColorValuesChanged;
    }

    private void OnSystemColorValuesChanged(UISettings sender, object args)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            if (_allSessions.Count > 0 || HeatmapDays.Count > 0)
            {
                BuildRollingLastYearHeatmap();
                SelectHeatmapDay(_inspectedDate, isFromHover: true);
            }
        });
    }

    partial void OnSelectedTimePeriodChanged(StatsTimePeriod value)
    {
        RecalculateStats();
    }

    public async Task InitializeAsync(ElementTheme theme = ElementTheme.Default)
    {
        _currentTheme = theme;
        _pinnedDate = null;
        _allSessions = await _trackerService.LoadSessionsAsync() ?? new List<PlaytimeSession>();
        _allGames = await _dataService.LoadGamesAsync() ?? new List<Game>();

        RecalculateStats();
        BuildRollingLastYearHeatmap();
        SelectHeatmapDay(DateTime.Today, isFromHover: false);
    }

    public void RefreshHeatmapTheme(ElementTheme theme)
    {
        _currentTheme = theme;
        if (_allSessions.Count > 0 || HeatmapDays.Count > 0)
        {
            BuildRollingLastYearHeatmap();
            SelectHeatmapDay(_inspectedDate, isFromHover: false);
        }
    }

    public void SelectHeatmapDay(DateTime date, bool isFromHover = false)
    {
        DateTime targetDate = date.Date;

        if (!isFromHover)
        {
            _pinnedDate = (_pinnedDate.HasValue && _pinnedDate.Value == targetDate && targetDate != DateTime.Today)
                ? DateTime.Today
                : targetDate;

            if (_pinnedDate.Value == DateTime.Today)
            {
                _pinnedDate = null;
            }
        }

        _inspectedDate = targetDate;
        ShowTodayButton = _inspectedDate != DateTime.Today;

        bool isDark = _currentTheme == ElementTheme.Dark ||
            (_currentTheme == ElementTheme.Default && Application.Current?.RequestedTheme == ApplicationTheme.Dark);

        Brush highlightBrush = isDark
            ? new SolidColorBrush(Color.FromArgb(95, 203, 213, 225))
            : new SolidColorBrush(Color.FromArgb(105, 71, 85, 105));

        var transparentBrush = new SolidColorBrush(Colors.Transparent);

        foreach (var cell in HeatmapDays)
        {
            cell.SelectionBorderBrush = (cell.IsInSelectedYear && cell.Date == _inspectedDate)
                ? highlightBrush
                : transparentBrush;
        }

        SelectedDayTitle = _inspectedDate == DateTime.Today
            ? "Today"
            : _inspectedDate == DateTime.Today.AddDays(-1)
                ? "Yesterday"
                : _inspectedDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

        SelectedDaySubtitle = _inspectedDate.ToString("dddd, MMM d, yyyy", CultureInfo.InvariantCulture);

        var daySessions = _allSessions
            .Where(s => s != null && s.Duration.TotalMinutes >= 1 && s.StartTime.Date == _inspectedDate)
            .ToList();

        SelectedDayGames.Clear();

        if (daySessions.Count == 0)
        {
            SelectedDayTotalFormatted = "0 mins";
            IsSelectedDayEmpty = true;
            return;
        }

        double dayTotalMins = daySessions.Sum(s => s.Duration.TotalMinutes);
        SelectedDayTotalFormatted = FormatPlaytime(dayTotalMins);
        IsSelectedDayEmpty = false;

        var groupedGames = daySessions
            .GroupBy(s => s.GameTitle)
            .Select(g =>
            {
                double mins = g.Sum(s => s.Duration.TotalMinutes);
                var matchedGame = _allGames.FirstOrDefault(
                    game => string.Equals(game.Title, g.Key, StringComparison.OrdinalIgnoreCase));

                return new DayGamePlayItem
                {
                    Title = g.Key,
                    Minutes = mins,
                    PlaytimeFormatted = FormatPlaytime(mins),
                    IconUrl = matchedGame?.IconUrl ?? string.Empty
                };
            })
            .OrderByDescending(x => x.Minutes)
            .ToList();

        foreach (var item in groupedGames)
        {
            SelectedDayGames.Add(item);
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
                    : (mins <= 0 ? emptyBrush : CreateTierBrush(mins, accentColor));

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

    private Color GetCurrentAccentColor()
    {
        try
        {
            return _uiSettings.GetColorValue(UIColorType.Accent);
        }
        catch
        {
            if (Application.Current?.Resources.TryGetValue("SystemAccentColor", out var resource) == true &&
                resource is Color resourceColor)
            {
                return resourceColor;
            }

            return Colors.DodgerBlue;
        }
    }

    private static SolidColorBrush CreateTierBrush(double minutes, Color accentColor)
    {
        byte alpha = minutes switch
        {
            < 30 => 42,
            < 90 => 88,
            < 180 => 160,
            _ => 255
        };

        return new SolidColorBrush(Color.FromArgb(alpha, accentColor.R, accentColor.G, accentColor.B));
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
        var validAllSessions = _allSessions
            .Where(s => s != null && s.Duration.TotalMinutes >= 1)
            .ToList();

        var allTimeGrouped = validAllSessions
            .GroupBy(s => s.GameTitle)
            .Select(g => new
            {
                Title = g.Key,
                TotalMinutes = g.Sum(s => s.Duration.TotalMinutes)
            })
            .OrderByDescending(x => x.TotalMinutes)
            .FirstOrDefault();

        FavoriteGameTitle = allTimeGrouped?.Title ?? "No sessions recorded";
        AllTimeFavoriteSubtitle = allTimeGrouped != null
            ? $"All-time top: {allTimeGrouped.Title}"
            : "All-time top: None yet";

        GamingStreakFormatted = FormatStreak(CalculateCurrentStreak(validAllSessions));

        RecentSessions.Clear();
        foreach (var session in validAllSessions.OrderByDescending(s => s.StartTime).Take(6))
        {
            var matchedGame = _allGames.FirstOrDefault(
                g => string.Equals(g.Title, session.GameTitle, StringComparison.OrdinalIgnoreCase));

            RecentSessions.Add(new RecentSessionItem
            {
                GameTitle = session.GameTitle,
                IconUrl = matchedGame?.IconUrl ?? string.Empty,
                DurationFormatted = FormatPlaytime(session.Duration.TotalMinutes),
                WhenFormatted = FormatRelativeDay(session.StartTime.Date)
            });
        }

        var filteredSessions = FilterSessionsByPeriod(validAllSessions, SelectedTimePeriod);

        if (filteredSessions.Count == 0)
        {
            TotalPlaytimeFormatted = "0 mins";
            AverageDailyFormatted = "0 mins";
            AverageSessionFormatted = "0 mins";
            LongestSessionFormatted = "0 mins";
            TotalSessionsCount = 0;
            PeriodFavoriteGameTitle = "No sessions in period";
            PeakWeekdayText = "Peak: —";
            WeekdayScaleText = "Scale: 0 mins max";
            TopPlayedGames.Clear();
            BuildWeekdayBars(Array.Empty<PlaytimeSession>());
            return;
        }

        TotalSessionsCount = filteredSessions.Count;

        double totalMinutes = filteredSessions.Sum(s => s.Duration.TotalMinutes);
        TotalPlaytimeFormatted = FormatPlaytime(totalMinutes);

        int divisorDays = SelectedTimePeriod switch
        {
            StatsTimePeriod.LastWeek => 7,
            StatsTimePeriod.LastMonth => 30,
            StatsTimePeriod.LastYear => 365,
            _ => Math.Max(1, (DateTime.Today - filteredSessions.Min(s => s.StartTime.Date)).Days + 1)
        };

        AverageDailyFormatted = FormatPlaytime(totalMinutes / divisorDays);
        AverageSessionFormatted = FormatPlaytime(totalMinutes / filteredSessions.Count);
        LongestSessionFormatted = FormatPlaytime(filteredSessions.Max(s => s.Duration.TotalMinutes));

        BuildWeekdayBars(filteredSessions);

        var grouped = filteredSessions
            .GroupBy(s => s.GameTitle)
            .Select(g =>
            {
                double totalMins = g.Sum(s => s.Duration.TotalMinutes);
                var matchedGame = _allGames.FirstOrDefault(
                    game => string.Equals(game.Title, g.Key, StringComparison.OrdinalIgnoreCase));

                double share = totalMinutes > 0 ? totalMins / totalMinutes : 0;

                return new GamePlayStatItem
                {
                    Title = g.Key,
                    TotalMinutes = totalMins,
                    TotalHoursFormatted = FormatPlaytime(totalMins),
                    SessionCount = g.Count(),
                    SharePercent = (int)Math.Round(share * 100),
                    ShareRatio = share > 0 ? Math.Clamp(share, 0.02, 1.0) : 0.0,
                    ShareBarWidth = Math.Max(6, share * 360),
                    IconUrl = matchedGame?.IconUrl ?? string.Empty
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

    private void BuildWeekdayBars(IEnumerable<PlaytimeSession> sessions)
    {
        if (WeekdayBars.Count != OrderedWeekdays.Length)
        {
            WeekdayBars.Clear();
            for (int i = 0; i < OrderedWeekdays.Length; i++)
            {
                WeekdayBars.Add(new WeekdayBarItem { DayLabel = ShortWeekdayLabels[i] });
            }
        }

        var totalsByDay = sessions
            .GroupBy(s => s.StartTime.DayOfWeek)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Duration.TotalMinutes));

        double maxMins = OrderedWeekdays.Max(d => totalsByDay.TryGetValue(d, out var m) ? m : 0);
        WeekdayScaleText = maxMins > 0 ? $"Scale: {FormatPlaytime(maxMins)} max" : "Scale: 0 mins max";

        var peakEntry = OrderedWeekdays
            .Select(d => new { Day = d, Mins = totalsByDay.TryGetValue(d, out var m) ? m : 0 })
            .OrderByDescending(x => x.Mins)
            .FirstOrDefault();

        PeakWeekdayText = (peakEntry != null && peakEntry.Mins > 0)
            ? $"Peak: {CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedDayName(peakEntry.Day)} ({FormatPlaytime(peakEntry.Mins)})"
            : "Peak: —";

        const double maxBarHeight = 112.0;

        for (int i = 0; i < OrderedWeekdays.Length; i++)
        {
            double mins = totalsByDay.TryGetValue(OrderedWeekdays[i], out var m) ? m : 0;
            double height = maxMins > 0 && mins > 0
                ? Math.Max(8.0, (mins / maxMins) * maxBarHeight)
                : 0.0;

            WeekdayBars[i].Minutes = mins;
            WeekdayBars[i].BarHeight = height;
            WeekdayBars[i].TooltipText = $"{ShortWeekdayLabels[i]}: {FormatPlaytime(mins)}";
        }
    }

    private static int CalculateCurrentStreak(IEnumerable<PlaytimeSession> sessions)
    {
        var activeDates = sessions
            .Select(s => s.StartTime.Date)
            .ToHashSet();

        if (activeDates.Count == 0)
        {
            return 0;
        }

        DateTime cursor = activeDates.Contains(DateTime.Today)
            ? DateTime.Today
            : (activeDates.Contains(DateTime.Today.AddDays(-1)) ? DateTime.Today.AddDays(-1) : DateTime.MinValue);

        if (cursor == DateTime.MinValue)
        {
            return 0;
        }

        int streak = 0;
        while (activeDates.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }

    private static string FormatStreak(int days) =>
        days == 1 ? "1 day" : $"{days} days";

    private static string FormatRelativeDay(DateTime date)
    {
        if (date == DateTime.Today) return "Today";
        if (date == DateTime.Today.AddDays(-1)) return "Yesterday";
        return date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
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

public partial class HeatmapDayCell : ObservableObject
{
    public DateTime Date { get; set; }
    public double Minutes { get; set; }
    public bool IsInSelectedYear { get; set; }
    public Brush FillBrush { get; set; } = new SolidColorBrush(Colors.Transparent);
    public double CellOpacity { get; set; } = 1.0;
    public string TooltipText { get; set; } = string.Empty;

    [ObservableProperty]
    private Brush _selectionBorderBrush = new SolidColorBrush(Colors.Transparent);
}

public class DayGamePlayItem
{
    public string Title { get; set; } = string.Empty;
    public double Minutes { get; set; }
    public string PlaytimeFormatted { get; set; } = "0 mins";
    public string IconUrl { get; set; } = string.Empty;
}

public partial class WeekdayBarItem : ObservableObject
{
    public string DayLabel { get; set; } = string.Empty;
    public double Minutes { get; set; }
    public string TooltipText { get; set; } = string.Empty;

    [ObservableProperty]
    private double _barHeight;
}

public class RecentSessionItem
{
    public string GameTitle { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public string DurationFormatted { get; set; } = "0 mins";
    public string WhenFormatted { get; set; } = string.Empty;
}

public class GamePlayStatItem
{
    public string Title { get; set; } = string.Empty;
    public double TotalMinutes { get; set; }
    public string TotalHoursFormatted { get; set; } = "0 mins";
    public int SessionCount { get; set; }
    public int SharePercent { get; set; }
    public double ShareRatio { get; set; }
    public double ShareBarWidth { get; set; }
    public string IconUrl { get; set; } = string.Empty;
    public string SessionsFormatted => $"{SessionCount} {(SessionCount == 1 ? "session" : "sessions")}";
    public string MetaSummaryFormatted => $"{SessionsFormatted} · {SharePercent}% of playtime";
}