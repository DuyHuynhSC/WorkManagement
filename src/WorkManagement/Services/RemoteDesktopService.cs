using System;
using System.Diagnostics;
using System.Threading.Tasks;
using WorkManagement.Models;

namespace WorkManagement.Services;

public class RemoteDesktopService
{
    private readonly AppLogger _logger;

    public RemoteDesktopService(AppLogger? logger = null)
    {
        _logger = logger ?? new AppLogger();
    }

    public static string FormatUsername(string username, string? hostOrIp)
    {
        if (string.IsNullOrWhiteSpace(username)) return string.Empty;

        // Nếu username đã có ký tự \ hoặc @ thì giữ nguyên
        if (username.Contains('\\') || username.Contains('@'))
        {
            return username.Trim();
        }

        if (!string.IsNullOrWhiteSpace(hostOrIp))
        {
            return $"{hostOrIp.Trim()}\\{username.Trim()}";
        }

        return username.Trim();
    }

    public async Task<bool> ConnectAsync(VirtualMachineEntry vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Ip))
        {
            _logger.Error("Không thể mở Remote Desktop: Địa chỉ IP bị trống.");
            return false;
        }

        try
        {
            string ip = vm.Ip.Trim();
            // Lấy [Địa chỉ IP] + "\" + [User] theo yêu cầu
            string user = FormatUsername(vm.Username, ip);
            string password = vm.Password ?? string.Empty;

            if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(password))
            {
                // Inject Windows credential using cmdkey
                await RunProcessAsync("cmdkey.exe", $"/generic:TERMSRV/{ip} /user:\"{user}\" /pass:\"{password}\"");
                _logger.Info($"Đã cập nhật credential đăng nhập cho máy {ip} ({user}).");
            }

            // Launch mstsc.exe
            var psi = new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = $"/v:{ip}",
                UseShellExecute = true
            };
            Process.Start(psi);
            _logger.Info($"Đã khởi động Remote Desktop tới {ip} với user {user}.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Lỗi khi mở Remote Desktop tới {vm.Ip}: {ex.Message}");
            return false;
        }
    }

    private static async Task RunProcessAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var proc = Process.Start(psi);
        if (proc != null)
        {
            await proc.WaitForExitAsync();
        }
    }
}
