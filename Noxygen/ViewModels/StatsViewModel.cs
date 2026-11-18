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

    [ObservableProperty]
    private string _totalPlaytimeFormatted = "0 hrs";

    [ObservableProperty]
    private int _totalSessionsCount;

    [ObservableProperty]
    private string _favoriteGameTitle = "None yet";

    public ObservableCollection<GamePlayStatItem> TopPlayedGames { get; } = new();

    public async Task InitializeAsync()
    {
        var sessions = await _trackerService.LoadSessionsAsync();

        if (sessions.Count == 0)
        {
            TotalPlaytimeFormatted = "0 hrs";
            TotalSessionsCount = 0;
            FavoriteGameTitle = "No sessions recorded";
            TopPlayedGames.Clear();
            return;
        }

        TotalSessionsCount = sessions.Count;

        var totalMinutes = sessions.Sum(s => s.Duration.TotalMinutes);
        TotalPlaytimeFormatted = totalMinutes >= 60
            ? $"{Math.Round(totalMinutes / 60.0, 1)} hrs"
            : $"{Math.Round(totalMinutes)} mins";

        var grouped = sessions
            .GroupBy(s => s.GameTitle)
            .Select(g => new GamePlayStatItem
            {
                Title = g.Key,
                TotalMinutes = g.Sum(s => s.Duration.TotalMinutes),
                TotalHoursFormatted = $"{Math.Round(g.Sum(s => s.Duration.TotalMinutes) / 60.0, 1)} hrs",
                SessionCount = g.Count()
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
}

public class GamePlayStatItem
{
    public string Title { get; set; } = string.Empty;
    public double TotalMinutes { get; set; }
    public string TotalHoursFormatted { get; set; } = "0 hrs";
    public int SessionCount { get; set; }
    public string SessionsFormatted => $"{SessionCount} {(SessionCount == 1 ? "session" : "sessions")}";
}