using System.Runtime.InteropServices;

namespace CpuScreenViewer;

internal sealed class HomeHook : IDisposable
{
    private Native.HookProc? _keyProc;
    private Native.HookProc? _mouseProc;
    private IntPtr _keyHook;
    private IntPtr _mouseHook;
    private Thread? _thread;
    private uint _threadId;
    private volatile bool _run;

    public event Action? Pressed;
    public event Action? HomePassed;
    public event Action<int>? OverlayKey;
    public event Action? EmptyClick;
    public event Action? AllowInScreenshot;
    public Action? FreezeCapture;
    public volatile bool EatHome = true;
    public volatile bool WatchEmptyClicks;
    public volatile IntPtr FormHwnd;
    public volatile IntPtr PreviewHwnd;
    public volatile IntPtr D3dHwnd;
    public Func<int, int, bool>? IsEmptyOverlayClick;

    public void Start()
    {
        if (_thread != null)
            return;
        _run = true;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "home-hook",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Loop()
    {
        _threadId = Native.GetCurrentThreadId();
        _keyProc = KeyCallback;
        _mouseProc = MouseCallback;
        var module = Native.GetModuleHandle(null);
        _keyHook = Native.SetWindowsHookEx(Native.WhKeyboardLl, _keyProc, module, 0);
        if (_keyHook == IntPtr.Zero)
            _keyHook = Native.SetWindowsHookEx(Native.WhKeyboardLl, _keyProc, Native.GetModuleHandle("user32"), 0);
        _mouseHook = Native.SetWindowsHookEx(Native.WhMouseLl, _mouseProc, module, 0);
        if (_mouseHook == IntPtr.Zero)
            _mouseHook = Native.SetWindowsHookEx(Native.WhMouseLl, _mouseProc, Native.GetModuleHandle("user32"), 0);
        while (_run && Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Native.TranslateMessage(ref msg);
            Native.DispatchMessage(ref msg);
        }
        if (_keyHook != IntPtr.Zero)
            Native.UnhookWindowsHookEx(_keyHook);
        if (_mouseHook != IntPtr.Zero)
            Native.UnhookWindowsHookEx(_mouseHook);
        _keyHook = IntPtr.Zero;
        _mouseHook = IntPtr.Zero;
    }

    private volatile bool _homeHeld;
    private volatile int _fxHeld; // bit0 F8, bit1 F11

    private IntPtr KeyCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<Native.KbdLlHook>(lParam);
            if ((info.Flags & Native.LlkfInjected) == 0 && IsScreenshotKey(info.VkCode, wParam, info.Flags))
            {
                try { FreezeCapture?.Invoke(); } catch { }
                AllowCapture();
                try { AllowInScreenshot?.Invoke(); } catch { }
                return Native.CallNextHookEx(_keyHook, code, wParam, lParam);
            }
            if (IsKeyUp(wParam, info.Flags))
            {
                if (info.VkCode == Native.VkOem8)
                    _homeHeld = false;
                else if (info.VkCode == Native.VkF8)
                    _fxHeld &= ~1;
                else if (info.VkCode == Native.VkF11)
                    _fxHeld &= ~2;
            }
        }
        if (code >= 0 && (wParam == (IntPtr)Native.WmKeyDown || wParam == (IntPtr)Native.WmSysKeyDown))
        {
            var info = Marshal.PtrToStructure<Native.KbdLlHook>(lParam);
            if ((info.Flags & Native.LlkfUp) != 0 || (info.Flags & Native.LlkfInjected) != 0)
                return Native.CallNextHookEx(_keyHook, code, wParam, lParam);
            if (info.VkCode == Native.VkOem8)
            {
                if (_homeHeld)
                    return (IntPtr)1;
                _homeHeld = true;
                if (EatHome)
                {
                    try { Pressed?.Invoke(); } catch { }
                }
                try { HomePassed?.Invoke(); } catch { }
                ForwardToD3d(Native.VkOem8);
                return (IntPtr)1;
            }
            if (info.VkCode is Native.VkF8 or Native.VkF11)
            {
                var bit = info.VkCode == Native.VkF8 ? 1 : 2;
                if ((_fxHeld & bit) != 0)
                    return (IntPtr)1;
                _fxHeld |= bit;
                try { OverlayKey?.Invoke(info.VkCode); } catch { }
                return (IntPtr)1;
            }
        }
        return Native.CallNextHookEx(_keyHook, code, wParam, lParam);
    }

    private static bool IsKeyUp(IntPtr wParam, int flags) =>
        wParam == (IntPtr)Native.WmKeyUp || wParam == (IntPtr)Native.WmSysKeyUp || (flags & Native.LlkfUp) != 0;

    private void ForwardToD3d(int vk)
    {
        var hwnd = D3dHwnd;
        if (hwnd == IntPtr.Zero)
            return;
        Native.PostMessage(hwnd, Native.WmKeyDown, vk, 1);
        Native.PostMessage(hwnd, Native.WmKeyUp, vk, (1 << 30) | 1);
    }

    private static bool KeyIsDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool IsScreenshotKey(int vk, IntPtr wParam, int flags)
    {
        var up = wParam == (IntPtr)Native.WmKeyUp || wParam == (IntPtr)Native.WmSysKeyUp
            || (flags & Native.LlkfUp) != 0;
        if (up)
            return false;
        if (vk == Native.VkSnapshot)
            return true;
        return vk == Native.VkS && KeyIsDown(Native.VkShift)
            && (KeyIsDown(Native.VkLwin) || KeyIsDown(Native.VkRwin));
    }

    private void AllowCapture()
    {
        Native.SetProcessDisplayAffinity(Native.WdaNone);
    }

    private IntPtr MouseCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && WatchEmptyClicks && wParam == (IntPtr)Native.WmLButtonDown)
        {
            var info = Marshal.PtrToStructure<Native.MsLlHook>(lParam);
            var empty = false;
            try { empty = IsEmptyOverlayClick?.Invoke(info.Pt.X, info.Pt.Y) == true; } catch { }
            if (empty)
            {
                try { EmptyClick?.Invoke(); } catch { }
                return (IntPtr)1;
            }
        }
        return Native.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    public void Dispose()
    {
        _run = false;
        if (_threadId != 0)
            Native.PostThreadMessage(_threadId, Native.WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(1000);
        _thread = null;
        _keyProc = null;
        _mouseProc = null;
    }
}
