namespace Noxygen.Models;

public class PcSystemInfo
{
    public string DeviceName { get; set; } = "Windows PC";
    public string OsSummary { get; set; } = "Windows";
    public string PlatformSummary { get; set; } = "PC Platform";
    public string FormFactorGlyph { get; set; } = "\uE7F8";

    public string HeroProcessor { get; set; } = "—";
    public string HeroMemory { get; set; } = "—";
    public string HeroGraphics { get; set; } = "—";

    // Hardware Overview - Processor
    public string CpuShortName { get; set; } = "—";
    public string CpuSubtext { get; set; } = "Processor";
    public string CpuCoresText { get; set; } = "—";
    public string CpuThreadsText { get; set; } = "—";

    // Hardware Overview - Graphics
    public string GpuShortName { get; set; } = "—";
    public string GpuSubtext { get; set; } = "Display Adapter";
    public string DedicatedVramText { get; set; } = "—";
    public string IntegratedGpuText { get; set; } = "—";
    public string DedicatedGpuDisplay { get; set; } = "None";
    public string IntegratedGpuDisplay { get; set; } = "None";
    public bool HasDedicatedGpu { get; set; }
    public bool HasIntegratedGpu { get; set; }

    // Hardware Overview - Memory
    public string TotalRamShort { get; set; } = "—";
    public string MemorySubtext { get; set; } = "System Memory";
    public string MemoryTypeText { get; set; } = "—";
    public string InstalledRamText { get; set; } = "—";

    // Hardware Overview - Storage
    public string StorageSummaryTitle { get; set; } = "Storage";
    public string StorageSubtext { get; set; } = "Storage devices";
    public string DriveCountText { get; set; } = "—";
    public string PartitionsText { get; set; } = "—";

    // System Monitoring - CPU & RAM
    public double CpuUsagePercent { get; set; }
    public string CpuUsageText { get; set; } = "—%";
    public string CpuTempText { get; set; } = "Temperature: — °C";

    public double MemoryUsagePercent { get; set; }
    public string MemoryUsageText { get; set; } = "— GB";
    public string MemoryTotalLabel { get; set; } = "— total";
    public string MemoryAvailableText { get; set; } = "Available: — GB";

    // System Monitoring - Dedicated & Integrated GPU
    public double DedicatedGpuUsagePercent { get; set; }
    public string DedicatedGpuUsageText { get; set; } = "0%";
    public string DedicatedGpuMonitorLabel { get; set; } = "Dedicated GPU";

    public double IntegratedGpuUsagePercent { get; set; }
    public string IntegratedGpuUsageText { get; set; } = "0%";
    public string IntegratedGpuMonitorLabel { get; set; } = "Integrated GPU";

    // Keep legacy properties mapped for compatibility
    public double GpuUsagePercent { get; set; }
    public string GpuUsageText { get; set; } = "—%";
    public string GpuSubtitleText { get; set; } = "Graphics";
    public string GpuVramLabel { get; set; } = "VRAM: —";

    // System Details
    public string OperatingSystemDetail { get; set; } = "—";
    public string ArchitectureDetail { get; set; } = "—";
    public string DirectXDetail { get; set; } = "DirectX 12";
    public string FirmwareDetail { get; set; } = "—";
    public string DisplayAdapterDetail { get; set; } = "—";
    public string ComputerNameDetail { get; set; } = "—";
}