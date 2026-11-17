using Microsoft.Win32;
using Noxygen.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace Noxygen.Helpers;

public static class LauncherScanner
{
    public static List<Launcher> ScanInstalledLaunchers()
    {
        var launchers = new List<Launcher>();

        // 1. Steam
        string? steamPath = GetRegistryValue(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath")
                         ?? GetRegistryValue(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "SteamPath");
        if (!string.IsNullOrEmpty(steamPath))
        {
            string exe = Path.Combine(steamPath, "steam.exe");
            if (File.Exists(exe))
            {
                launchers.Add(new Launcher { Name = "Steam", ExecutablePath = exe, UriScheme = "steam://open/main" });
            }
        }

        // 2. Epic Games Launcher (Registry + All Drives Fallback)
        string? epicExe = FindFileAcrossAllDrives(@"Epic Games\Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe");
        if (!string.IsNullOrEmpty(epicExe) && File.Exists(epicExe))
        {
            launchers.Add(new Launcher { Name = "Epic Games", ExecutablePath = epicExe, UriScheme = "com.epicgames.launcher://" });
        }

        // 3. GOG Galaxy
        string? gogDir = GetRegistryValue(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths", "client");
        string gogExe = !string.IsNullOrEmpty(gogDir) ? Path.Combine(gogDir, "GalaxyClient.exe") : string.Empty;

        if (string.IsNullOrEmpty(gogExe) || !File.Exists(gogExe))
        {
            gogExe = FindFileAcrossAllDrives(@"GOG Galaxy\GalaxyClient.exe") ?? string.Empty;
        }

        if (File.Exists(gogExe))
        {
            launchers.Add(new Launcher { Name = "GOG Galaxy", ExecutablePath = gogExe });
        }

        // 4. Riot Client
        string riotExe = FindFileAcrossAllDrives(@"Riot Games\Riot Client\RiotClientServices.exe") ?? @"C:\Riot Games\Riot Client\RiotClientServices.exe";
        if (File.Exists(riotExe))
        {
            launchers.Add(new Launcher { Name = "Riot Client", ExecutablePath = riotExe, UriScheme = "riotclient://" });
        }

        // 5. EA App (Registry + All Drives Fallback)
        string? eaExe = GetRegistryValue(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Electronic Arts\EA Desktop", "Client Path")
                     ?? GetRegistryValue(Registry.LocalMachine, @"SOFTWARE\Electronic Arts\EA Desktop", "Client Path");

        if (string.IsNullOrEmpty(eaExe) || !File.Exists(eaExe))
        {
            eaExe = FindFileAcrossAllDrives(@"EA Games\EA Desktop\EA Desktop\EADesktop.exe")
                 ?? FindFileAcrossAllDrives(@"Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe");
        }

        if (!string.IsNullOrEmpty(eaExe) && File.Exists(eaExe))
        {
            launchers.Add(new Launcher { Name = "EA App", ExecutablePath = eaExe, UriScheme = "origin://" });
        }

        // 6. Ubisoft Connect (Registry InstallDir + All Drives Fallback)
        string? ubiDir = GetRegistryValue(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Ubisoft\Launcher", "InstallDir")
                      ?? GetRegistryValue(Registry.LocalMachine, @"SOFTWARE\Ubisoft\Launcher", "InstallDir");

        string ubiExe = string.Empty;
        if (!string.IsNullOrEmpty(ubiDir))
        {
            ubiExe = Path.Combine(ubiDir, "UbisoftConnect.exe");
        }

        // If registry didn't work or path doesn't exist, search across all logical drives dynamically
        if (string.IsNullOrEmpty(ubiExe) || !File.Exists(ubiExe))
        {
            ubiExe = FindFileAcrossAllDrives(@"Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe") ?? string.Empty;
        }

        if (File.Exists(ubiExe))
        {
            launchers.Add(new Launcher { Name = "Ubisoft Connect", ExecutablePath = ubiExe, UriScheme = "uplay://" });
        }

        return launchers;
    }

    private static string? GetRegistryValue(RegistryKey rootKey, string subKeyPath, string valueName)
    {
        try
        {
            using var key = rootKey.OpenSubKey(subKeyPath);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Safely scans all ready logical drives (C:, D:, E:, etc.) for a specific relative file path.
    /// This ensures custom installation drives are fully supported.
    /// </summary>
    private static string? FindFileAcrossAllDrives(string relativePath)
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                {
                    // Check standard Program Files folders first on each drive for speed
                    string[] targetFolders = new[]
                    {
                        Path.Combine(drive.RootDirectory.FullName, "Program Files", relativePath),
                        Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", relativePath),
                        Path.Combine(drive.RootDirectory.FullName, relativePath)
                    };

                    foreach (var fullPath in targetFolders)
                    {
                        if (File.Exists(fullPath))
                        {
                            return fullPath;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Drive Scanner Error] {ex.Message}");
        }

        return null;
    }
}