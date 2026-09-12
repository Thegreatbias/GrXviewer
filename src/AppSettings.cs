using System.Text.Json;

namespace CpuScreenViewer;

internal sealed class AppSettings
{
    public int Fps { get; set; } = 60;
    public int Scale { get; set; } = 100;
    public int FrameGen { get; set; } = 1;
    public int Upscale { get; set; }
    public int Stabilizer { get; set; } = 1;
    public bool Foveated { get; set; }
    public bool Checkerboard { get; set; }
    public bool FullScreenCapture { get; set; } = true;
    public int MonitorIndex { get; set; } = 1;
    public bool CaptureOnGpu { get; set; } = true;
    public bool GpuRender { get; set; } = true;
    public bool AutoGpu { get; set; } = true;
    public bool AutoFps { get; set; } = true;
    public int GpuIndex { get; set; }
    public int CaptureGpuIndex { get; set; }
    public string GpuName { get; set; } = "";
    public string CaptureGpuName { get; set; } = "";
    public bool TopMost { get; set; } = true;
    public bool WindowClickThrough { get; set; }
    public bool ShowFps { get; set; }
    public bool FxSharpen { get; set; }
    public bool FxColor { get; set; }
    public bool FxVignette { get; set; }
    public bool FxFog { get; set; }
    public bool FxDof { get; set; }
    public bool FxMotionBlur { get; set; }
    public int BufferView { get; set; }
    public float FsrSharpness { get; set; } = 0.2f;
    public float CasAmount { get; set; } = 0.4f;
    public float Brightness { get; set; }
    public float Contrast { get; set; } = 1f;
    public float Saturation { get; set; } = 1f;
    public float VignetteAmount { get; set; } = 0.45f;
    public float VignetteRadius { get; set; } = 0.75f;
    public float DepthMix { get; set; } = 0.25f;
    public float FogAmount { get; set; } = 0.45f;
    public float FogStart { get; set; } = 0.35f;
    public float DofAmount { get; set; } = 0.4f;
    public float DofFocus { get; set; } = 0.35f;
    public float MotionBlurAmount { get; set; } = 0.5f;
    public string EnabledShaders { get; set; } = "";
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; }
    public int WindowW { get; set; } = 1280;
    public int WindowH { get; set; } = 780;
    public int WindowState { get; set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static AppSettings? Load()
    {
        foreach (var path in Paths())
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json);
                if (loaded != null)
                    return loaded;
            }
            catch
            {
                // try next location
            }
        }
        return null;
    }

    public static void Save(AppSettings settings)
    {
        var text = JsonSerializer.Serialize(settings, Json);
        foreach (var path in Paths())
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, text);
            }
            catch
            {
                // ignore a locked or read-only location
            }
        }
    }

    private static IEnumerable<string> Paths()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GrXviewer",
            "settings.json");
        yield return Path.Combine(AppContext.BaseDirectory, "GrXviewer.settings.json");
    }
}
