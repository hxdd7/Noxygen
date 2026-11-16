using CommunityToolkit.Mvvm.ComponentModel;
using Noxygen.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Noxygen.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<Game> PinnedGames { get; } = new();

    [ObservableProperty]
    private bool _hasPinnedGames;

    public void RefreshPinnedGames(IEnumerable<Game> allGames)
    {
        PinnedGames.Clear();

        if (allGames != null)
        {
            foreach (var game in allGames.Where(g => g != null && g.IsPinned))
            {
                PinnedGames.Add(game);
            }
        }

        HasPinnedGames = PinnedGames.Count > 0;
    }
}