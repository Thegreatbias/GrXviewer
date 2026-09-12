using System.Diagnostics;
using Microsoft.Win32;

namespace CpuScreenViewer;

internal sealed class HardwareProfile
{
    public int PresentGpuIndex { get; init; }
    public int CaptureGpuIndex { get; init; }
    public int Fps { get; init; }
    public bool CaptureOnGpu { get; init; }
    public string Summary { get; init; } = "";

    public static HardwareProfile Detect(IReadOnlyList<GpuAdapter> gpus, Rectangle captureBounds)
    {
        var capture = GpuAdapters.CaptureIndexFor(gpus, captureBounds);
        // Keep present on the same GPU as capture so the shared-texture path stays on one adapter.
        var present = capture;
        var fps = GpuAdapters.RefreshFor(gpus, captureBounds);
        var gpu = GpuAdapters.Find(gpus, present);
        var captureOnGpu = gpu is { Software: false };
        CpuTune.EnsureCached();

        var bits = new List<string>();
        if (gpu is { } g && !string.IsNullOrWhiteSpace(g.Name))
            bits.Add(GpuAdapters.ShortName(g.Name));
        bits.Add($"{fps} Hz");
        if (CpuTune.HasEfficiencyCores)
            bits.Add("P-cores");
        else if (!string.IsNullOrEmpty(CpuTune.ShortName))
            bits.Add(CpuTune.ShortName);

        return new HardwareProfile
        {
            PresentGpuIndex = present,
            CaptureGpuIndex = capture,
            Fps = fps,
            CaptureOnGpu = captureOnGpu,
            Summary = "Detected: " + string.Join(" · ", bits),
        };
    }
}

internal static class CpuTune
{
    private static uint[] _perfCpuSets = [];
    private static bool _cached;

    public static string ShortName { get; private set; } = "";
    public static bool HasEfficiencyCores { get; private set; }

    public static void EnsureCached()
    {
        if (_cached)
            return;
        _cached = true;
        ShortName = ReadCpuShortName();
        _perfCpuSets = Native.PerformanceCpuSetIds();
        HasEfficiencyCores = _perfCpuSets.Length > 0;
    }

    public static void ApplyProcess()
    {
        EnsureCached();
        try
        {
            var proc = Process.GetCurrentProcess();
            proc.PriorityClass = HasEfficiencyCores
                ? ProcessPriorityClass.High
                : ProcessPriorityClass.AboveNormal;
        }
        catch
        {
            // ignore
        }
    }

    public static void ApplyToCurrentThread()
    {
        EnsureCached();
        Native.PreferFastThread();
        Native.PinToCpuSets(_perfCpuSets);
        try { Thread.CurrentThread.Priority = ThreadPriority.Highest; }
        catch { /* ignore */ }
    }

    private static string ReadCpuShortName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var raw = key?.GetValue("ProcessorNameString") as string;
            if (string.IsNullOrWhiteSpace(raw))
                return "";
            raw = raw.Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
                .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
                .Replace("CPU", "", StringComparison.OrdinalIgnoreCase)
                .Trim();
            if (raw.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                return "Intel";
            if (raw.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                return "AMD";
            return raw.Length <= 18 ? raw : raw[..18].Trim();
        }
        catch
        {
            return "";
        }
    }
}
