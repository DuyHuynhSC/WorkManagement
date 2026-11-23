using System;
using System.Windows;
using WorkManagement.Helpers;
using System.Collections.Generic;
using System.Linq;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configService = new ConfigService();
        var logger = new AppLogger();

        DispatcherUnhandledException += (sender, args) =>
        {
            logger.Error($"Lỗi ngoại lệ giao diện chưa xử lý (Unhandled): {args.Exception}");
            MessageBox.Show($"Đã xảy ra lỗi: {args.Exception.Message}\n\nChi tiết đã được ghi vào file log.", "Lỗi Ứng Dụng", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var ftpService = new FtpCleanupService(logger);
        var emailService = new EmailNotificationService(logger);
        var cliOptions = CliOptions.Parse(e.Args);

        if (cliOptions.IsConsoleMode)
        {
            NativeConsole.AttachOrCreateConsole();
            int exitCode = 0;
            try
            {
                var settings = configService.Load();
                string modeDesc = cliOptions.IsPreviewOnly ? "PREVIEW ONLY" : "AUTO BACKUP & DELETE";
                string scopeDesc = cliOptions.IsAllProfiles ? "TẤT CẢ PROFILES" : $"PROFILE '{settings.SelectedProfileName}'";
                logger.Info($"Chạy chế độ Console ({modeDesc}) - Phạm vi: {scopeDesc}.");

                var targetProfiles = new List<FtpSiteProfile>();
                if (cliOptions.IsAllProfiles && settings.Profiles != null && settings.Profiles.Count > 0)
                {
                    targetProfiles.AddRange(settings.Profiles);
                }
                else
                {
                    var curProfile = settings.Profiles?.FirstOrDefault(p =>
                        string.Equals(p.Name, settings.SelectedProfileName, StringComparison.OrdinalIgnoreCase))
                        ?? FtpSiteProfile.FromSettings(settings.SelectedProfileName, settings);
                    targetProfiles.Add(curProfile);
                }

                var allCandidates = new List<FtpFileCandidate>();

                foreach (var profile in targetProfiles)
                {
                    var profSettings = profile.ToSettings(settings);
                    logger.Info($"--- [Profile: {profile.Name}] Đang quét máy chủ FTP ({profSettings.Host}:{profSettings.Port})... ---");
                    try
                    {
                        var scanned = await ftpService.ScanCandidatesAsync(profSettings, DateTime.Now);
                        foreach (var c in scanned)
                        {
                            c.ProfileName = profile.Name;
                            allCandidates.Add(c);
                            if (cliOptions.IsPreviewOnly)
                            {
                                logger.Info($"[PREVIEW][Profile: {profile.Name}] Sẽ xóa: {c.FullPath} | Upload: {c.ModifiedDate:yyyy-MM-dd HH:mm:ss} | Size: {c.Size:N0} bytes");
                            }
                        }
                        logger.Info($"[Profile: {profile.Name}] Phát hiện {scanned.Count} file thỏa điều kiện xóa.");
                    }
                    catch (Exception exProf)
                    {
                        logger.Error($"[Profile: {profile.Name}] Quét thất bại: {exProf.Message}");
                        exitCode = 1;
                    }
                }

                if (cliOptions.IsPreviewOnly)
                {
                    logger.Info($"=== TỔNG KẾT PREVIEW: Tìm thấy {allCandidates.Count} file thỏa điều kiện xóa trên {targetProfiles.Count} Profile ===");

                    bool shouldSendEmail = (cliOptions.SendEmailIfCandidatesFound || settings.AutoSendEmailOnPreview) &&
                                           settings.RecipientEmails != null && settings.RecipientEmails.Count > 0;

                    if (shouldSendEmail)
                    {
                        if (allCandidates.Count > 0)
                        {
                            logger.Info($"Phát hiện {allCandidates.Count} file cần xóa. Đang gửi email báo cáo tới {settings.RecipientEmails!.Count} địa chỉ...");
                            await emailService.SendPreviewReportAsync(settings, allCandidates);
                            logger.Info("Đã gửi email báo cáo Preview thành công!");
                        }
                        else
                        {
                            logger.Info("Không có file nào thỏa điều kiện xóa trên các Profile. Bỏ qua bước gửi email.");
                        }
                    }
                }
                else
                {
                    if (allCandidates.Count == 0)
                    {
                        logger.Info("Không có file nào cần dọn dẹp.");
                    }
                    else
                    {
                        var groups = allCandidates.GroupBy(c => c.ProfileName).ToList();
                        int totalDeleted = 0;
                        int totalFailed = 0;

                        foreach (var group in groups)
                        {
                            var profile = targetProfiles.FirstOrDefault(p =>
                                string.Equals(p.Name, group.Key, StringComparison.OrdinalIgnoreCase));
                            var profSettings = profile != null ? profile.ToSettings(settings) : settings;

                            logger.Info($"=== Bắt đầu Backup & Xóa {group.Count()} file cho Profile '{group.Key}' ===");
                            var (deleted, failed) = await ftpService.ExecuteBackupAndDeleteAsync(profSettings, group.ToList());
                            totalDeleted += deleted;
                            totalFailed += failed;
                        }

                        logger.Info($"Hoàn tất Backup & Xóa: Đã xóa {totalDeleted} file, Lỗi {totalFailed} file.");
                        if (totalFailed > 0) exitCode = 1;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Lỗi nghiêm trọng trong chế độ Console: {ex.Message}");
                exitCode = 1;
            }
            finally
            {
                NativeConsole.FreeConsole();
                Shutdown(exitCode);
            }
            return;
        }

        var mainWindow = new MainWindow(configService, logger, ftpService, emailService);
        MainWindow = mainWindow;
        mainWindow.InitializeSystemTray(startMinimized: cliOptions.StartMinimizedToTray);
        if (!cliOptions.StartMinimizedToTray)
        {
            mainWindow.Show();
        }
    }
}
