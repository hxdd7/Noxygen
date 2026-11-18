using CommunityToolkit.Mvvm.ComponentModel;
using Noxygen.Models;
using Noxygen.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Noxygen.ViewModels;

public partial class StatsViewModel : ObservableObject
{
    private readonly PlaytimeTrackerService _trackerService = new();
    private readonly GameDataService _dataService = new();

    [ObservableProperty]
    private string _totalPlaytimeFormatted = "0 mins";

    [ObservableProperty]
    private int _totalSessionsCount;

    [ObservableProperty]
    private string _favoriteGameTitle = "None yet";

    public ObservableCollection<GamePlayStatItem> TopPlayedGames { get; } = new();

    public async Task InitializeAsync()
    {
        var sessions = await _trackerService.LoadSessionsAsync();
        var allGames = await _dataService.LoadGamesAsync();

        if (sessions.Count == 0)
        {
            TotalPlaytimeFormatted = "0 mins";
            TotalSessionsCount = 0;
            FavoriteGameTitle = "No sessions recorded";
            TopPlayedGames.Clear();
            return;
        }

        TotalSessionsCount = sessions.Count;

        var totalMinutes = sessions.Sum(s => s.Duration.TotalMinutes);
        TotalPlaytimeFormatted = FormatPlaytime(totalMinutes);

        var grouped = sessions
            .GroupBy(s => s.GameTitle)
            .Select(g =>
            {
                var totalMins = g.Sum(s => s.Duration.TotalMinutes);

                // Find matching game to grab its specific IconUrl
                var matchedGame = allGames.FirstOrDefault(game => string.Equals(game.Title, g.Key, StringComparison.OrdinalIgnoreCase));

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

        if (grouped.Any())
        {
            FavoriteGameTitle = grouped.First().Title;
        }

        TopPlayedGames.Clear();
        foreach (var item in grouped)
        {
            TopPlayedGames.Add(item);
        }
    }

    public static string FormatPlaytime(double totalMinutes)
    {
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

public class GamePlayStatItem
{
    public string Title { get; set; } = string.Empty;
    public double TotalMinutes { get; set; }
    public string TotalHoursFormatted { get; set; } = "0 mins";
    public int SessionCount { get; set; }
    public string IconUrl { get; set; } = string.Empty;
    public string SessionsFormatted => $"{SessionCount} {(SessionCount == 1 ? "session" : "sessions")}";
}