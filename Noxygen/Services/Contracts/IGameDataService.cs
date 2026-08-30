using System.Collections.Generic;
using System.Threading.Tasks;
using Noxygen.Models;

namespace Noxygen.Services.Contracts;

public interface IGameDataService
{
    Task<List<Game>> GetGamesAsync();
    Task SaveGamesAsync(List<Game> games);
    Task<List<Game>> ScanSystemGamesAsync();
}