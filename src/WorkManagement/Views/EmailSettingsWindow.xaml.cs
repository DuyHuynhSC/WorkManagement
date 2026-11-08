using System;
using System.Collections.Generic;
using System.Windows;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement.Views;

public partial class EmailSettingsWindow : Window
{
    public EmailSettingsWindow(
        FtpSettings settings,
        EmailNotificationService emailService,
        IReadOnlyList<FtpFileCandidate> currentCandidates)
    {
        InitializeComponent();
        InnerView.Initialize(settings, emailService, () => currentCandidates);
        InnerView.SettingsSaved += (_, _) =>
        {
            if (IsLoaded)
            {
                DialogResult = true;
                Close();
            }
        };
    }

    public void ApplyToSettings(FtpSettings target)
    {
        InnerView.ApplyToSettings(target);
    }
}
