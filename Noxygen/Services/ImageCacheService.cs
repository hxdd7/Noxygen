using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Noxygen.Services;

public static class ImageCacheService
{
    private static readonly HttpClient _httpClient = new();
    private static readonly string CacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Noxygen",
        "ImageCache");

    static ImageCacheService()
    {
        if (!Directory.Exists(CacheFolder))
        {
            Directory.CreateDirectory(CacheFolder);
        }
    }

    /// <summary>
    /// Downloads a web URL to the local ImageCache folder if it isn't already cached.
    /// Returns the local file path on success, or the original input if it fails/is already local.
    /// </summary>
    public static async Task<string> EnsureCachedAsync(string inputUrl, string fallbackName = "image")
    {
        if (string.IsNullOrWhiteSpace(inputUrl)) return string.Empty;

        // If it's already a local file path, keep it
        if (File.Exists(inputUrl) || inputUrl.StartsWith(CacheFolder, StringComparison.OrdinalIgnoreCase))
        {
            return inputUrl;
        }

        // Validate web URI format
        if (!Uri.TryCreate(inputUrl, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return inputUrl;
        }

        try
        {
            // Create a safe, unique filename using SHA256 of the URL
            string hash = GetMd5Hash(inputUrl);
            string ext = Path.GetExtension(uri.AbsolutePath);
            if (string.IsNullOrEmpty(ext) || ext.Length > 5) ext = ".png";

            string localPath = Path.Combine(CacheFolder, $"{fallbackName}_{hash}{ext}");

            // Return cached version if already downloaded
            if (File.Exists(localPath))
            {
                return localPath;
            }

            // Download bytes to disk
            byte[] data = await _httpClient.GetByteArrayAsync(uri);
            await File.WriteAllBytesAsync(localPath, data);

            return localPath;
        }
        catch
        {
            // Return original URL if download fails (silent fallback)
            return inputUrl;
        }
    }

    private static string GetMd5Hash(string input)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = SHA256.HashData(inputBytes);
        return Convert.ToHexString(hashBytes)[..12];
    }
}