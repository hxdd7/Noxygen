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
        if (game == null) return;

        try
        {
            string? steamAppId = null;

            // 1. Check if valid numeric SteamId exists on the model
            if (!string.IsNullOrWhiteSpace(game.SteamId) && long.TryParse(game.SteamId, out _))
            {
                steamAppId = game.SteamId.Trim();
            }
            // 2. Fallback to API title search if SteamId is missing or invalid
            else
            {
                steamAppId = await SearchSteamAppIdByTitleAsync(game.Title);
                if (!string.IsNullOrEmpty(steamAppId))
                {
                    game.SteamId = steamAppId;
                }
            }

            if (string.IsNullOrEmpty(steamAppId)) return;

            // Apply valid vertical cover URL (600x900)
            game.CoverArtUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{steamAppId}/library_600x900.jpg";

            // Query Steam API for metadata details
            string url = $"https://store.steampowered.com/api/appdetails?appids={steamAppId}";
            string json = await _httpClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty(steamAppId, out var appData) &&
                appData.TryGetProperty("success", out var success) &&
                success.GetBoolean())
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
            // Fallback silently on network or parse failure
        }
    }

    private async Task<string?> SearchSteamAppIdByTitleAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

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