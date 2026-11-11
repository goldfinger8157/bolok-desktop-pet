using System.Runtime.InteropServices;
using System.Text;

namespace Bolok;

public readonly record struct Win(IntPtr H, Rectangle R, bool Topmost);
public readonly record struct WinState(bool Alive, bool Minimized, Rectangle R);

static class Native
{
    public const int WM_DPICHANGED = 0x02E0;
    public const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;

    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10;

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    struct LastInput { public uint Size; public uint Time; }

    [DllImport("user32.dll")]
    static extern bool GetLastInputInfo(ref LastInput info);

    public static void Topmost(IntPtr hwnd) => SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    static readonly IntPtr HWND_NOTOPMOST = new(-2);
    const int GWL_EXSTYLE = -20, DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14, GW_HWNDPREV = 3;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int L, T, R, B; }

    delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr h, StringBuilder name, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")]
    static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    static extern int DwmGetRect(IntPtr h, int attr, out Rect value, int size);

    public static void NotTopmost(IntPtr hwnd) => SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    // hwnd 를 target 바로 아래로 — target 이 진짜로 볼록이를 가린다
    public static void PutBelow(IntPtr hwnd, IntPtr target) => SetWindowPos(hwnd, target, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    // target 이 hwnd 보다 위에 있는지와, 사이에 낀 창 수. 바로 위가 아니어도 안 보이는 창이 끼었을 뿐일 수 있다
    public static (bool Below, int Between, int VisibleBetween) ZGap(IntPtr hwnd, IntPtr target)
    {
        int between = 0, visible = 0;
        for (var h = GetWindow(hwnd, GW_HWNDPREV); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDPREV))
        {
            if (h == target) return (true, between, visible);
            between++;
            if (IsWindowVisible(h)) visible++;
        }
        return (false, between, visible);
    }

    // 눈에 보이는 창의 테두리. GetWindowRect 는 안 보이는 크기 조절 여백까지 포함한다
    static Rectangle Frame(IntPtr h)
    {
        if (DwmGetRect(h, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<Rect>()) != 0) GetWindowRect(h, out r);
        return Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
    }

    public static WinState Probe(IntPtr h) =>
        new(IsWindow(h) && IsWindowVisible(h), IsIconic(h), Frame(h));

    // 지금 보이는 일반 창을 위에서 아래 순서로. 제목은 있는지만 보고 내용은 읽지 않는다
    public static List<Win> ListWindows()
    {
        var list = new List<Win>();
        var name = new StringBuilder(64);
        uint me = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h) || GetWindowTextLength(h) == 0) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == me) return true;
            long ex = (long)GetWindowLongPtr(h, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0) return true;
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true; // 다른 가상 데스크톱 등
            name.Clear();
            GetClassName(h, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            list.Add(new Win(h, Frame(h), (ex & WS_EX_TOPMOST) != 0));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(Point p, uint flags);
    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

    public static int DpiAt(Point p) =>
        GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out uint x, out _) == 0 ? (int)x : 96;

    [DllImport("user32.dll")]
    static extern uint GetGuiResources(IntPtr process, uint kind);

    public static int GdiObjects() => (int)GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 0);

    static IntPtr shellChecked;
    static bool isShell;

    public static bool OwnForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    // 지금 활성인 일반 창. 바탕화면·작업표시줄·Alt+Tab 화면처럼 창이 아닌 것은 없는 것으로 친다
    public static Win? Foreground()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero || !IsWindowVisible(h) || IsIconic(h) || GetWindowTextLength(h) == 0) return null;
        long ex = (long)GetWindowLongPtr(h, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) != 0) return null;
        if (h != shellChecked)
        {
            var name = new StringBuilder(64);
            GetClassName(h, name, name.Capacity);
            shellChecked = h;
            isShell = name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
                or "XamlExplorerHostIslandWindow" or "MultitaskingViewFrame" or "Windows.UI.Core.CoreWindow";
        }
        return isShell ? null : new Win(h, Frame(h), (ex & WS_EX_TOPMOST) != 0);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
    [DllImport("user32.dll")]
    static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")]
    static extern IntPtr GetAncestor(IntPtr h, uint flags);

    // 게임 중엔 볼록이 창도 클릭을 통과시켜 게임판이 받게 한다
    public static void SetClickThrough(IntPtr h, bool on)
    {
        long ex = (long)GetWindowLongPtr(h, GWL_EXSTYLE);
        ex = on ? ex | WS_EX_TRANSPARENT : ex & ~(long)WS_EX_TRANSPARENT;
        SetWindowLongPtr(h, GWL_EXSTYLE, (IntPtr)ex);
    }

    // 그 자리를 누르면 클릭을 받을 최상위 창
    public static IntPtr RootAt(Point p) => GetAncestor(WindowFromPoint(p), 2);

    [StructLayout(LayoutKind.Sequential)]
    struct IconInfo { public bool IsIcon; public int X, Y; public IntPtr Mask, Color; }
    [DllImport("user32.dll")]
    static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")]
    static extern IntPtr CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);
    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr obj);

    // 그림으로 커서 만들기. 끝점(hotspot)은 조준경 가운데
    public static Cursor MakeCursor(Bitmap bmp, int hx, int hy)
    {
        IntPtr icon = bmp.GetHicon();
        GetIconInfo(icon, out var info);
        (info.IsIcon, info.X, info.Y) = (false, hx, hy);
        IntPtr cursor = CreateIconIndirect(ref info);
        DeleteObject(info.Mask);
        DeleteObject(info.Color);
        DestroyIcon(icon);
        return new Cursor(cursor);
    }

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);
    public static bool KeyDown(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0; // 포커스 없이 키 상태만 본다

    [DllImport("user32.dll")]
    static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
    [DllImport("user32.dll")]
    static extern bool GetWindowDisplayAffinity(IntPtr h, out uint affinity);

    // 화면 공유·녹화에 안 찍히게 (윈도우 10 2004 이상). 되돌리면 다시 찍힌다. 실제로 걸렸는지 값을 돌려준다
    public static uint HideFromCapture(IntPtr h, bool on)
    {
        SetWindowDisplayAffinity(h, on ? 0x11u : 0u);
        GetWindowDisplayAffinity(h, out uint now);
        return now;
    }

    public static double IdleMinutes()
    {
        var i = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        if (!GetLastInputInfo(ref i)) return 0;
        return (uint)(Environment.TickCount - (int)i.Time) / 60000.0;
    }

    // 뜯기는 자리의 화면색을 몇 개 집어 온다. 저장하지 않고 부스러기 색으로만 쓴다
    public static Color[] SampleScreen(int cx, int cy, int r)
    {
        int size = r * 2;
        using var bmp = new Bitmap(size, size);
        try
        {
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(cx - r, cy - r, 0, 0, bmp.Size);
        }
        catch (ExternalException ex) // 관리자 권한 창이 떠 있거나 잠금 화면이면 화면을 못 읽는다
        {
            Dbg.Log("sample-failed", ("msg", ex.Message));
            return Enumerable.Repeat(Color.FromArgb(200, 205, 212), 16).ToArray();
        }
        var colors = new Color[16];
        var rng = Random.Shared;
        for (int i = 0; i < colors.Length; i++) colors[i] = bmp.GetPixel(rng.Next(size), rng.Next(size));
        return colors;
    }
}
