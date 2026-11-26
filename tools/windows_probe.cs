// Test driver. Never included in the distribution.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class Probe
{
    delegate IntPtr Proc(IntPtr w, uint m, IntPtr p, IntPtr l);
    static readonly Proc Handler = DefWindowProc;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WndClass {
        public uint style; public Proc proc; public int clsExtra, wndExtra;
        public IntPtr instance, icon, cursor, brush;
        public string menu, name;
    }
    [DllImport("user32", CharSet=CharSet.Unicode)] static extern ushort RegisterClass(ref WndClass c);
    [DllImport("user32", CharSet=CharSet.Unicode)] static extern IntPtr DefWindowProc(IntPtr w, uint m, IntPtr p, IntPtr l);
    [DllImport("user32", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("kernel32", CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32")] static extern bool SetForegroundWindow(IntPtr w);

    [STAThread]
    static int Main(string[] args) {
        if (args.Length == 3 && args[0] == "import") {
            try { Console.WriteLine(Disc.Open(args[1], args[2], delegate { })); return 0; }
            catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
        }
        if (Environment.GetEnvironmentVariable("PS2X_WINDOWED") != "1") return 2;
        var c = new WndClass { instance = GetModuleHandle(null), proc = Handler, name = "GLFW30" };
        if (RegisterClass(ref c) == 0) return 3;
        c.name = "ps2x_pcsx2_gs"; if (RegisterClass(ref c) == 0) return 4;
        var w = CreateWindowEx(0, "GLFW30", "Yakuza window geometry test", 0x10CA0000, 16,16,640,448,IntPtr.Zero,IntPtr.Zero,c.instance,IntPtr.Zero);
        var gs = CreateWindowEx(0, c.name, "GS", 0x50000000, 0,0,640,448,w,IntPtr.Zero,c.instance,IntPtr.Zero);
        if (w == IntPtr.Zero || gs == IntPtr.Zero) return 5;
        SetForegroundWindow(w);
        Application.Run(); return 0;
    }
}
