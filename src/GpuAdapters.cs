using Vortice.DXGI;
using Vortice.Mathematics;

namespace CpuScreenViewer;

internal enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel,
    Microsoft,
}

internal readonly record struct GpuOutput(Rectangle Bounds, int RefreshHz);

internal readonly record struct GpuAdapter(
    int Index,
    string Name,
    long VramMb,
    bool Software,
    GpuVendor Vendor,
    GpuOutput[] Outputs)
{
    public bool HasOutputs => Outputs.Length > 0;

    public int MaxRefreshHz
    {
        get
        {
            var max = 0;
            foreach (var output in Outputs)
            {
                if (output.RefreshHz > max)
                    max = output.RefreshHz;
            }
            return max;
        }
    }

    public int OverlapArea(Rectangle target)
    {
        var area = 0;
        foreach (var output in Outputs)
        {
            var hit = Rectangle.Intersect(output.Bounds, target);
            if (hit.Width > 0 && hit.Height > 0)
                area += hit.Width * hit.Height;
        }
        return area;
    }
}

internal static class GpuAdapters
{
    public static List<GpuAdapter> List()
    {
        var list = new List<GpuAdapter>();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint i = 0; ; i++)
        {
            if (factory.EnumAdapters1(i, out var adapter).Failure)
                break;
            using (adapter)
            {
                var d = adapter.Description1;
                var name = (d.Description ?? $"GPU {i}").TrimEnd('\0').Trim();
                var mb = (long)(d.DedicatedVideoMemory / (1024 * 1024));
                var software = (d.Flags & AdapterFlags.Software) != 0;
                var vendor = ParseVendor(d.VendorId, name);
                var outputs = new List<GpuOutput>();
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    using (output)
                    {
                        var desk = ToRect(output.Description.DesktopCoordinates);
                        var device = (output.Description.DeviceName ?? "").TrimEnd('\0').Trim();
                        outputs.Add(new GpuOutput(desk, Native.DisplayRefreshHz(device)));
                    }
                }
                list.Add(new GpuAdapter((int)i, name, mb, software, vendor, outputs.ToArray()));
            }
        }
        return list;
    }

    public static GpuVendor ParseVendor(uint vendorId, string name)
    {
        if (vendorId == 0x10DE) return GpuVendor.Nvidia;
        if (vendorId is 0x1002 or 0x1022) return GpuVendor.Amd;
        if (vendorId == 0x8086) return GpuVendor.Intel;
        if (vendorId == 0x1414) return GpuVendor.Microsoft;
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Nvidia;
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Amd;
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Intel;
        return GpuVendor.Unknown;
    }

    public static int DefaultHardwareIndex(IReadOnlyList<GpuAdapter> gpus)
    {
        var best = -1;
        var bestScore = long.MinValue;
        foreach (var gpu in gpus)
        {
            if (gpu.Software)
                continue;
            var score = (gpu.HasOutputs ? 1_000_000_000L : 0L) + gpu.VramMb;
            if (score > bestScore)
            {
                bestScore = score;
                best = gpu.Index;
            }
        }
        if (best >= 0)
            return best;
        return gpus.Count > 0 ? gpus[0].Index : 0;
    }

    public static int CaptureIndexFor(IReadOnlyList<GpuAdapter> gpus, Rectangle target)
    {
        var best = -1;
        var bestArea = -1;
        var bestVram = -1L;
        foreach (var gpu in gpus)
        {
            if (gpu.Software)
                continue;
            var area = gpu.OverlapArea(target);
            if (area > bestArea || (area == bestArea && gpu.VramMb > bestVram))
            {
                bestArea = area;
                bestVram = gpu.VramMb;
                best = gpu.Index;
            }
        }
        return best >= 0 && bestArea > 0 ? best : DefaultHardwareIndex(gpus);
    }

    public static int RefreshFor(IReadOnlyList<GpuAdapter> gpus, Rectangle target)
    {
        var max = 0;
        foreach (var gpu in gpus)
        {
            foreach (var output in gpu.Outputs)
            {
                var hit = Rectangle.Intersect(output.Bounds, target);
                if (hit.Width <= 0 || hit.Height <= 0)
                    continue;
                if (output.RefreshHz > max)
                    max = output.RefreshHz;
            }
        }
        if (max <= 0)
        {
            foreach (var gpu in gpus)
            {
                if (gpu.MaxRefreshHz > max)
                    max = gpu.MaxRefreshHz;
            }
        }
        if (max is 29 or 59 or 119)
            max++;
        return max > 0 ? Math.Clamp(max, 1, 240) : 60;
    }

    public static GpuAdapter? Find(IReadOnlyList<GpuAdapter> gpus, int index)
    {
        foreach (var gpu in gpus)
        {
            if (gpu.Index == index)
                return gpu;
        }
        return null;
    }

    public static string Label(GpuAdapter gpu)
    {
        var extra = gpu.Software ? "software" : gpu.VramMb > 0 ? $"{gpu.VramMb} MB" : "";
        if (gpu.HasOutputs)
            extra = string.IsNullOrEmpty(extra) ? "display" : extra + ", display";
        return string.IsNullOrEmpty(extra) ? gpu.Name : $"{gpu.Name}  ({extra})";
    }

    public static string ShortName(string name)
    {
        name = name.Replace("NVIDIA ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("AMD ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Intel(R) ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Intel ", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
        return name.Length <= 40 ? name : name[..37] + "...";
    }

    private static Rectangle ToRect(RectI r) => Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
}
