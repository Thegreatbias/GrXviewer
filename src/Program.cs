using System.Reflection;
using System.Runtime.CompilerServices;

namespace CpuScreenViewer;

internal static class Program
{
    [ModuleInitializer]
    internal static void UseRuntimeFolder()
    {
        // Before SetDllDirectory / anything else: pin System32 DXGI so local ReShade dxgi.dll
        // does not intercept Present (that caps output at display refresh, often 60).
        Native.PreferSystemGraphicsStack();

        var runtime = Path.Combine(AppContext.BaseDirectory, "runtime");
        if (!Directory.Exists(runtime))
            return;

        Native.SetDllDirectory(runtime);
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        Environment.SetEnvironmentVariable("PATH", runtime + Path.PathSeparator + path);

        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(name))
                return null;
            var candidate = Path.Combine(runtime, name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Native.PreferPerformance();
        CpuTune.ApplyProcess();
        if (args.Any(a => a.Equals("--sim", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = CaptureSimulation.Run();
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
