using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Noxygen.Models;

namespace Noxygen.Services;

public class SystemInfoService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public LUID AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_QUERY_VIDEO_MEMORY_INFO
    {
        public ulong Budget;
        public ulong CurrentUsage;
        public ulong AvailableForReservation;
        public ulong CurrentReservation;
    }

    private enum DXGI_MEMORY_SEGMENT_GROUP
    {
        DXGI_MEMORY_SEGMENT_GROUP_LOCAL = 0,
        DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE
    {
        public uint CStatus;
        public double doubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM_W
    {
        public IntPtr szName;
        public PDH_FMT_COUNTERVALUE FmtValue;
    }

    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_MORE_DATA = 0x800007D2;
    private const uint PDH_CSTATUS_VALID_DATA = 0x00000000;
    private const uint PDH_CSTATUS_NEW_DATA = 0x00000001;
    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
    private const uint ERROR_SUCCESS = 0;

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterArrayW(
        IntPtr hCounter,
        uint dwFormat,
        ref uint lpdwBufferSize,
        ref uint lpdwItemCount,
        IntPtr lpBuffer);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(IntPtr hQuery);

    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid IID_IDXGIAdapter3 = new("dc8a0cdc-326f-422e-bb8f-5c4471d16366");

    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _hasPrevCpuSample;

    private IntPtr _pdhQuery = IntPtr.Zero;
    private IntPtr _pdhGpuCounter = IntPtr.Zero;
    private bool _pdhPrimed;

    private string? _dedicatedLuidKey;
    private string? _integratedLuidKey;

    private string? _cachedDdrType;
    private string? _cachedStorageTypeSummary;
    private int? _cachedPhysicalDiskCount;

    public async Task<PcSystemInfo> GetSystemInfoAsync()
    {
        return await Task.Run(() =>
        {
            var info = new PcSystemInfo
            {
                ComputerNameDetail = Environment.MachineName,
                ArchitectureDetail = RuntimeInformation.OSArchitecture switch
                {
                    Architecture.X64 => "64-bit",
                    Architecture.X86 => "32-bit",
                    Architecture.Arm64 => "ARM64",
                    _ => RuntimeInformation.OSArchitecture.ToString()
                },
                OperatingSystemDetail = GetFriendlyOsName(),
                DirectXDetail = GetDirectXVersion()
            };

            info.OsSummary = $"{info.OperatingSystemDetail} · {info.ArchitectureDetail}";

            PopulateHardwareModelAndBios(info);
            PopulateCpuInfo(info);
            PopulateMemoryInfo(info);
            PopulateGpuInfo(info);
            PopulateStorageInfo(info);
            PopulateCimHardwareDetails(info);
            PopulateLiveMonitoring(info);

            return info;
        });
    }

    public Task UpdateLiveMonitoringAsync(PcSystemInfo current)
    {
        return Task.Run(() =>
        {
            PopulateMemoryInfo(current);
            PopulateLiveMonitoring(current);
        });
    }

    private void PopulateCimHardwareDetails(PcSystemInfo info)
    {
        try
        {
            if (_cachedDdrType != null && _cachedStorageTypeSummary != null)
            {
                ApplyCachedCimDetails(info);
                return;
            }

            string script =
                "$ram = @(Get-CimInstance Win32_PhysicalMemory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty SMBIOSMemoryType); " +
                "$disks = @(Get-CimInstance MSFT_PhysicalDisk -Namespace root/Microsoft/Windows/Storage -ErrorAction SilentlyContinue | Select-Object FriendlyName, MediaType, BusType); " +
                "if ($disks.Count -eq 0) { $disks = @(Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue | Select-Object @{N='FriendlyName';E={$_.Model}}, @{N='MediaType';E={0}}, @{N='BusType';E={0}}) } " +
                "[PSCustomObject]@{ RamTypes = $ram; Disks = $disks } | ConvertTo-Json -Compress";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);

                if (!string.IsNullOrWhiteSpace(output))
                {
                    using var doc = JsonDocument.Parse(output);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("RamTypes", out var ramElem) && ramElem.ValueKind == JsonValueKind.Array)
                    {
                        var ddrLabels = new List<string>();
                        foreach (var item in ramElem.EnumerateArray())
                        {
                            if (item.TryGetInt32(out int code))
                            {
                                string ddr = code switch
                                {
                                    20 => "DDR",
                                    21 => "DDR2",
                                    22 => "DDR2 FB-DIMM",
                                    24 => "DDR3",
                                    26 => "DDR4",
                                    27 => "LPDDR",
                                    28 => "LPDDR2",
                                    29 => "LPDDR3",
                                    30 => "LPDDR4",
                                    34 => "DDR5",
                                    35 => "LPDDR5",
                                    _ => string.Empty
                                };
                                if (!string.IsNullOrEmpty(ddr) && !ddrLabels.Contains(ddr))
                                {
                                    ddrLabels.Add(ddr);
                                }
                            }
                        }

                        if (ddrLabels.Count > 0)
                        {
                            _cachedDdrType = string.Join(" / ", ddrLabels);
                        }
                    }

                    if (root.TryGetProperty("Disks", out var diskElem) && diskElem.ValueKind == JsonValueKind.Array)
                    {
                        var types = new List<string>();
                        int count = 0;

                        foreach (var d in diskElem.EnumerateArray())
                        {
                            count++;
                            int busType = d.TryGetProperty("BusType", out var bt) && bt.TryGetInt32(out var bVal) ? bVal : 0;
                            int mediaType = d.TryGetProperty("MediaType", out var mt) && mt.TryGetInt32(out var mVal) ? mVal : 0;
                            string name = d.TryGetProperty("FriendlyName", out var fn) ? (fn.GetString() ?? string.Empty) : string.Empty;

                            string label = busType == 17 || name.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
                                ? "NVMe SSD"
                                : mediaType == 4 || name.Contains("SSD", StringComparison.OrdinalIgnoreCase)
                                    ? "SSD"
                                    : mediaType == 3 || name.Contains("HDD", StringComparison.OrdinalIgnoreCase)
                                        ? "HDD"
                                        : "Drive";

                            if (!types.Contains(label))
                            {
                                types.Add(label);
                            }
                        }

                        if (count > 0)
                        {
                            _cachedPhysicalDiskCount = count;
                            _cachedStorageTypeSummary = string.Join(" + ", types);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CIM Hardware Error] {ex.Message}");
        }

        ApplyCachedCimDetails(info);
    }

    private void ApplyCachedCimDetails(PcSystemInfo info)
    {
        if (!string.IsNullOrWhiteSpace(_cachedDdrType))
        {
            info.MemoryTypeText = _cachedDdrType;
            info.MemorySubtext = $"{_cachedDdrType} System Memory";
        }

        if (!string.IsNullOrWhiteSpace(_cachedStorageTypeSummary))
        {
            info.StorageSummaryTitle = _cachedStorageTypeSummary;
            if (_cachedPhysicalDiskCount.HasValue)
            {
                info.DriveCountText = _cachedPhysicalDiskCount.Value.ToString();
            }
        }
    }

    private static string GetFriendlyOsName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string? productName = key?.GetValue("ProductName") as string;
            object? currentBuildObj = key?.GetValue("CurrentBuildNumber");
            int.TryParse(currentBuildObj?.ToString(), out int build);

            if (!string.IsNullOrWhiteSpace(productName))
            {
                if (build >= 22000 && productName.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
                {
                    productName = productName.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
                }
                return productName;
            }
        }
        catch { }

        return RuntimeInformation.OSDescription;
    }

    private static string GetDirectXVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\DirectX");
            if (key?.GetValue("Version") is string ver && !string.IsNullOrWhiteSpace(ver))
            {
                if (ver.StartsWith("4.09", StringComparison.Ordinal)) return "DirectX 9";
                if (ver.StartsWith("4.10", StringComparison.Ordinal)) return "DirectX 10";
                if (ver.StartsWith("4.11", StringComparison.Ordinal)) return "DirectX 11";
                if (ver.StartsWith("4.12", StringComparison.Ordinal)) return "DirectX 12";
            }
        }
        catch { }

        return "DirectX 12";
    }

    private static void PopulateHardwareModelAndBios(PcSystemInfo info)
    {
        string? manufacturer = null;
        string? productName = null;

        try
        {
            using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            manufacturer = biosKey?.GetValue("SystemManufacturer") as string;
            productName = biosKey?.GetValue("SystemProductName") as string;
            string? sku = biosKey?.GetValue("SystemSKU") as string;

            if (!string.IsNullOrWhiteSpace(sku) && sku.Contains("UEFI", StringComparison.OrdinalIgnoreCase))
            {
                info.FirmwareDetail = "UEFI";
            }
        }
        catch { }

        if (string.IsNullOrWhiteSpace(info.FirmwareDetail) || info.FirmwareDetail == "—")
        {
            info.FirmwareDetail = Directory.Exists(@"C:\Windows\Panther") ? "UEFI" : "BIOS";
        }

        string combinedModel = string.Join(" ", new[] { manufacturer?.Trim(), productName?.Trim() }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        info.DeviceName = !string.IsNullOrWhiteSpace(combinedModel)
            ? combinedModel
            : Environment.MachineName;

        bool looksLikeLaptop =
            info.DeviceName.Contains("Laptop", StringComparison.OrdinalIgnoreCase) ||
            info.DeviceName.Contains("Book", StringComparison.OrdinalIgnoreCase) ||
            info.DeviceName.Contains("Aspire", StringComparison.OrdinalIgnoreCase) ||
            info.DeviceName.Contains("ThinkPad", StringComparison.OrdinalIgnoreCase) ||
            info.DeviceName.Contains("MateBook", StringComparison.OrdinalIgnoreCase) ||
            info.DeviceName.Contains("ZenBook", StringComparison.OrdinalIgnoreCase) ||
            info.DeviceName.Contains("Pavilion", StringComparison.OrdinalIgnoreCase) ||
            info.DeviceName.Contains("Inspiron", StringComparison.OrdinalIgnoreCase) ||
            info.DeviceName.Contains("ROG", StringComparison.OrdinalIgnoreCase) ||
            info.DeviceName.Contains("TUF", StringComparison.OrdinalIgnoreCase);

        info.FormFactorGlyph = looksLikeLaptop ? "\uE7F8" : "\uE977";
    }

    private static void PopulateCpuInfo(PcSystemInfo info)
    {
        int threads = Environment.ProcessorCount;
        info.CpuThreadsText = threads > 0 ? threads.ToString() : "—";

        string? cpuName = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpuName = (key?.GetValue("ProcessorNameString") as string)?.Trim();
        }
        catch { }

        if (string.IsNullOrWhiteSpace(cpuName))
        {
            cpuName = "Processor";
        }

        info.HeroProcessor = cpuName;

        var match = Regex.Match(
            cpuName,
            @"(?:Core\s+)?(i[3579]|Ultra\s*[579]|Ryzen\s*[3579]\s*\w*|Ryzen\s*[3579]|Xeon\s*\w*|Snapdragon\s*\w+)",
            RegexOptions.IgnoreCase);

        if (match.Success)
        {
            string raw = match.Value.Trim();
            info.CpuShortName = Regex.IsMatch(raw, @"^i[3579]$", RegexOptions.IgnoreCase)
                ? $"Core {raw.ToUpperInvariant()}"
                : raw;
        }
        else
        {
            info.CpuShortName = string.Join(" ", cpuName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3));
        }

        var genMatch = Regex.Match(cpuName, @"(\d{1,2})(?:th|st|nd|rd)\s+Gen", RegexOptions.IgnoreCase);
        info.CpuSubtext = genMatch.Success
            ? $"{genMatch.Groups[1].Value}th Generation"
            : (cpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel Processor"
                : cpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? "AMD Processor"
                : "Processor");

        info.PlatformSummary = $"{info.CpuSubtext} platform";

        int estimatedCores = threads >= 4 && threads % 2 == 0 ? threads / 2 : threads;
        info.CpuCoresText = estimatedCores > 0 ? estimatedCores.ToString() : "—";
    }

    private static void PopulateMemoryInfo(PcSystemInfo info)
    {
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref memStatus))
        {
            return;
        }

        double totalGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
        double availGb = memStatus.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
        double usedGb = Math.Max(0, totalGb - availGb);

        int roundedTotalGb = Math.Max(1, (int)Math.Round(totalGb));
        string totalFormatted = totalGb >= 1 && Math.Abs(totalGb - roundedTotalGb) < 0.25
            ? $"{roundedTotalGb} GB"
            : $"{totalGb:0.0} GB";

        info.HeroMemory = totalFormatted;
        info.TotalRamShort = totalFormatted;
        info.InstalledRamText = totalFormatted;
        info.MemoryTotalLabel = $"{totalFormatted} total";
        info.MemoryUsageText = $"{usedGb:0.0} GB";
        info.MemoryAvailableText = $"Available: {availGb:0.0} GB";
        info.MemoryUsagePercent = memStatus.ullTotalPhys > 0
            ? Math.Clamp((usedGb / totalGb) * 100.0, 0.0, 100.0)
            : 0.0;

        if (string.IsNullOrWhiteSpace(info.MemoryTypeText) || info.MemoryTypeText == "—")
        {
            info.MemoryTypeText = "System RAM";
        }
        if (string.IsNullOrWhiteSpace(info.MemorySubtext) || info.MemorySubtext == "—")
        {
            info.MemorySubtext = "System Memory";
        }
    }

    private void PopulateGpuInfo(PcSystemInfo info)
    {
        var adapters = new List<(string Name, ulong DedicatedBytes, string LuidKey)>();

        IntPtr factoryPtr = IntPtr.Zero;
        try
        {
            Guid factoryIid = IID_IDXGIFactory1;
            if (CreateDXGIFactory1(ref factoryIid, out factoryPtr) >= 0 && factoryPtr != IntPtr.Zero)
            {
                for (uint i = 0; i < 8; i++)
                {
                    IntPtr adapterPtr = EnumAdapters1(factoryPtr, i);
                    if (adapterPtr == IntPtr.Zero) break;

                    try
                    {
                        if (TryGetAdapterDesc1(adapterPtr, out var desc) &&
                            (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) == 0 &&
                            !string.IsNullOrWhiteSpace(desc.Description))
                        {
                            string luidKey = FormatLuidKey(desc.AdapterLuid);
                            adapters.Add((
                                desc.Description.Trim(),
                                desc.DedicatedVideoMemory.ToUInt64(),
                                luidKey));
                        }
                    }
                    finally
                    {
                        ReleaseComPtr(adapterPtr);
                    }
                }
            }
        }
        catch { }
        finally
        {
            ReleaseComPtr(factoryPtr);
        }

        if (adapters.Count == 0)
        {
            try
            {
                using var videoKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (videoKey != null)
                {
                    foreach (string subKeyName in videoKey.GetSubKeyNames())
                    {
                        if (!int.TryParse(subKeyName, out _)) continue;
                        using var sub = videoKey.OpenSubKey(subKeyName);
                        string? desc = (sub?.GetValue("DriverDesc") as string)?.Trim();
                        if (!string.IsNullOrWhiteSpace(desc) && !adapters.Any(a => a.Name.Equals(desc, StringComparison.OrdinalIgnoreCase)))
                        {
                            adapters.Add((desc, 0, string.Empty));
                        }
                    }
                }
            }
            catch { }
        }

        var discrete = adapters.FirstOrDefault(g =>
            g.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            g.Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
            g.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
            g.Name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
            g.Name.Contains("MX", StringComparison.OrdinalIgnoreCase) ||
            g.Name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
            g.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase));

        var integrated = adapters.FirstOrDefault(g =>
            !string.Equals(g.Name, discrete.Name, StringComparison.OrdinalIgnoreCase) &&
            (g.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
             g.Name.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
             g.Name.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
             g.Name.Contains("Graphics", StringComparison.OrdinalIgnoreCase)));

        info.HasDedicatedGpu = !string.IsNullOrWhiteSpace(discrete.Name);
        info.HasIntegratedGpu = !string.IsNullOrWhiteSpace(integrated.Name);

        _dedicatedLuidKey = info.HasDedicatedGpu && !string.IsNullOrEmpty(discrete.LuidKey) ? discrete.LuidKey : null;
        _integratedLuidKey = info.HasIntegratedGpu && !string.IsNullOrEmpty(integrated.LuidKey) ? integrated.LuidKey : null;

        info.DedicatedGpuDisplay = info.HasDedicatedGpu ? discrete.Name : "None";
        info.IntegratedGpuDisplay = info.HasIntegratedGpu ? integrated.Name : "None";

        info.DedicatedGpuMonitorLabel = info.HasDedicatedGpu ? discrete.Name : "Dedicated GPU";
        info.IntegratedGpuMonitorLabel = info.HasIntegratedGpu ? integrated.Name : "Integrated GPU";

        info.IntegratedGpuText = info.HasIntegratedGpu ? ExtractShortGpuName(integrated.Name) : "—";

        string primaryName = info.HasDedicatedGpu
            ? discrete.Name
            : (adapters.FirstOrDefault().Name ?? "Display Adapter");

        info.HeroGraphics = primaryName;
        info.DisplayAdapterDetail = info.HasDedicatedGpu && info.HasIntegratedGpu
            ? $"{discrete.Name} + {integrated.Name}"
            : primaryName;

        info.GpuSubtitleText = info.HasDedicatedGpu && info.HasIntegratedGpu
            ? $"{discrete.Name} / {integrated.Name}"
            : primaryName;

        string vendor = primaryName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                        primaryName.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                        primaryName.Contains("MX", StringComparison.OrdinalIgnoreCase)
            ? "NVIDIA"
            : primaryName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
              primaryName.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
                ? "AMD"
                : primaryName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                  primaryName.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
                  primaryName.Contains("UHD", StringComparison.OrdinalIgnoreCase)
                    ? "Intel"
                    : "Graphics";

        string shortGpu = ExtractShortGpuName(primaryName);
        info.GpuShortName = shortGpu.StartsWith(vendor, StringComparison.OrdinalIgnoreCase)
            ? shortGpu
            : $"{vendor} {shortGpu}";

        info.GpuSubtext = info.HasDedicatedGpu ? $"{vendor} Dedicated" : $"{vendor} Adapter";

        PopulateDxgiVramInfo(info);
    }

    private static string FormatLuidKey(LUID luid) =>
        $"0x{luid.HighPart:x8}_0x{luid.LowPart:x8}";

    private static string ExtractShortGpuName(string fullName)
    {
        var match = Regex.Match(
            fullName,
            @"(RTX\s*\d+\s*\w*|GTX\s*\d+\s*\w*|MX\d+|Arc\s*\w+|RX\s*\d+\s*\w*|Iris\s*\w+|UHD\s*Graphics)",
            RegexOptions.IgnoreCase);

        if (match.Success)
        {
            return match.Value.Trim();
        }

        return string.Join(" ", fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3));
    }

    private static void PopulateStorageInfo(PcSystemInfo info)
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .ToList();

            int partitionCount = drives.Count;
            info.PartitionsText = partitionCount > 0 ? partitionCount.ToString() : "—";

            if (partitionCount == 0)
            {
                info.StorageSummaryTitle = "No Drives";
                info.DriveCountText = "0";
                return;
            }

            long totalBytes = drives.Sum(d => d.TotalSize);
            double totalTb = totalBytes / (1024.0 * 1024.0 * 1024.0 * 1024.0);
            double totalGb = totalBytes / (1024.0 * 1024.0 * 1024.0);

            string capText = totalTb >= 1.0
                ? $"{totalTb:0.0} TB"
                : $"{Math.Round(totalGb)} GB";

            info.StorageSummaryTitle = capText;
            info.DriveCountText = partitionCount == 1 ? "1 volume" : $"{partitionCount} volumes";
            info.StorageSubtext = $"{partitionCount} active {(partitionCount == 1 ? "drive" : "drives")}";
        }
        catch
        {
            info.StorageSummaryTitle = "Storage";
            info.DriveCountText = "—";
            info.PartitionsText = "—";
        }
    }

    private void PopulateLiveMonitoring(PcSystemInfo info)
    {
        if (GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
        {
            ulong idle = ToUInt64(idleFt);
            ulong kernel = ToUInt64(kernelFt);
            ulong user = ToUInt64(userFt);

            if (_hasPrevCpuSample)
            {
                ulong idleDiff = idle - _prevIdle;
                ulong totalDiff = (kernel - _prevKernel) + (user - _prevUser);

                if (totalDiff > 0 && totalDiff >= idleDiff)
                {
                    double usage = ((totalDiff - idleDiff) / (double)totalDiff) * 100.0;
                    info.CpuUsagePercent = Math.Clamp(usage, 0.0, 100.0);
                    info.CpuUsageText = $"{Math.Round(info.CpuUsagePercent)}%";
                }
            }

            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _hasPrevCpuSample = true;

            if (info.CpuUsageText == "—%")
            {
                info.CpuUsageText = "0%";
            }
        }

        info.CpuTempText = "Temperature: — °C";
        PopulatePerGpuUsagePdh(info);
        PopulateDxgiVramInfo(info);
    }

    private void PopulatePerGpuUsagePdh(PcSystemInfo info)
    {
        try
        {
            if (_pdhQuery == IntPtr.Zero || _pdhGpuCounter == IntPtr.Zero)
            {
                ClosePdhQuery();
                if (PdhOpenQueryW(null, IntPtr.Zero, out _pdhQuery) != ERROR_SUCCESS || _pdhQuery == IntPtr.Zero)
                {
                    SetGpuZero(info);
                    return;
                }

                uint addStatus = PdhAddEnglishCounterW(
                    _pdhQuery,
                    @"\GPU Engine(*)\Utilization Percentage",
                    IntPtr.Zero,
                    out _pdhGpuCounter);

                if (addStatus != ERROR_SUCCESS || _pdhGpuCounter == IntPtr.Zero)
                {
                    ClosePdhQuery();
                    SetGpuZero(info);
                    return;
                }

                PdhCollectQueryData(_pdhQuery);
                Thread.Sleep(60);
                _pdhPrimed = true;
            }

            if (PdhCollectQueryData(_pdhQuery) != ERROR_SUCCESS || !_pdhPrimed)
            {
                SetGpuZero(info);
                return;
            }

            uint bufSize = 0;
            uint itemCount = 0;
            uint fmtStatus = PdhGetFormattedCounterArrayW(
                _pdhGpuCounter,
                PDH_FMT_DOUBLE,
                ref bufSize,
                ref itemCount,
                IntPtr.Zero);

            if (fmtStatus != PDH_MORE_DATA || bufSize == 0)
            {
                SetGpuZero(info);
                return;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)bufSize);
            try
            {
                fmtStatus = PdhGetFormattedCounterArrayW(
                    _pdhGpuCounter,
                    PDH_FMT_DOUBLE,
                    ref bufSize,
                    ref itemCount,
                    buffer);

                if (fmtStatus != ERROR_SUCCESS || itemCount == 0)
                {
                    SetGpuZero(info);
                    return;
                }

                int stride = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM_W>();
                // Map: luidKey -> (engineType -> summed utilization across PIDs for that engine)
                var perLuidEngineTotals = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

                for (uint i = 0; i < itemCount; i++)
                {
                    IntPtr itemPtr = IntPtr.Add(buffer, (int)(i * stride));
                    var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM_W>(itemPtr);

                    if ((item.FmtValue.CStatus != PDH_CSTATUS_VALID_DATA && item.FmtValue.CStatus != PDH_CSTATUS_NEW_DATA) ||
                        double.IsNaN(item.FmtValue.doubleValue) ||
                        double.IsInfinity(item.FmtValue.doubleValue) ||
                        item.FmtValue.doubleValue <= 0 ||
                        item.szName == IntPtr.Zero)
                    {
                        continue;
                    }

                    string instanceName = Marshal.PtrToStringUni(item.szName) ?? string.Empty;
                    var luidMatch = Regex.Match(instanceName, @"luid_(0x[0-9a-fA-F]+_0x[0-9a-fA-F]+)", RegexOptions.IgnoreCase);
                    if (!luidMatch.Success) continue;

                    string luidKey = luidMatch.Groups[1].Value;
                    var engMatch = Regex.Match(instanceName, @"engtype_([A-Za-z0-9_]+)$", RegexOptions.IgnoreCase);
                    string engType = engMatch.Success ? engMatch.Groups[1].Value : "Default";

                    if (!perLuidEngineTotals.TryGetValue(luidKey, out var engMap))
                    {
                        engMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                        perLuidEngineTotals[luidKey] = engMap;
                    }

                    engMap[engType] = (engMap.TryGetValue(engType, out var cur) ? cur : 0.0) + item.FmtValue.doubleValue;
                }

                double dedUsage = GetUtilizationForLuid(perLuidEngineTotals, _dedicatedLuidKey);
                double igpUsage = GetUtilizationForLuid(perLuidEngineTotals, _integratedLuidKey);

                info.DedicatedGpuUsagePercent = Math.Clamp(dedUsage, 0.0, 100.0);
                info.DedicatedGpuUsageText = $"{Math.Round(info.DedicatedGpuUsagePercent)}%";

                info.IntegratedGpuUsagePercent = Math.Clamp(igpUsage, 0.0, 100.0);
                info.IntegratedGpuUsageText = $"{Math.Round(info.IntegratedGpuUsagePercent)}%";

                // Primary GpuUsagePercent reflects dedicated if available, otherwise integrated
                info.GpuUsagePercent = info.HasDedicatedGpu ? info.DedicatedGpuUsagePercent : info.IntegratedGpuUsagePercent;
                info.GpuUsageText = info.HasDedicatedGpu ? info.DedicatedGpuUsageText : info.IntegratedGpuUsageText;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PDH GPU Error] {ex.Message}");
            ClosePdhQuery();
            SetGpuZero(info);
        }
    }

    private static double GetUtilizationForLuid(
        Dictionary<string, Dictionary<string, double>> perLuidEngineTotals,
        string? luidKey)
    {
        if (string.IsNullOrEmpty(luidKey) || !perLuidEngineTotals.TryGetValue(luidKey, out var engMap) || engMap.Count == 0)
        {
            return 0.0;
        }

        // Task Manager reports the highest utilization across GPU engine groups (3D, Copy, Video, Compute)
        return Math.Clamp(engMap.Values.Max(), 0.0, 100.0);
    }

    private static void SetGpuZero(PcSystemInfo info)
    {
        info.DedicatedGpuUsagePercent = 0;
        info.DedicatedGpuUsageText = "0%";
        info.IntegratedGpuUsagePercent = 0;
        info.IntegratedGpuUsageText = "0%";
        info.GpuUsagePercent = 0;
        info.GpuUsageText = "0%";
    }

    private void ClosePdhQuery()
    {
        if (_pdhQuery != IntPtr.Zero)
        {
            try { PdhCloseQuery(_pdhQuery); } catch { }
            _pdhQuery = IntPtr.Zero;
        }
        _pdhGpuCounter = IntPtr.Zero;
        _pdhPrimed = false;
    }

    private static void PopulateDxgiVramInfo(PcSystemInfo info)
    {
        IntPtr factoryPtr = IntPtr.Zero;
        try
        {
            Guid factoryIid = IID_IDXGIFactory1;
            int hr = CreateDXGIFactory1(ref factoryIid, out factoryPtr);
            if (hr < 0 || factoryPtr == IntPtr.Zero)
            {
                return;
            }

            for (uint index = 0; index < 8; index++)
            {
                IntPtr adapterPtr = EnumAdapters1(factoryPtr, index);
                if (adapterPtr == IntPtr.Zero)
                {
                    break;
                }

                try
                {
                    IntPtr adapter3Ptr = QueryInterfacePtr(adapterPtr, IID_IDXGIAdapter3);
                    if (adapter3Ptr == IntPtr.Zero)
                    {
                        continue;
                    }

                    try
                    {
                        if (TryQueryVideoMemoryInfo(
                                adapter3Ptr,
                                DXGI_MEMORY_SEGMENT_GROUP.DXGI_MEMORY_SEGMENT_GROUP_LOCAL,
                                out var localMem) &&
                            localMem.Budget > 0)
                        {
                            double usedGb = localMem.CurrentUsage / (1024.0 * 1024.0 * 1024.0);
                            double budgetGb = localMem.Budget / (1024.0 * 1024.0 * 1024.0);

                            string usedStr = usedGb >= 0.1
                                ? $"{usedGb:0.0} GB"
                                : $"{Math.Round(localMem.CurrentUsage / (1024.0 * 1024.0))} MB";

                            string budgetStr = budgetGb >= 1.0
                                ? $"{budgetGb:0.0} GB"
                                : $"{Math.Round(localMem.Budget / (1024.0 * 1024.0))} MB";

                            info.GpuVramLabel = $"VRAM: {usedStr} / {budgetStr}";
                            info.DedicatedVramText = budgetStr;
                            return;
                        }
                    }
                    finally
                    {
                        ReleaseComPtr(adapter3Ptr);
                    }
                }
                finally
                {
                    ReleaseComPtr(adapterPtr);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DXGI VRAM Error] {ex.Message}");
        }
        finally
        {
            ReleaseComPtr(factoryPtr);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterfaceDelegate(IntPtr pThis, ref Guid riid, out IntPtr ppvObject);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(IntPtr pThis);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr pThis, uint adapterIndex, out IntPtr ppAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr pThis, out DXGI_ADAPTER_DESC1 pDesc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryVideoMemoryInfoDelegate(
        IntPtr pThis,
        uint nodeIndex,
        DXGI_MEMORY_SEGMENT_GROUP memorySegmentGroup,
        out DXGI_QUERY_VIDEO_MEMORY_INFO pVideoMemoryInfo);

    private static IntPtr GetVtableSlot(IntPtr pComObj, int slotIndex)
    {
        IntPtr vtable = Marshal.ReadIntPtr(pComObj);
        return Marshal.ReadIntPtr(vtable, slotIndex * IntPtr.Size);
    }

    private static IntPtr QueryInterfacePtr(IntPtr pComObj, Guid iid)
    {
        var qi = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(GetVtableSlot(pComObj, 0));
        int hr = qi(pComObj, ref iid, out IntPtr ppv);
        return hr >= 0 ? ppv : IntPtr.Zero;
    }

    private static void ReleaseComPtr(IntPtr pComObj)
    {
        if (pComObj == IntPtr.Zero) return;
        var rel = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(GetVtableSlot(pComObj, 2));
        rel(pComObj);
    }

    private static IntPtr EnumAdapters1(IntPtr pFactory1, uint index)
    {
        var enumAdapters = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(GetVtableSlot(pFactory1, 12));
        int hr = enumAdapters(pFactory1, index, out IntPtr pAdapter);
        return hr >= 0 ? pAdapter : IntPtr.Zero;
    }

    private static bool TryGetAdapterDesc1(IntPtr pAdapter1, out DXGI_ADAPTER_DESC1 desc)
    {
        desc = default;
        // IDXGIAdapter1::GetDesc1 is vtable slot 10: IUnknown [0..2], IDXGIObject [3..6], IDXGIAdapter [7..9], IDXGIAdapter1 [10]
        var getDesc1 = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(GetVtableSlot(pAdapter1, 10));
        return getDesc1(pAdapter1, out desc) >= 0;
    }

    private static bool TryQueryVideoMemoryInfo(
        IntPtr pAdapter3,
        DXGI_MEMORY_SEGMENT_GROUP group,
        out DXGI_QUERY_VIDEO_MEMORY_INFO memInfo)
    {
        memInfo = default;
        var queryMem = Marshal.GetDelegateForFunctionPointer<QueryVideoMemoryInfoDelegate>(GetVtableSlot(pAdapter3, 14));
        int hr = queryMem(pAdapter3, 0, group, out memInfo);
        return hr >= 0;
    }

    private static ulong ToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME ft) =>
        ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
}