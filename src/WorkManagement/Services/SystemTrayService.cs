using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using MaterialDesignThemes.Wpf;
using WorkManagement.Models;

namespace WorkManagement.Services;

/// <summary>
/// Quản lý biểu tượng chạy ngầm dưới Khay hệ thống (System Tray / Notification Area)
/// và phím tắt toàn cục (Global Hotkey) sử dụng Win32 Shell_NotifyIconW &amp; RegisterHotKey.
/// </summary>
public sealed class SystemTrayService : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYMESSAGE = WM_USER + 2026;
    private const int WM_HOTKEY = 0x0312;

    private const int HOTKEY_ID_CTRL_K = 9001;
    private const int HOTKEY_ID_CTRL_ALT_K = 9002;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_K = 0x4B;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int NIN_BALLOONUSERCLICK = WM_USER + 5;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_INFO = 0x00000001;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;
    private const int IDI_APPLICATION = 32512;

    private readonly Window _mainWindow;
    private readonly ConfigService _configService;
    private readonly FtpSettings _settings;
    private readonly AppLogger _logger;
    private readonly Action _onRestoreWindow;
    private readonly Action _onOpenCommandPalette;
    private readonly Action _onTriggerPreviewScan;
    private readonly Action _onExplicitExit;

    private HwndSource? _messageSink;
    private IntPtr _iconHandle = IntPtr.Zero;
    private bool _ownsIconHandle;
    private bool _isTrayAdded;
    private bool _isCtrlKRegistered;
    private bool _isCtrlAltKRegistered;
    private bool _disposed;
    private ContextMenu? _trayContextMenu;

    public bool IsInitialized => _isTrayAdded;

    public SystemTrayService(
        Window mainWindow,
        ConfigService configService,
        FtpSettings settings,
        AppLogger logger,
        Action onRestoreWindow,
        Action onOpenCommandPalette,
        Action onTriggerPreviewScan,
        Action onExplicitExit)
    {
        _mainWindow = mainWindow;
        _configService = configService;
        _settings = settings;
        _logger = logger;
        _onRestoreWindow = onRestoreWindow;
        _onOpenCommandPalette = onOpenCommandPalette;
        _onTriggerPreviewScan = onTriggerPreviewScan;
        _onExplicitExit = onExplicitExit;
    }

    public void Initialize()
    {
        if (_disposed || _isTrayAdded) return;

        try
        {
            var parameters = new HwndSourceParameters("WorkManagementTraySink")
            {
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0,
                WindowStyle = 0
            };

            _messageSink = new HwndSource(parameters);
            _messageSink.AddHook(WndProc);

            _iconHandle = ResolveTrayIconHandle(out _ownsIconHandle);

            var data = CreateNotifyIconData();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            data.uCallbackMessage = WM_TRAYMESSAGE;
            data.hIcon = _iconHandle;
            data.szTip = "WorkManagement v2.0 - Quản lý FTP, Máy ảo RDP & Dự án (Ctrl + K)";

            _isTrayAdded = Shell_NotifyIconW(NIM_ADD, ref data);

            // Luôn đăng ký phím tắt dự phòng Ctrl + Alt + K toàn hệ thống
            _isCtrlAltKRegistered = RegisterHotKey(
                _messageSink.Handle,
                HOTKEY_ID_CTRL_ALT_K,
                MOD_CONTROL | MOD_ALT | MOD_NOREPEAT,
                VK_K);
        }
        catch (Exception ex)
        {
            _logger.Error($"Không thể khởi tạo biểu tượng System Tray: {ex.Message}");
        }
    }

    /// <summary>
    /// Bật hoặc tắt đăng ký phím tắt toàn cục Ctrl + K khi ứng dụng thu nhỏ xuống System Tray
    /// (Khi cửa sổ chính đang mở, giải phóng Ctrl + K để không xung đột với trình duyệt/VS Code,
    /// còn khi ẩn xuống System Tray thì đăng ký Ctrl + K để bấm là bật ngay Command Palette).
    /// </summary>
    public void SetTrayGlobalHotkeyEnabled(bool enabled)
    {
        if (_disposed || _messageSink == null) return;

        try
        {
            if (enabled && !_isCtrlKRegistered)
            {
                _isCtrlKRegistered = RegisterHotKey(
                    _messageSink.Handle,
                    HOTKEY_ID_CTRL_K,
                    MOD_CONTROL | MOD_NOREPEAT,
                    VK_K);
            }
            else if (!enabled && _isCtrlKRegistered)
            {
                UnregisterHotKey(_messageSink.Handle, HOTKEY_ID_CTRL_K);
                _isCtrlKRegistered = false;
            }
        }
        catch
        {
            // Bỏ qua nếu phím tắt đang bị giữ bởi ứng dụng khác
        }
    }

    public void ShowBalloonNotification(string title, string message)
    {
        if (!_isTrayAdded || _disposed || _messageSink == null) return;

        try
        {
            var data = CreateNotifyIconData();
            data.uFlags = NIF_INFO | NIF_TIP | NIF_ICON | NIF_MESSAGE;
            data.uCallbackMessage = WM_TRAYMESSAGE;
            data.hIcon = _iconHandle;
            data.szTip = "WorkManagement v2.0 - Đang chạy ngầm (Nhấn Ctrl + K để mở nhanh)";
            data.szInfoTitle = TruncateString(title, 60);
            data.szInfo = TruncateString(message, 250);
            data.dwInfoFlags = NIIF_INFO;

            Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
        catch
        {
            // Bỏ qua nếu hệ điều hành tắt thông báo Balloon
        }
    }

    public ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint,
            MinWidth = 260
        };

        // 1. Mở cửa sổ chính
        var itemOpen = CreateMenuItem(
            "Mở WorkManagement",
            PackIconKind.ApplicationOutline,
            () => _onRestoreWindow(),
            isBold: true);
        menu.Items.Add(itemOpen);

        // 2. Mở nhanh Command Palette (Ctrl + K)
        var itemPalette = CreateMenuItem(
            "Tìm nhanh & Command Palette (Ctrl + K)",
            PackIconKind.Magnify,
            () => _onOpenCommandPalette());
        menu.Items.Add(itemPalette);

        menu.Items.Add(new Separator());

        // 3. Submenu Kết nối nhanh RDP tới Máy ảo
        var itemRdpRoot = new MenuItem
        {
            Header = "Kết nối nhanh RDP (Máy ảo)",
            Icon = new PackIcon { Kind = PackIconKind.RemoteDesktop, Width = 16, Height = 16 }
        };

        try
        {
            var vmService = new VmManagerService();
            var favVms = vmService.LoadAll().Where(v => v.IsFavorite && !string.IsNullOrWhiteSpace(v.Ip)).ToList();
            if (favVms.Count == 0)
            {
                itemRdpRoot.Items.Add(new MenuItem
                {
                    Header = "(Chưa chọn sao ★ máy ảo yêu thích nào)",
                    IsEnabled = false
                });
            }
            else
            {
                var rdpService = new RemoteDesktopService(_logger);
                foreach (var vm in favVms)
                {
                    var capturedVm = vm;
                    string label = string.IsNullOrWhiteSpace(capturedVm.MachineName)
                        ? $"★ {capturedVm.Ip}"
                        : $"★ {capturedVm.MachineName} ({capturedVm.Ip})";

                    var vmItem = CreateMenuItem(
                        label,
                        PackIconKind.DesktopTowerMonitor,
                        async () => await rdpService.ConnectAsync(capturedVm));
                    itemRdpRoot.Items.Add(vmItem);
                }
            }
        }
        catch
        {
            itemRdpRoot.Items.Add(new MenuItem { Header = "(Không thể tải danh sách VM)", IsEnabled = false });
        }

        menu.Items.Add(itemRdpRoot);

        // 4. Quét Xem Trước FTP Ngay
        var itemScan = CreateMenuItem(
            "Quét Xem Trước FTP Ngay (Preview)",
            PackIconKind.FolderSearchOutline,
            () =>
            {
                _onRestoreWindow();
                _onTriggerPreviewScan();
            });
        menu.Items.Add(itemScan);

        menu.Items.Add(new Separator());

        // 5. Tùy chọn thu nhỏ xuống Tray khi bấm nút X
        var itemToggleCloseToTray = new MenuItem
        {
            Header = "Thu nhỏ xuống Tray khi bấm Đóng (X)",
            IsCheckable = true,
            IsChecked = _settings.MinimizeToTrayOnClose
        };
        itemToggleCloseToTray.Click += (_, _) =>
        {
            _settings.MinimizeToTrayOnClose = itemToggleCloseToTray.IsChecked;
            try
            {
                _configService.Save(_settings);
            }
            catch { }
        };
        menu.Items.Add(itemToggleCloseToTray);

        menu.Items.Add(new Separator());

        // 6. Thoát hoàn toàn
        var itemExit = CreateMenuItem(
            "Thoát hoàn toàn (Exit)",
            PackIconKind.Power,
            () => _onExplicitExit());
        menu.Items.Add(itemExit);

        return menu;
    }

    private static MenuItem CreateMenuItem(string header, PackIconKind iconKind, Action onClick, bool isBold = false)
    {
        var item = new MenuItem
        {
            Header = header,
            Icon = new PackIcon { Kind = iconKind, Width = 16, Height = 16 },
            FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();
            if (hotkeyId == HOTKEY_ID_CTRL_K || hotkeyId == HOTKEY_ID_CTRL_ALT_K)
            {
                handled = true;
                _mainWindow.Dispatcher.BeginInvoke(_onOpenCommandPalette);
            }
        }
        else if (msg == WM_TRAYMESSAGE)
        {
            int mouseEvent = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouseEvent == WM_LBUTTONUP || mouseEvent == WM_LBUTTONDBLCLK || mouseEvent == NIN_BALLOONUSERCLICK)
            {
                handled = true;
                _mainWindow.Dispatcher.BeginInvoke(_onRestoreWindow);
            }
            else if (mouseEvent == WM_RBUTTONUP || mouseEvent == WM_CONTEXTMENU)
            {
                handled = true;
                _mainWindow.Dispatcher.BeginInvoke(ShowTrayContextMenu);
            }
        }

        return IntPtr.Zero;
    }

    private void ShowTrayContextMenu()
    {
        if (_disposed) return;

        try
        {
            if (_messageSink != null)
            {
                SetForegroundWindow(_messageSink.Handle);
            }

            _trayContextMenu = BuildContextMenu();
            _trayContextMenu.IsOpen = true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Không thể hiển thị menu System Tray: {ex.Message}");
        }
    }

    private NOTIFYICONDATAW CreateNotifyIconData()
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _messageSink?.Handle ?? IntPtr.Zero,
            uID = 1,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
        return data;
    }

    private static IntPtr ResolveTrayIconHandle(out bool ownsHandle)
    {
        ownsHandle = false;
        try
        {
            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(icoPath))
            {
                IntPtr hIcon = LoadImageW(IntPtr.Zero, icoPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE | LR_DEFAULTSIZE);
                if (hIcon != IntPtr.Zero)
                {
                    ownsHandle = true;
                    return hIcon;
                }
            }

            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                ushort iconIndex = 0;
                IntPtr hAssoc = ExtractAssociatedIconW(IntPtr.Zero, exePath, ref iconIndex);
                if (hAssoc != IntPtr.Zero)
                {
                    ownsHandle = true;
                    return hAssoc;
                }
            }
        }
        catch
        {
            // Fallback xuống icon mặc định của Windows
        }

        return LoadIconW(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
    }

    private static string TruncateString(string? input, int maxLength)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input.Length <= maxLength ? input : input.Substring(0, maxLength);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_trayContextMenu != null)
            {
                _trayContextMenu.IsOpen = false;
                _trayContextMenu = null;
            }

            if (_messageSink != null)
            {
                if (_isCtrlKRegistered)
                {
                    UnregisterHotKey(_messageSink.Handle, HOTKEY_ID_CTRL_K);
                    _isCtrlKRegistered = false;
                }

                if (_isCtrlAltKRegistered)
                {
                    UnregisterHotKey(_messageSink.Handle, HOTKEY_ID_CTRL_ALT_K);
                    _isCtrlAltKRegistered = false;
                }

                if (_isTrayAdded)
                {
                    var data = CreateNotifyIconData();
                    Shell_NotifyIconW(NIM_DELETE, ref data);
                    _isTrayAdded = false;
                }
            }
        }
        catch { }

        try
        {
            if (_ownsIconHandle && _iconHandle != IntPtr.Zero)
            {
                DestroyIcon(_iconHandle);
                _iconHandle = IntPtr.Zero;
            }
        }
        catch { }

        try
        {
            _messageSink?.Dispose();
            _messageSink = null;
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractAssociatedIconW(IntPtr hInst, string lpIconPath, ref ushort lpiIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
