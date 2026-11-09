using System;
using System.Windows;
using MaterialDesignThemes.Wpf;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement.Views;

public partial class EditVmDialog : Window
{
    public VirtualMachineEntry Entry { get; private set; }
    private readonly RemoteDesktopService _rdpService;
    private bool _isPasswordRevealed;

    public EditVmDialog(VirtualMachineEntry? existing = null)
    {
        InitializeComponent();
        _rdpService = new RemoteDesktopService();

        if (existing != null)
        {
            Entry = existing.Clone();
            TxtHeaderTitle.Text = $"Chỉnh Sửa Máy Ảo: {Entry.MachineName ?? Entry.Ip}";
            LoadFromEntry();
        }
        else
        {
            Entry = new VirtualMachineEntry();
            TxtHeaderTitle.Text = "Thêm Máy Ảo Mới";
            TxtExpirationDate.Text = "Vô hạn";
        }
    }

    private void LoadFromEntry()
    {
        TxtNo.Text = Entry.No;
        TxtIp.Text = Entry.Ip;
        TxtMachineName.Text = Entry.MachineName;
        TxtDomain.Text = Entry.DomainOrWorkgroup;
        TxtOs.Text = Entry.Os;
        TxtUsername.Text = Entry.Username;
        PbPassword.Password = Entry.Password;
        TxtPassword.Text = Entry.Password;
        TxtTns.Text = Entry.Tns;
        TxtDatabaseName.Text = Entry.DatabaseName;
        TxtOracleVersion.Text = Entry.OracleVersion;
        TxtDbaUser.Text = Entry.DbaUser;
        TxtDbaPassword.Text = Entry.DbaPassword;
        TxtTester.Text = Entry.Tester;
        TxtNotes.Text = Entry.Notes;
        TxtExpirationDate.Text = string.IsNullOrWhiteSpace(Entry.ExpirationDate) ? "Vô hạn" : Entry.ExpirationDate;
    }

    private void SaveToEntry()
    {
        string rawPass = _isPasswordRevealed ? TxtPassword.Text : PbPassword.Password;

        Entry.No = TxtNo.Text.Trim();
        Entry.Ip = TxtIp.Text.Trim();
        Entry.MachineName = TxtMachineName.Text.Trim();
        Entry.DomainOrWorkgroup = TxtDomain.Text.Trim();
        Entry.Os = TxtOs.Text.Trim();
        Entry.Username = TxtUsername.Text.Trim();
        Entry.Password = rawPass.Trim();
        Entry.Tns = TxtTns.Text.Trim();
        Entry.DatabaseName = TxtDatabaseName.Text.Trim();
        Entry.OracleVersion = TxtOracleVersion.Text.Trim();
        Entry.DbaUser = TxtDbaUser.Text.Trim();
        Entry.DbaPassword = TxtDbaPassword.Text.Trim();
        Entry.Tester = TxtTester.Text.Trim();
        Entry.Notes = TxtNotes.Text.Trim();
        Entry.ExpirationDate = string.IsNullOrWhiteSpace(TxtExpirationDate.Text) ? "Vô hạn" : TxtExpirationDate.Text.Trim();
    }

    private void BtnTogglePassword_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordRevealed = !_isPasswordRevealed;
        if (_isPasswordRevealed)
        {
            TxtPassword.Text = PbPassword.Password;
            PbPassword.Visibility = Visibility.Collapsed;
            TxtPassword.Visibility = Visibility.Visible;
            IconPassEye.Kind = PackIconKind.EyeOffOutline;
        }
        else
        {
            PbPassword.Password = TxtPassword.Text;
            TxtPassword.Visibility = Visibility.Collapsed;
            PbPassword.Visibility = Visibility.Visible;
            IconPassEye.Kind = PackIconKind.EyeOutline;
        }
    }

    private async void BtnTestRdp_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtIp.Text))
        {
            MessageBox.Show(this, "Vui lòng nhập địa chỉ IP trước khi thử mở Remote Desktop.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SaveToEntry();
        bool success = await _rdpService.ConnectAsync(Entry);
        if (!success)
        {
            MessageBox.Show(this, "Không thể mở Remote Desktop. Vui lòng kiểm tra lại địa chỉ IP và quyền hệ thống.", "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtIp.Text))
        {
            MessageBox.Show(this, "Địa chỉ IP là bắt buộc.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtIp.Focus();
            return;
        }

        SaveToEntry();
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
