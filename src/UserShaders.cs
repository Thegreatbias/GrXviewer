using System.Text;
using System.Text.RegularExpressions;

namespace CpuScreenViewer;

internal sealed class ShaderFile
{
    public required string FileName { get; init; }
    public required string DisplayName { get; init; }
    public required string Path { get; init; }
    public required string Id { get; init; }
    public bool Enabled { get; set; }
    public bool Returns4 { get; set; }
    public string? Error { get; set; }
}

internal static class UserShaders
{
    public const int MaxCount = 32;
    public static string Folder { get; } = Path.Combine(AppContext.BaseDirectory, "shaders");
    public static string Status { get; private set; } = "";
    private static readonly List<ShaderFile> _files = [];
    private static readonly HashSet<string> _enabled = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ShaderFile> Files => _files;
    public static uint Mask { get; private set; }

    public static void EnsureFolder()
    {
        Directory.CreateDirectory(Folder);
        var example = Path.Combine(Folder, "example-tint.hlsl");
        if (!File.Exists(example))
            File.WriteAllText(example, ExampleHlsl);
    }

    public static void ApplySaved(string? names)
    {
        _enabled.Clear();
        if (string.IsNullOrWhiteSpace(names))
            return;
        foreach (var part in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _enabled.Add(part);
    }

    public static string SavedNames() => string.Join(",", _enabled);

    public static void Scan()
    {
        EnsureFolder();
        _files.Clear();
        var used = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var path in Directory.EnumerateFiles(Folder, "*.hlsl").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var file = Path.GetFileName(path);
                if (file.StartsWith('_'))
                    continue;
                if (_files.Count >= MaxCount)
                    break;
                var id = MakeId(Path.GetFileNameWithoutExtension(file), used);
                var display = Path.GetFileNameWithoutExtension(file).Replace('-', ' ').Replace('_', ' ');
                var item = new ShaderFile
                {
                    FileName = file,
                    DisplayName = display,
                    Path = path,
                    Id = id,
                    Enabled = _enabled.Contains(file),
                };
                Prepare(item);
                _files.Add(item);
            }
        }
        catch (Exception ex)
        {
            RebuildMask();
            Status = ex.Message;
            return;
        }
        RebuildMask();
        Status = _files.Count == 0
            ? "Drop .hlsl files into the shaders folder"
            : _files.Count + " shader" + (_files.Count == 1 ? "" : "s");
    }

    public static void SetEnabled(string fileName, bool on)
    {
        if (on) _enabled.Add(fileName);
        else _enabled.Remove(fileName);
        foreach (var file in _files)
        {
            if (file.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                file.Enabled = on && file.Error is null;
        }
        RebuildMask();
    }

    public static void OpenFolder()
    {
        try
        {
            EnsureFolder();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Folder,
                UseShellExecute = true,
            });
        }
        catch
        {
            // ignore
        }
    }

    public static string PixelSource()
    {
        var functions = new StringBuilder();
        var calls = new StringBuilder();
        var bit = 1u;
        foreach (var file in _files)
        {
            if (!file.Enabled || file.Error != null)
            {
                bit <<= 1;
                continue;
            }
            functions.AppendLine(LoadFunction(file));
            var expr = file.Returns4 ? $"{file.Id}(uv, color).rgb" : $"{file.Id}(uv, color)";
            calls.AppendLine($"    if (UserMask & {bit})");
            calls.AppendLine($"        color = {expr};");
            bit <<= 1;
        }

        var src = UpscaleShaders.Hlsl;
        src = src.Replace(
            "float4 PSMain(VSOut i) : SV_Target",
            functions + "float4 PSMain(VSOut i) : SV_Target",
            StringComparison.Ordinal);
        src = src.Replace("// USER_SHADERS", calls.ToString(), StringComparison.Ordinal);
        return src;
    }

    public static string? CompileError { get; private set; }

    public static byte[]? TryCompilePixel()
    {
        try
        {
            CompileError = null;
            return UpscaleShaders.CompileSource(PixelSource(), "PSMain", "ps_5_0").ToArray();
        }
        catch (Exception ex)
        {
            CompileError = TrimError(ex.Message);
            return null;
        }
    }

    private static void Prepare(ShaderFile file)
    {
        try
        {
            var text = File.ReadAllText(file.Path);
            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text[1..];
            var match = Regex.Match(text, @"float([34])\s+Shader\s*\(", RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                file.Error = "Need float3 Shader(float2 uv, float3 color)";
                file.Enabled = false;
                return;
            }
            file.Returns4 = match.Groups[1].Value == "4";
            var wrapped = LoadFunction(file);
            var test = UpscaleShaders.Hlsl.Replace(
                "float4 PSMain(VSOut i) : SV_Target",
                wrapped + "\nfloat4 PSMain(VSOut i) : SV_Target",
                StringComparison.Ordinal);
            var call = file.Returns4 ? $"{file.Id}(uv, color).rgb" : $"{file.Id}(uv, color)";
            test = test.Replace(
                "    return float4(color, 1.0);",
                $"    color = {call};\n    return float4(color, 1.0);",
                StringComparison.Ordinal);
            UpscaleShaders.CompileSource(test, "PSMain", "ps_5_0");
            file.Error = null;
        }
        catch (Exception ex)
        {
            file.Error = TrimError(ex.Message);
            file.Enabled = false;
            _enabled.Remove(file.FileName);
        }
    }

    private static string LoadFunction(ShaderFile file)
    {
        var text = File.ReadAllText(file.Path);
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];
        return Regex.Replace(
            text,
            @"float([34])\s+Shader\s*\(",
            $"float$1 {file.Id}(",
            RegexOptions.CultureInvariant);
    }

    private static void RebuildMask()
    {
        uint mask = 0;
        var bit = 1u;
        foreach (var file in _files)
        {
            if (file.Enabled && file.Error is null)
                mask |= bit;
            bit <<= 1;
        }
        Mask = mask;
    }

    private static string MakeId(string name, HashSet<string> used)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var id = new string(chars);
        if (id.Length == 0 || char.IsDigit(id[0]))
            id = "S_" + id;
        var unique = "User_" + id;
        var n = 2;
        while (!used.Add(unique))
            unique = "User_" + id + "_" + n++;
        return unique;
    }

    private static string TrimError(string message)
    {
        var line = message.Replace('\r', '\n').Split('\n').FirstOrDefault(s => s.Contains("error", StringComparison.OrdinalIgnoreCase));
        var text = string.IsNullOrWhiteSpace(line) ? message : line.Trim();
        return text.Length > 180 ? text[..177] + "..." : text;
    }

    private const string ExampleHlsl = """
// Folder shaders — each .hlsl file is one ¬ overlay technique.
// Required entry:
//   float3 Shader(float2 uv, float3 color)
// Available:
//   InputTex, PrevTex, Samp, SampleTex(uv)
//   InputSize, OutputSize, BufDepth(uv), BufFlow(uv)

float3 Shader(float2 uv, float3 color)
{
    float3 tint = float3(1.05, 1.00, 0.96);
    return saturate(color * tint);
}
""";
}
