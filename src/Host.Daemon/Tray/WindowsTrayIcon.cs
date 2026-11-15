using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RigForge.Host.Daemon.Tray;

/// <summary>
/// Notification-area icon driven by its own message loop. Shell_NotifyIcon delivers clicks to a window,
/// and the daemon's main thread does not pump Win32 messages.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsTrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8001;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmNull = 0x0000;
    private const uint WmQuit = 0x0012;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint NinSelect = 0x0400;
    private const uint NinKeySelect = 0x0401;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint NiifInfo = 0x00000001;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;
    private const uint MfString = 0x00000000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int ErrorClassAlreadyExists = 1410;
    private const uint CommandOpen = 1;
    private const uint CommandExit = 2;
    private const string WindowClassName = "RigForge.Tray";

    private static readonly Native.WndProc WndProcDelegate = WndProc;
    private static WindowsTrayIcon? _current;

    private readonly Func<DashboardLaunch> _openDashboard;
    private readonly Action _exit;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hwnd;
    private IntPtr _icon;
    private bool _ownsIcon;
    private bool _iconAdded;
    private bool _disposed;
    private long _lastOpenTick;

    public WindowsTrayIcon(Func<DashboardLaunch> openDashboard, Action exit, ILogger<WindowsTrayIcon> logger)
    {
        _openDashboard = openDashboard;
        _exit = exit;
        _logger = logger;
    }

    public void Start()
    {
        using var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() => Run(ready))
        {
            IsBackground = true,
            Name = "RigForge.Tray",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5)))
        {
            LogTrayFailed("the tray thread did not start");
        }
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            thread = _thread;
            var hwnd = _hwnd;
            if (hwnd != IntPtr.Zero)
            {
                Native.PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
            }
            else if (_threadId != 0)
            {
                Native.PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            }
        }

        thread?.Join(TimeSpan.FromSeconds(2));
    }

    private void Run(ManualResetEventSlim ready)
    {
        try
        {
            _current = this;
            _threadId = Native.GetCurrentThreadId();
            _hwnd = CreateHiddenWindow();
            if (_hwnd != IntPtr.Zero)
            {
                AddIcon();
            }
        }
        catch (Exception exception)
        {
            LogTrayFailed(exception.Message);
        }
        finally
        {
            ready.Set();
        }

        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        while (Native.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            Native.TranslateMessage(ref message);
            Native.DispatchMessage(ref message);
        }

        RemoveIcon();
        if (_current == this)
        {
            _current = null;
        }
    }

    private IntPtr CreateHiddenWindow()
    {
        var instance = Native.GetModuleHandle(null);
        var windowClass = new Native.WndClass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcDelegate),
            hInstance = instance,
            lpszClassName = WindowClassName,
        };

        if (Native.RegisterClass(ref windowClass) == 0 && Marshal.GetLastWin32Error() != ErrorClassAlreadyExists)
        {
            LogTrayFailed($"RegisterClass {Marshal.GetLastWin32Error()}");
            return IntPtr.Zero;
        }

        var hwnd = Native.CreateWindowEx(
            WsExToolWindow | WsExNoActivate,
            WindowClassName,
            "RigForge",
            WsPopup,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            LogTrayFailed($"CreateWindowEx {Marshal.GetLastWin32Error()}");
        }

        return hwnd;
    }

    private void AddIcon()
    {
        _icon = LoadTrayIcon();
        var data = CreateData();
        data.uFlags = NifMessage | NifIcon | NifTip;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = "RigForge";
        if (!Native.Shell_NotifyIcon(NimAdd, ref data))
        {
            LogTrayFailed($"Shell_NotifyIcon {Marshal.GetLastWin32Error()}");
            return;
        }

        _iconAdded = true;
    }

    private IntPtr LoadTrayIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rigforge.ico");
        if (File.Exists(path))
        {
            var loaded = Native.LoadImage(IntPtr.Zero, path, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
            if (loaded != IntPtr.Zero)
            {
                _ownsIcon = true;
                return loaded;
            }
        }

        return Native.LoadIcon(IntPtr.Zero, (IntPtr)32512);
    }

    private void RemoveIcon()
    {
        if (_iconAdded && _hwnd != IntPtr.Zero)
        {
            var data = CreateData();
            Native.Shell_NotifyIcon(NimDelete, ref data);
            _iconAdded = false;
        }

        if (_ownsIcon && _icon != IntPtr.Zero)
        {
            Native.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
            _ownsIcon = false;
        }
    }

    private void ShowMissingShell()
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = CreateData();
        data.uFlags = NifInfo;
        data.szInfoTitle = "RigForge";
        data.szInfo = "RigForge window is not installed.";
        data.dwInfoFlags = NiifInfo;
        Native.Shell_NotifyIcon(NimModify, ref data);
    }

    private Native.NotifyIconData CreateData()
    {
        return new Native.NotifyIconData
        {
            cbSize = Marshal.SizeOf<Native.NotifyIconData>(),
            hWnd = _hwnd,
            uID = 1,
            szTip = "",
            szInfo = "",
            szInfoTitle = "",
        };
    }

    private void OpenFromIcon()
    {
        var now = Environment.TickCount64;
        if (now - _lastOpenTick < 500)
        {
            return;
        }

        _lastOpenTick = now;
        if (_openDashboard() == DashboardLaunch.Missing)
        {
            ShowMissingShell();
        }
    }

    private void ShowMenu()
    {
        var menu = Native.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Native.AppendMenu(menu, MfString, CommandOpen, "Open dashboard");
            Native.AppendMenu(menu, MfString, CommandExit, "Exit");
            Native.GetCursorPos(out var point);
            Native.SetForegroundWindow(_hwnd);
            var command = Native.TrackPopupMenu(menu, TpmRightButton | TpmReturnCmd, point.X, point.Y, 0, _hwnd, IntPtr.Zero);
            Native.PostMessage(_hwnd, WmNull, IntPtr.Zero, IntPtr.Zero);
            if (command == CommandOpen)
            {
                OpenFromIcon();
            }
            else if (command == CommandExit)
            {
                _exit();
            }
        }
        finally
        {
            Native.DestroyMenu(menu);
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        var icon = _current;
        if (icon is null)
        {
            return Native.DefWindowProc(hwnd, message, wParam, lParam);
        }

        if (message == WmClose)
        {
            Native.DestroyWindow(hwnd);
            return IntPtr.Zero;
        }

        if (message == WmDestroy)
        {
            icon.RemoveIcon();
            icon._hwnd = IntPtr.Zero;
            Native.PostQuitMessage(0);
            return IntPtr.Zero;
        }

        if (message == CallbackMessage)
        {
            switch ((uint)(lParam.ToInt64() & 0xFFFF))
            {
                case WmLButtonUp:
                case WmLButtonDblClk:
                case NinSelect:
                case NinKeySelect:
                    icon.OpenFromIcon();
                    return IntPtr.Zero;
                case WmRButtonUp:
                case WmContextMenu:
                    icon.ShowMenu();
                    return IntPtr.Zero;
            }
        }

        return Native.DefWindowProc(hwnd, message, wParam, lParam);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "RigForge tray icon is unavailable: {Error}")]
    private partial void LogTrayFailed(string error);

    private static partial class Native
    {
        internal delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WndClass
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Message
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct NotifyIconData
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ushort RegisterClass(ref WndClass windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateWindowEx(
            int exStyle,
            string className,
            string windowName,
            int style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern void PostQuitMessage(int exitCode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
        internal static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetMessage(out Message message, IntPtr hwnd, uint filterMin, uint filterMax);

        [DllImport("user32.dll")]
        internal static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll")]
        internal static extern IntPtr DispatchMessage(ref Message message);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string? moduleName);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int cx, int cy, uint loadFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string text);

        [DllImport("user32.dll")]
        internal static extern bool DestroyMenu(IntPtr menu);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    }
}
