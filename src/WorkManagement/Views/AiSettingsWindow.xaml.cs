using System.Windows;
using WorkManagement.Services;

namespace WorkManagement.Views;

public partial class AiSettingsWindow : Window
{
    public AiSettingsWindow(ConfigService? configService = null, AppLogger? logger = null)
    {
        InitializeComponent();
        var cfg = configService ?? new ConfigService();
        InnerView.Initialize(cfg, logger);
        InnerView.SettingsSaved += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
    }
}
