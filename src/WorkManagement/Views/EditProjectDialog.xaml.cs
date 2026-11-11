using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement.Views;

public partial class EditProjectDialog : Window
{
    public ProjectEntry Entry { get; private set; }
    private readonly List<VirtualMachineEntry> _availableVms = new();
    private bool _isPasswordRevealed;

    public EditProjectDialog(ProjectEntry? entryToEdit = null)
    {
        InitializeComponent();

        try
        {
            var vmService = new VmManagerService();
            _availableVms = vmService.LoadAll();
            CmbHostServer.ItemsSource = _availableVms.Select(x => x.Ip).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        }
        catch { }

        if (entryToEdit != null)
        {
            Entry = entryToEdit.Clone();
            TxtDialogTitle.Text = $"Chỉnh Sửa Dự Án: {Entry.ProjectName}";
            Title = $"Chỉnh Sửa Dự Án - {Entry.ProjectName}";
        }
        else
        {
            Entry = new ProjectEntry();
            TxtDialogTitle.Text = "Thêm Dự Án Mới";
            Title = "Thêm Dự Án Mới";
        }

        PopulateForm();
    }

    private void PopulateForm()
    {
        TxtNo.Text = Entry.No;
        TxtProjectName.Text = Entry.ProjectName;
        TxtDescription.Text = Entry.Description;
        TxtSourceFolder.Text = Entry.SourceFolder;
        TxtTnsName.Text = Entry.TnsName;
        CmbHostServer.Text = Entry.HostServer;
        TxtServiceName.Text = string.IsNullOrWhiteSpace(Entry.ServiceName) ? "ORCL" : Entry.ServiceName;
        TxtPort.Text = string.IsNullOrWhiteSpace(Entry.Port) ? "1521" : Entry.Port;
        TxtDbUsers.Text = Entry.DbUsers;
        TxtTablespace.Text = Entry.Tablespace;

        string host = Entry.HostServer?.Trim() ?? string.Empty;
        var matchedVm = _availableVms.FirstOrDefault(x => x.Ip.Equals(host, StringComparison.OrdinalIgnoreCase));
        string defaultDbaUser = (matchedVm != null && !string.IsNullOrWhiteSpace(matchedVm.DbaUser))
            ? matchedVm.DbaUser
            : (string.IsNullOrWhiteSpace(Entry.DbaUsername) ? "SYSTEM" : Entry.DbaUsername);
        string defaultDbaPass = (matchedVm != null && !string.IsNullOrWhiteSpace(matchedVm.DbaPassword))
            ? matchedVm.DbaPassword
            : Entry.DbaPassword;

        TxtDbaUsername.Text = defaultDbaUser;
        PbDbaPassword.Password = defaultDbaPass;
        TxtDbaPasswordRevealed.Text = defaultDbaPass;
        TxtNotes.Text = Entry.Notes;
        TxtPersonInCharge.Text = Entry.PersonInCharge;
        TxtNotes2.Text = Entry.Notes2;
    }

    private void CmbHostServer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string selectedIp = CmbHostServer.SelectedValue?.ToString() ?? CmbHostServer.Text;
        if (string.IsNullOrWhiteSpace(selectedIp)) return;

        var matchedVm = _availableVms.FirstOrDefault(x => x.Ip.Equals(selectedIp, StringComparison.OrdinalIgnoreCase));
        if (matchedVm != null)
        {
            if (!string.IsNullOrWhiteSpace(matchedVm.Tns) && string.IsNullOrWhiteSpace(TxtTnsName.Text))
            {
                TxtTnsName.Text = matchedVm.Tns;
            }
            if (!string.IsNullOrWhiteSpace(matchedVm.DatabaseName) && string.IsNullOrWhiteSpace(TxtServiceName.Text))
            {
                TxtServiceName.Text = matchedVm.DatabaseName;
            }
            if (!string.IsNullOrWhiteSpace(matchedVm.Tester) && string.IsNullOrWhiteSpace(TxtPersonInCharge.Text))
            {
                TxtPersonInCharge.Text = matchedVm.Tester;
            }
            if (!string.IsNullOrWhiteSpace(matchedVm.DbaUser))
            {
                TxtDbaUsername.Text = matchedVm.DbaUser;
            }
            if (!string.IsNullOrWhiteSpace(matchedVm.DbaPassword))
            {
                PbDbaPassword.Password = matchedVm.DbaPassword;
                TxtDbaPasswordRevealed.Text = matchedVm.DbaPassword;
            }
        }
    }

    private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Chọn thư mục Source của dự án (Network Drive / Local)",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            TxtSourceFolder.Text = dialog.FolderName;
        }
    }

    private void BtnToggleDbaPassword_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordRevealed = !_isPasswordRevealed;
        if (_isPasswordRevealed)
        {
            TxtDbaPasswordRevealed.Text = PbDbaPassword.Password;
            PbDbaPassword.Visibility = Visibility.Collapsed;
            TxtDbaPasswordRevealed.Visibility = Visibility.Visible;
            IconDbaPassEye.Kind = PackIconKind.EyeOffOutline;
        }
        else
        {
            PbDbaPassword.Password = TxtDbaPasswordRevealed.Text;
            TxtDbaPasswordRevealed.Visibility = Visibility.Collapsed;
            PbDbaPassword.Visibility = Visibility.Visible;
            IconDbaPassEye.Kind = PackIconKind.EyeOutline;
        }
    }

    private async void BtnTestDbConnection_Click(object sender, RoutedEventArgs e)
    {
        string host = CmbHostServer.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host))
        {
            MessageBox.Show(this, "Vui lòng nhập Host Server (IP) của Oracle DB.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string dbaUser = TxtDbaUsername.Text.Trim();
        string dbaPass = _isPasswordRevealed ? TxtDbaPasswordRevealed.Text : PbDbaPassword.Password;

        if (string.IsNullOrWhiteSpace(dbaUser))
        {
            MessageBox.Show(this, "Vui lòng nhập Tài khoản DBA (ví dụ: SYSTEM hoặc SYS).", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TxtStatusMessage.Text = "Đang kiểm tra kết nối Oracle DB...";
        BtnTestDbConnection.IsEnabled = false;

        var tempProject = new ProjectEntry
        {
            HostServer = host,
            Port = TxtPort.Text.Trim(),
            ServiceName = TxtServiceName.Text.Trim()
        };

        var (ok, errorMsg) = await ProjectCleanupService.TestConnectionAsync(tempProject, dbaUser, dbaPass);
        BtnTestDbConnection.IsEnabled = true;

        if (ok)
        {
            TxtStatusMessage.Text = "Kết nối Database THÀNH CÔNG!";
            MessageBox.Show(this, $"Kết nối tới Oracle Database tại {host}:{tempProject.Port}/{tempProject.ServiceName} thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            TxtStatusMessage.Text = "Kết nối Database THẤT BẠI.";
            MessageBox.Show(this, $"Không thể kết nối tới Oracle Database tại {host}:{tempProject.Port}/{tempProject.ServiceName}.\n\nChi tiết lỗi từ Oracle:\n{errorMsg}\n\nVui lòng kiểm tra lại IP, Port, Service Name / SID hoặc User/Password DBA.", "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnGetTablespace_Click(object sender, RoutedEventArgs e)
    {
        string host = CmbHostServer.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host))
        {
            MessageBox.Show(this, "Vui lòng nhập Host Server (IP) của Oracle DB.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string dbaUser = TxtDbaUsername.Text.Trim();
        string dbaPass = _isPasswordRevealed ? TxtDbaPasswordRevealed.Text : PbDbaPassword.Password;

        if (string.IsNullOrWhiteSpace(dbaUser))
        {
            MessageBox.Show(this, "Vui lòng nhập Tài khoản DBA (ví dụ: SYSTEM hoặc SYS).", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var users = ProjectCleanupService.ParseUserList(TxtDbUsers.Text);
        if (users.Count == 0)
        {
            MessageBox.Show(this, "Vui lòng nhập Danh sách DB User cần quản lý trước khi lấy Tablespace.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtDbUsers.Focus();
            return;
        }

        TxtStatusMessage.Text = "Đang truy vấn Tablespace từ Oracle DB...";
        BtnGetTablespace.IsEnabled = false;

        var tempProject = new ProjectEntry
        {
            HostServer = host,
            Port = TxtPort.Text.Trim(),
            ServiceName = TxtServiceName.Text.Trim()
        };

        try
        {
            var tablespaces = await ProjectCleanupService.GetTablespacesForUsersAsync(tempProject, dbaUser, dbaPass, users);
            if (tablespaces.Count > 0)
            {
                string result = string.Join(", ", tablespaces);
                TxtTablespace.Text = result;
                TxtStatusMessage.Text = $"Đã lấy Tablespace thành công: {result}";
                MessageBox.Show(this, $"Đã tìm thấy {tablespaces.Count} Tablespace cho các user ({string.Join(", ", users)}):\n\n{result}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                TxtStatusMessage.Text = "Không tìm thấy Tablespace nào cho danh sách User đã nhập.";
                MessageBox.Show(this, $"Không tìm thấy Tablespace nào trong Oracle DB tương ứng với các user ({string.Join(", ", users)}).\nVui lòng kiểm tra lại danh sách DB User hoặc quyền của tài khoản DBA.", "Không tìm thấy", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            TxtStatusMessage.Text = "Lỗi khi truy vấn Tablespace từ Database.";
            MessageBox.Show(this, $"Lỗi khi kết nối hoặc truy vấn Oracle Database:\n{ex.Message}", "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnGetTablespace.IsEnabled = true;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string projectName = TxtProjectName.Text.Trim();
        if (string.IsNullOrWhiteSpace(projectName))
        {
            MessageBox.Show(this, "Vui lòng nhập Tên Dự Án (Project Name).", "Ràng buộc", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtProjectName.Focus();
            return;
        }

        string dbaPass = _isPasswordRevealed ? TxtDbaPasswordRevealed.Text : PbDbaPassword.Password;

        Entry.No = TxtNo.Text.Trim();
        Entry.ProjectName = projectName;
        Entry.Description = TxtDescription.Text.Trim();
        Entry.SourceFolder = TxtSourceFolder.Text.Trim();
        Entry.TnsName = TxtTnsName.Text.Trim();
        Entry.HostServer = CmbHostServer.Text?.Trim() ?? string.Empty;
        Entry.ServiceName = string.IsNullOrWhiteSpace(TxtServiceName.Text) ? "ORCL" : TxtServiceName.Text.Trim();
        Entry.Port = string.IsNullOrWhiteSpace(TxtPort.Text) ? "1521" : TxtPort.Text.Trim();
        Entry.DbUsers = TxtDbUsers.Text.Trim();
        Entry.Tablespace = TxtTablespace.Text.Trim();
        Entry.DbaUsername = string.IsNullOrWhiteSpace(TxtDbaUsername.Text) ? "SYSTEM" : TxtDbaUsername.Text.Trim();
        Entry.DbaPassword = dbaPass;
        Entry.ResolvedDbaUser = Entry.DbaUsername;
        Entry.ResolvedDbaPassword = Entry.DbaPassword;
        Entry.Notes = TxtNotes.Text.Trim();
        Entry.PersonInCharge = TxtPersonInCharge.Text.Trim();
        Entry.Notes2 = TxtNotes2.Text.Trim();

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
