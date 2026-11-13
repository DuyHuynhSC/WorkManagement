using System.Windows;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement.Views;

public partial class BackupFolderSettingsWindow : Window
{
    public BackupFolderSettingsWindow(ConfigService? configService = null, AppLogger? logger = null)
    {
        InitializeComponent();
        InnerView.Initialize(configService, logger);
        InnerView.SettingsSaved += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
    }

    public BackupFolderSettings ReadFromUi() => InnerView.ReadFromUi();
}
