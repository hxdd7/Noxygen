using System.Diagnostics;

namespace Noxygen.Helpers;

public static class ProcessRunner
{
    public static bool OpenFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !System.IO.Directory.Exists(folderPath))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true,
                Verb = "open"
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool LaunchExecutable(string exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !System.IO.File.Exists(exePath))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = System.IO.Path.GetDirectoryName(exePath),
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}