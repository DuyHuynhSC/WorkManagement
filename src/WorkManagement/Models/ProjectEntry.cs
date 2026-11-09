using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace WorkManagement.Models;

public class ProjectEntry : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _no = string.Empty;
    private string _projectName = string.Empty;
    private string _description = string.Empty;
    private string _sourceFolder = string.Empty;
    private string _tnsName = string.Empty;
    private string _hostServer = string.Empty;
    private string _serviceName = "ORCL";
    private string _port = "1521";
    private string _dbUsers = string.Empty;
    private string _tablespace = string.Empty;
    private string _dbaUsername = "SYSTEM";
    private string _dbaPassword = string.Empty;
    private string _notes = string.Empty;
    private string _personInCharge = string.Empty;
    private string _notes2 = string.Empty;
    private string _resolvedDbaUser = string.Empty;
    private string _resolvedDbaPassword = string.Empty;
    private bool _isDbaPasswordMasked = true;
    private bool _isSelected;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string No
    {
        get => _no;
        set => SetField(ref _no, value);
    }

    public string ProjectName
    {
        get => _projectName;
        set => SetField(ref _projectName, value);
    }

    public string Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public string SourceFolder
    {
        get => _sourceFolder;
        set => SetField(ref _sourceFolder, value);
    }

    public string TnsName
    {
        get => _tnsName;
        set => SetField(ref _tnsName, value);
    }

    public string HostServer
    {
        get => _hostServer;
        set => SetField(ref _hostServer, value);
    }

    public string ServiceName
    {
        get => _serviceName;
        set => SetField(ref _serviceName, value);
    }

    public string Port
    {
        get => _port;
        set => SetField(ref _port, value);
    }

    public string DbUsers
    {
        get => _dbUsers;
        set => SetField(ref _dbUsers, value);
    }

    public string Tablespace
    {
        get => _tablespace;
        set => SetField(ref _tablespace, value);
    }

    public string DbaUsername
    {
        get => _dbaUsername;
        set => SetField(ref _dbaUsername, value);
    }

    public string DbaPassword
    {
        get => _dbaPassword;
        set => SetField(ref _dbaPassword, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetField(ref _notes, value);
    }

    public string PersonInCharge
    {
        get => _personInCharge;
        set => SetField(ref _personInCharge, value);
    }

    public string Notes2
    {
        get => _notes2;
        set => SetField(ref _notes2, value);
    }

    [JsonIgnore]
    public string ResolvedDbaUser
    {
        get => string.IsNullOrWhiteSpace(_resolvedDbaUser) ? _dbaUsername : _resolvedDbaUser;
        set => SetField(ref _resolvedDbaUser, value);
    }

    [JsonIgnore]
    public string ResolvedDbaPassword
    {
        get => string.IsNullOrEmpty(_resolvedDbaPassword) ? _dbaPassword : _resolvedDbaPassword;
        set
        {
            if (SetField(ref _resolvedDbaPassword, value))
            {
                OnPropertyChanged(nameof(DisplayDbaPassword));
            }
        }
    }

    [JsonIgnore]
    public bool IsDbaPasswordMasked
    {
        get => _isDbaPasswordMasked;
        set
        {
            if (SetField(ref _isDbaPasswordMasked, value))
            {
                OnPropertyChanged(nameof(DisplayDbaPassword));
            }
        }
    }

    [JsonIgnore]
    public string DisplayDbaPassword
    {
        get
        {
            string pass = ResolvedDbaPassword;
            return _isDbaPasswordMasked && !string.IsNullOrEmpty(pass)
                ? new string('•', Math.Min(pass.Length, 8))
                : pass;
        }
    }

    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public ProjectEntry Clone()
    {
        return new ProjectEntry
        {
            Id = Id,
            No = No,
            ProjectName = ProjectName,
            Description = Description,
            SourceFolder = SourceFolder,
            TnsName = TnsName,
            HostServer = HostServer,
            ServiceName = ServiceName,
            Port = Port,
            DbUsers = DbUsers,
            Tablespace = Tablespace,
            DbaUsername = DbaUsername,
            DbaPassword = DbaPassword,
            Notes = Notes,
            PersonInCharge = PersonInCharge,
            Notes2 = Notes2,
            ResolvedDbaUser = ResolvedDbaUser,
            ResolvedDbaPassword = ResolvedDbaPassword,
            IsDbaPasswordMasked = IsDbaPasswordMasked,
            IsSelected = IsSelected
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
