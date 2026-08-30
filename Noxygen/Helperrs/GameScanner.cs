using Microsoft.Win32;
using Noxygen.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noxygen.Helpers;

public class GameScanner
{
    public List<Game> ScanAll()
    {
        var games = new List<Game>();

        Debug.WriteLine("---> [SCANNER START]");

        try { games.AddRange(ScanSteamGames()); } catch (Exception ex) { Debug.WriteLine($"Steam Scan Failure: {ex.Message}"); }
        try { games.AddRange(ScanEpicGames()); } catch (Exception ex) { Debug.WriteLine($"Epic Scan Failure: {ex.Message}"); }
        try { games.AddRange(ScanGogGames()); } catch (Exception ex) { Debug.WriteLine($"GOG Scan Failure: {ex.Message}"); }

        // Deduplicate by title across all launchers to keep the list clean
        var uniqueGames = games
            .GroupBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        Debug.WriteLine($"---> [SCANNER COMPLETE] Total Unique Games: {uniqueGames.Count}");
        return uniqueGames;
    }

    #region Steam Detection
    private List<Game> ScanSteamGames()
    {
        var games = new List<Game>();

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")
                     ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");

        if (key?.GetValue("SteamPath") is not string steamPath || !Directory.Exists(steamPath))
            return games;

        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        var libraryPaths = new HashSet<string> { Path.Combine(steamPath, "steamapps") };

        if (File.Exists(vdfPath))
        {
            try
            {
                var vdfContent = File.ReadAllText(vdfPath);
                var matches = Regex.Matches(vdfContent, @"""path""\s+""([^""]+)""");
                foreach (Match match in matches)
                {
                    var path = match.Groups[1].Value.Replace(@"\\", @"\");
                    var appsPath = Path.Combine(path, "steamapps");
                    if (Directory.Exists(appsPath))
                        libraryPaths.Add(appsPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[STEAM VDF PARSE ERROR]: {ex.Message}");
            }
        }

        foreach (var libPath in libraryPaths)
        {
            if (!Directory.Exists(libPath)) continue;

            foreach (var manifestFile in Directory.GetFiles(libPath, "appmanifest_*.acf"))
            {
                try
                {
                    var content = File.ReadAllText(manifestFile);

                    var nameMatch = Regex.Match(content, @"""name""\s+""([^""]+)""");
                    var dirMatch = Regex.Match(content, @"""installdir""\s+""([^""]+)""");

                    // Extract numeric AppId from manifest content
                    var appIdMatch = Regex.Match(content, @"""appid""\s+""?(\d+)""?");
                    string appId = appIdMatch.Success ? appIdMatch.Groups[1].Value.Trim() : string.Empty;

                    // Fallback to filename extraction (e.g. appmanifest_945360.acf) if missing
                    if (string.IsNullOrEmpty(appId) || !long.TryParse(appId, out _))
                    {
                        var fileMatch = Regex.Match(Path.GetFileName(manifestFile), @"appmanifest_(\d+)\.acf");
                        if (fileMatch.Success) appId = fileMatch.Groups[1].Value;
                    }

                    if (nameMatch.Success && dirMatch.Success)
                    {
                        string title = nameMatch.Groups[1].Value;

                        // Filter non-game software
                        if (title.StartsWith("Steamworks") ||
                            title.StartsWith("Proton") ||
                            title.EndsWith("Server") ||
                            title.EndsWith("Dedicated Server") ||
                            title.Contains("Test Server"))
                            continue;

                        string folderName = dirMatch.Groups[1].Value;
                        string fullPath = Path.Combine(libPath, "common", folderName);

                        if (!Directory.Exists(fullPath)) continue;

                        if (games.Exists(g => g.Title.Equals(title, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        string exePath = FindPrimaryExecutable(fullPath, title);

                        // Direct fallback CDN cover
                        string coverUrl = !string.IsNullOrEmpty(appId)
                            ? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg"
                            : string.Empty;

                        games.Add(new Game
                        {
                            Title = title,
                            SteamId = appId,
                            InstallPath = fullPath,
                            ExecutablePath = exePath,
                            CoverArtUrl = coverUrl,
                            InstalledSizeGb = CalculateDirectorySizeGb(fullPath),
                            Launcher = "Steam"
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[STEAM MANIFEST ERROR]: {ex.Message}");
                }
            }
        }

        return games;
    }
    #endregion

    #region Epic Games Detection
    private List<Game> ScanEpicGames()
    {
        var games = new List<Game>();
        string epicManifestDir = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";

        if (!Directory.Exists(epicManifestDir)) return games;

        foreach (var manifestFile in Directory.GetFiles(epicManifestDir, "*.item"))
        {
            try
            {
                var json = File.ReadAllText(manifestFile);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string title = root.TryGetProperty("DisplayName", out var nameProp) ? nameProp.GetString() ?? "" : "";
                string installPath = root.TryGetProperty("InstallLocation", out var pathProp) ? pathProp.GetString() ?? "" : "";
                string launchExe = root.TryGetProperty("LaunchExecutable", out var exeProp) ? exeProp.GetString() ?? "" : "";

                if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                {
                    string fullExePath = !string.IsNullOrEmpty(launchExe) ? Path.Combine(installPath, launchExe) : string.Empty;
                    if (string.IsNullOrEmpty(fullExePath) || !File.Exists(fullExePath))
                    {
                        fullExePath = FindPrimaryExecutable(installPath, title);
                    }

                    games.Add(new Game
                    {
                        Title = title,
                        InstallPath = installPath,
                        ExecutablePath = fullExePath,
                        InstalledSizeGb = CalculateDirectorySizeGb(installPath),
                        Launcher = "Epic Games"
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EPIC MANIFEST ERROR]: {ex.Message}");
            }
        }

        return games;
    }
    #endregion

    #region GOG Galaxy Detection
    private List<Game> ScanGogGames()
    {
        var games = new List<Game>();
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");

        if (key == null) return games;

        foreach (var subKeyName in key.GetSubKeyNames())
        {
            try
            {
                using var gameKey = key.OpenSubKey(subKeyName);
                if (gameKey == null) continue;

                string title = gameKey.GetValue("gameName") as string ?? "";
                string path = gameKey.GetValue("path") as string ?? "";
                string exeName = gameKey.GetValue("exe") as string ?? "";

                if (!string.IsNullOrEmpty(title) && Directory.Exists(path))
                {
                    string fullExePath = !string.IsNullOrEmpty(exeName) ? Path.Combine(path, exeName) : string.Empty;
                    if (string.IsNullOrEmpty(fullExePath) || !File.Exists(fullExePath))
                    {
                        fullExePath = FindPrimaryExecutable(path, title);
                    }

                    games.Add(new Game
                    {
                        Title = title,
                        InstallPath = path,
                        ExecutablePath = fullExePath,
                        InstalledSizeGb = CalculateDirectorySizeGb(path),
                        Launcher = "GOG Galaxy"
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GOG REGISTRY ERROR]: {ex.Message}");
            }
        }

        return games;
    }
    #endregion

    #region Executable Resolution & Directory Helper
    public static string FindPrimaryExecutable(string folderPath, string title)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return string.Empty;

        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
            };

            var files = new DirectoryInfo(folderPath).EnumerateFiles("*.exe", options)
                .Select(f => f.FullName)
                .ToList();

            if (!files.Any()) return string.Empty;

            var filtered = files.Where(f =>
            {
                string name = Path.GetFileName(f).ToLowerInvariant();
                return !name.Contains("unins") &&
                       !name.Contains("setup") &&
                       !name.Contains("crash") &&
                       !name.Contains("unityweb") &&
                       !name.Contains("redist") &&
                       !name.Contains("dxsetup");
            }).ToList();

            if (!filtered.Any()) filtered = files;

            string sanitizedTitle = new string(title.Where(char.IsLetterOrDigit).ToArray());
            var bestMatch = filtered.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                .Replace(" ", "")
                .Equals(sanitizedTitle, StringComparison.OrdinalIgnoreCase));

            return bestMatch ?? filtered.FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private double CalculateDirectorySizeGb(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return 0;

        try
        {
            var dirInfo = new DirectoryInfo(path);
            long totalBytes = 0;

            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
            };

            foreach (var file in dirInfo.EnumerateFiles("*", options))
            {
                totalBytes += file.Length;
            }

            return Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0), 2);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SIZE CALCULATION ERROR] Path '{path}': {ex.Message}");
            return 0.0;
        }
    }
    #endregion
}