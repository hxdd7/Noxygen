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

        try { games.AddRange(ScanSteamGames()); } catch { }
        try { games.AddRange(ScanEpicGames()); } catch { }
        try { games.AddRange(ScanGogGames()); } catch { }

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

                    if (nameMatch.Success && dirMatch.Success)
                    {
                        string title = nameMatch.Groups[1].Value;

                        // 1. Filter out non-game software, tools, and dedicated servers
                        if (title.StartsWith("Steamworks") ||
                            title.StartsWith("Proton") ||
                            title.EndsWith("Server") ||
                            title.EndsWith("Dedicated Server") ||
                            title.Contains("Test Server"))
                            continue;

                        string folderName = dirMatch.Groups[1].Value;
                        string fullPath = Path.Combine(libPath, "common", folderName);

                        // 2. Prevent adding duplicate games with identical titles
                        if (games.Exists(g => g.Title.Equals(title, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        games.Add(new Game
                        {
                            Title = title,
                            InstallPath = fullPath,
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

                string title = root.GetProperty("DisplayName").GetString() ?? "";
                string installPath = root.GetProperty("InstallLocation").GetString() ?? "";

                if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                {
                    games.Add(new Game
                    {
                        Title = title,
                        InstallPath = installPath,
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

                if (!string.IsNullOrEmpty(title) && Directory.Exists(path))
                {
                    games.Add(new Game
                    {
                        Title = title,
                        InstallPath = path,
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

    #region Safe Directory Calculation
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