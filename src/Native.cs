using System.Runtime.InteropServices;

namespace CpuScreenViewer;

internal static class Native
{
    public const uint WsExLayered = 0x80000;
    public const uint WsExTransparent = 0x20;
    public const uint WsExNoRedirectionBitmap = 0x00200000;
    public const uint LwaAlpha = 0x2;
    public const int GwlExStyle = -20;
    public const int WdaExcludeFromCapture = 0x11;
    public const uint WdaNone = 0;
    public const int SrcCopy = 0x00CC0020;
    public const int CursorShowing = 1;
    public const uint DiNormal = 3;
    public const uint WsChild = 0x40000000;
    public const uint WsPopup = 0x80000000;
    public const uint WsVisible = 0x10000000;
    public const uint WsClipSiblings = 0x04000000;
    public const uint WsClipChildren = 0x02000000;
    public const uint WsTabStop = 0x00010000;
    public const uint WsExNoActivate = 0x08000000;
    public const int GwlStyle = -16;
    public const int GwlpHwndParent = -8;
    public const uint GaRoot = 2;
    public const uint CsOwndc = 0x0020;
    public const int VkHome = 0x24;
    public const int VkEscape = 0x1B;
    public const int VkF8 = 0x77;
    public const int VkF11 = 0x7A;
    public const int VkOem3 = 0xC0;
    public const int VkOem8 = 0xDF;
    public const int VkSnapshot = 0x2C;
    public const int VkS = 0x53;
    public const int VkShift = 0x10;
    public const int VkLwin = 0x5B;
    public const int VkRwin = 0x5C;
    public const uint WmKeyDown = 0x0100;
    public const uint WmKeyUp = 0x0101;
    public const uint WmSysKeyDown = 0x0104;
    public const uint WmSysKeyUp = 0x0105;
    public const uint WmQuit = 0x0012;
    public const int WhKeyboardLl = 13;
    public const int WhMouseLl = 14;
    public const uint WmLButtonDown = 0x0201;
    public const int LlkfInjected = 0x10;
    public const int LlkfUp = 0x80;
    public const int SwRestore = 9;
    public const int SwShow = 5;
    public const int SwHide = 0;
    public const int SwShowNoActivate = 8;
    public static readonly IntPtr HwndTop = IntPtr.Zero;
    public static readonly IntPtr HwndTopMost = new(-1);
    public static readonly IntPtr HwndNoTopMost = new(-2);
    public static readonly IntPtr HwndBottom = new(1);
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoZOrder = 0x0004;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public WndProc lpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr IconSm;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    public const int DwmwaCloaked = 14;
    public const int WsExToolwindow = 0x80;
    public const int WsExAppwindow = 0x40000;
    public const uint ProcessQueryLimited = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CursorInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Cursor;
        public Point ScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IconInfo
    {
        public int Icon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr Mask;
        public IntPtr Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Bitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr hIcon, out IconInfo info);
    [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int w, int h, uint step, IntPtr brush, uint flags);
    [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr h, int size, out Bitmap bmp);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    public const uint PwRenderFullContent = 2;
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
    [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")]
    public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetFocus();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, int wParam, int lParam);
    [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] public static extern uint GetCurrentProcessId();
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;
    public const uint SwpFrameChanged = 0x0020;
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref Msg lpMsg);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct MsLlHook
    {
        public Point Pt;
        public int MouseData;
        public int Flags;
        public int Time;
        public IntPtr Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdLlHook
    {
        public int VkCode;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassEx(ref WndClassEx lpwcx);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] public static extern bool ValidateRect(IntPtr hWnd, IntPtr lpRect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint uPeriod);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint uPeriod);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool SetDllDirectory(string lpPathName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadLibrary(string lpFileName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);
    public const uint LoadLibrarySearchSystem32 = 0x00000800;

    /// <summary>
    /// Load OS DXGI/D3D11 before any app-directory proxy (e.g. ReShade dxgi.dll) can hook Present and lock ~60Hz.
    /// Files next to the exe are left untouched.
    /// </summary>
    public static void PreferSystemGraphicsStack()
    {
        LoadLibraryEx("dxgi.dll", IntPtr.Zero, LoadLibrarySearchSystem32);
        LoadLibraryEx("d3d11.dll", IntPtr.Zero, LoadLibrarySearchSystem32);
        LoadLibraryEx("d3dcompiler_47.dll", IntPtr.Zero, LoadLibrarySearchSystem32);
    }
    [DllImport("kernel32.dll")] public static extern uint SetErrorMode(uint uMode);
    public const uint SemFailCriticalErrors = 0x0001;
    public const uint SemNoGpFaultErrorBox = 0x0002;
    [DllImport("kernel32.dll")] public static extern bool VirtualProtect(IntPtr address, nuint size, uint newProtect, out uint oldProtect);
    public const uint PageExecuteReadWrite = 0x40;
    public const int ColorOnColor = 3;
    public const int Halftone = 4;
    public const uint DibRgbColors = 0;
    public const int BiRgb = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfo
    {
        public BitmapInfoHeader Header;
    }

    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);
    [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int rop);
    [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr ho);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, System.Text.StringBuilder lpExeName, ref int lpdwSize);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr hObject);
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] public static extern bool SetProcessInformation(IntPtr hProcess, int processInformationClass, in PowerThrottlingState processInformation, uint processInformationSize);
    [DllImport("kernel32.dll")] public static extern bool SetThreadInformation(IntPtr hThread, int threadInformationClass, in PowerThrottlingState threadInformation, uint threadInformationSize);
    [DllImport("kernel32.dll")] public static extern bool GetSystemCpuSetInformation(IntPtr information, uint bufferLength, out uint returnedLength, IntPtr process, uint flags);
    [DllImport("kernel32.dll")] public static extern bool SetThreadSelectedCpuSets(IntPtr thread, uint[] cpuSetIds, uint cpuSetIdCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode mode);

    public const int EnumCurrentSettings = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DevMode
    {
        private const int Cch = 32;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Cch)] public string DeviceName;
        public short SpecVersion;
        public short DriverVersion;
        public short Size;
        public short DriverExtra;
        public int Fields;
        public int PositionX;
        public int PositionY;
        public int DisplayOrientation;
        public int DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Cch)] public string FormName;
        public short LogPixels;
        public int BitsPerPel;
        public int PelsWidth;
        public int PelsHeight;
        public int DisplayFlags;
        public int DisplayFrequency;
    }

    public const int ProcessPowerThrottling = 4;
    public const int ThreadPowerThrottling = 4;
    public const uint PowerThrottlingExecSpeed = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    public static void PreferPerformance()
    {
        var state = new PowerThrottlingState
        {
            Version = 1,
            ControlMask = PowerThrottlingExecSpeed,
            StateMask = 0,
        };
        var size = (uint)Marshal.SizeOf<PowerThrottlingState>();
        try { SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, state, size); } catch { }
        PreferFastThread();
    }

    public static void PreferFastThread()
    {
        var state = new PowerThrottlingState
        {
            Version = 1,
            ControlMask = PowerThrottlingExecSpeed,
            StateMask = 0,
        };
        var size = (uint)Marshal.SizeOf<PowerThrottlingState>();
        try { SetThreadInformation(GetCurrentThread(), ThreadPowerThrottling, state, size); } catch { }
    }

    public static int DisplayRefreshHz(string? deviceName)
    {
        try
        {
            var mode = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
            if (!EnumDisplaySettings(string.IsNullOrWhiteSpace(deviceName) ? null : deviceName, EnumCurrentSettings, ref mode))
                return 0;
            return mode.DisplayFrequency > 1 ? mode.DisplayFrequency : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static uint[] PerformanceCpuSetIds()
    {
        try
        {
            GetSystemCpuSetInformation(IntPtr.Zero, 0, out var needed, IntPtr.Zero, 0);
            if (needed < 32)
                return [];
            var buf = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!GetSystemCpuSetInformation(buf, needed, out var returned, IntPtr.Zero, 0) || returned < 32)
                    return [];
                var minEff = byte.MaxValue;
                var maxEff = byte.MinValue;
                var ids = new List<(uint Id, byte Eff)>();
                var offset = 0;
                while (offset + 20 <= returned)
                {
                    var size = Marshal.ReadInt32(buf, offset);
                    if (size < 20)
                        break;
                    var type = Marshal.ReadInt32(buf, offset + 4);
                    if (type == 0)
                    {
                        var id = (uint)Marshal.ReadInt32(buf, offset + 8);
                        var eff = Marshal.ReadByte(buf, offset + 18);
                        ids.Add((id, eff));
                        if (eff < minEff) minEff = eff;
                        if (eff > maxEff) maxEff = eff;
                    }
                    offset += size;
                }
                if (ids.Count == 0 || minEff == maxEff)
                    return [];
                return ids.Where(x => x.Eff == minEff).Select(x => x.Id).ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
        catch
        {
            return [];
        }
    }

    public static void PinToCpuSets(uint[] cpuSetIds)
    {
        if (cpuSetIds.Length == 0)
            return;
        try { SetThreadSelectedCpuSets(GetCurrentThread(), cpuSetIds, (uint)cpuSetIds.Length); }
        catch { /* ignore */ }
    }

    private static readonly object AffinityLock = new();
    private static uint _affinityPid;
    private static uint _affinityValue;
    private static readonly EnumWindowsProc AffinityProc = ApplyProcessAffinity;

    public static void SetProcessDisplayAffinity(uint affinity)
    {
        lock (AffinityLock)
        {
            _affinityPid = GetCurrentProcessId();
            _affinityValue = affinity;
            EnumWindows(AffinityProc, IntPtr.Zero);
        }
    }

    public static void ExcludeFromCapture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        try { SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture); } catch { }
    }

    public static void RaiseOwnedPopup(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        ExcludeFromCapture(hwnd);
        try
        {
            SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }
        catch
        {
            // ignore
        }
    }

    public static void ClearTopMost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        try
        {
            SetWindowPos(hwnd, HwndNoTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
        catch
        {
            // ignore
        }
    }

    public static void FloatOwnedWindow(IntPtr hwnd, IntPtr owner)
    {
        if (hwnd == IntPtr.Zero)
            return;
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        style = (style & ~WsChild) | WsPopup;
        SetWindowLongPtr(hwnd, GwlStyle, (IntPtr)style);
        SetParent(hwnd, IntPtr.Zero);
        if (owner != IntPtr.Zero)
            SetWindowLongPtr(hwnd, GwlpHwndParent, owner);
        ExcludeFromCapture(hwnd);
    }

    private static bool ApplyProcessAffinity(IntPtr hwnd, IntPtr lParam)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _affinityPid)
            SetWindowDisplayAffinity(hwnd, _affinityValue);
        return true;
    }
}
