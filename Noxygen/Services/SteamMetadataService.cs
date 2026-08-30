using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Noxygen.Models;

namespace Noxygen.Services;

public class SteamMetadataService
{
    private static readonly HttpClient _httpClient = new();

    public async Task FetchMetadataForGameAsync(Game game)
    {
        try
        {
            string? steamAppId = null;

            if (game.Launcher.Equals("Steam", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(game.ExecutablePath))
            {
                steamAppId = game.ExecutablePath;
            }
            else
            {
                steamAppId = await SearchSteamAppIdByTitleAsync(game.Title);
            }

            if (string.IsNullOrEmpty(steamAppId)) return;

            // Force 2:3 Vertical Cover (Library Capsule: 600x900)
            game.CoverArtUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{steamAppId}/library_600x900.jpg";

            // Fetch Background and Company details
            string url = $"https://store.steampowered.com/api/appdetails?appids={steamAppId}";
            string json = await _httpClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty(steamAppId, out var appData) && appData.GetProperty("success").GetBoolean())
            {
                var data = appData.GetProperty("data");

                if (data.TryGetProperty("background", out var bg))
                {
                    game.BackgroundUrl = bg.GetString() ?? game.BackgroundUrl;
                }

                if (data.TryGetProperty("developers", out var devs) && devs.GetArrayLength() > 0)
                {
                    game.Company = devs[0].GetString() ?? game.Company;
                }
            }
        }
        catch
        {
            // Fallback silently
        }
    }

    private async Task<string?> SearchSteamAppIdByTitleAsync(string title)
    {
        try
        {
            string searchUrl = $"https://store.steampowered.com/api/storesearch/?term={HttpUtility.UrlEncode(title)}&l=english&cc=US";
            string json = await _httpClient.GetStringAsync(searchUrl);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
            {
                return items[0].GetProperty("id").GetInt32().ToString();
            }
        }
        catch { }

        return null;
    }
}