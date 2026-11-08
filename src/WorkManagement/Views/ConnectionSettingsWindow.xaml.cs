using System;
using System.Windows;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement.Views;

public partial class ConnectionSettingsWindow : Window
{
    public ConnectionSettingsWindow(
        FtpSettings settings,
        ConfigService configService,
        FtpCleanupService ftpService,
        AppLogger logger)
    {
        InitializeComponent();
        InnerView.Initialize(settings, configService, ftpService, logger);
        InnerView.SettingsSaved += (_, _) =>
        {
            if (IsLoaded)
            {
                DialogResult = true;
                Close();
            }
        };
    }

    public FtpSettings ReadSettingsFromUi()
    {
        return InnerView.ReadSettingsFromUi();
    }
}
