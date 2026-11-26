// Size the original GLFW window and its existing PCSX2 GS child in place.
// The renderer stays attached to the same window and keeps receiving input.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class WindowLayout
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo { public int Size; public Rect Monitor, Work; public int Flags; }

    [DllImport("user32", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr mon, ref MonitorInfo info);
    [DllImport("user32")] static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32", EntryPoint = "GetWindowLongW")] static extern uint Style(IntPtr hwnd, int index);
    [DllImport("user32", EntryPoint = "SetWindowLongW")] static extern int SetStyle(IntPtr hwnd, int index, uint style);
    [DllImport("user32")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32")] static extern bool ShowWindow(IntPtr hwnd, int mode);
    [DllImport("user32")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32")] static extern bool SetProcessDPIAware();
    [DllImport("user32")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    const int GwlStyle = -16;
    const uint OverlappedWindow = 0x00CF0000, Popup = 0x80000000;
    const uint NoSize = 1, NoMove = 2, NoZOrder = 4, NoActivate = 0x10, FrameChanged = 0x20;

    internal static void EnableDpiAwareness()
    {
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } // Per-monitor v2 on Windows 10/11.
        catch (EntryPointNotFoundException) { }
        SetProcessDPIAware();
    }

    static IntPtr GameWindow(int pid)
    {
        IntPtr hwnd = IntPtr.Zero;
        while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, "GLFW30", null)) != IntPtr.Zero)
        {
            int owner;
            GetWindowThreadProcessId(hwnd, out owner);
            if (pid == owner && IsWindowVisible(hwnd)) return hwnd;
        }
        return IntPtr.Zero;
    }

    internal static void SetLayout(IntPtr hwnd, bool borderless)
    {
        if (borderless)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return;
            Rect r = info.Monitor;
            ShowWindow(hwnd, 9); // Restore before changing a maximized window.
            SetStyle(hwnd, GwlStyle, (Style(hwnd, GwlStyle) & ~OverlappedWindow) | Popup);
            // A one-pixel bottom margin keeps this a desktop window at native resolution.
            SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Right-r.Left, r.Bottom-r.Top-1,
                         NoZOrder | NoActivate | FrameChanged);
        }
        else
        {
            SetStyle(hwnd, GwlStyle, (Style(hwnd, GwlStyle) & ~Popup) | OverlappedWindow);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, NoMove | NoSize | NoZOrder | NoActivate | FrameChanged);
            ShowWindow(hwnd, 3); // Maximize in the work area. The title bar supports restoring and resizing.
        }
        FitRenderer(hwnd);
    }

    static void FitRenderer(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) return;
        IntPtr gs = FindWindowEx(hwnd, IntPtr.Zero, "ps2x_pcsx2_gs", null);
        Rect host, child;
        if (gs == IntPtr.Zero || !GetClientRect(hwnd, out host) || !GetClientRect(gs, out child)) return;
        if (host.Right > 0 && host.Bottom > 0 && (host.Right != child.Right || host.Bottom != child.Bottom))
            SetWindowPos(gs, IntPtr.Zero, 0, 0, host.Right, host.Bottom, NoZOrder | NoActivate);
    }

    internal static void Follow(Process game, bool borderless)
    {
        IntPtr hwnd = IntPtr.Zero;
        bool held = false;
        var refresh = Stopwatch.StartNew();
        while (!game.WaitForExit(hwnd == IntPtr.Zero ? 100 : 25))
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                hwnd = GameWindow(game.Id);
                if (hwnd == IntPtr.Zero) continue;
                // Let the original startup finish creating its rendering child first.
                if (FindWindowEx(hwnd, IntPtr.Zero, "ps2x_pcsx2_gs", null) == IntPtr.Zero)
                {
                    hwnd = IntPtr.Zero;
                    continue;
                }
                SetLayout(hwnd, borderless);
            }
            int foreground;
            GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
            bool down = foreground == game.Id && GetAsyncKeyState(0x12) < 0 && GetAsyncKeyState(0x0D) < 0;
            if (down && !held) { borderless = !borderless; SetLayout(hwnd, borderless); }
            held = down;
            if (refresh.ElapsedMilliseconds >= 300) { FitRenderer(hwnd); refresh.Restart(); }
        }
    }
}
