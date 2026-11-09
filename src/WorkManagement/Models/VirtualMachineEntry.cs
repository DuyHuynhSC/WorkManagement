using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace WorkManagement.Models;

public class VirtualMachineEntry : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _no = string.Empty;
    private string _ip = string.Empty;
    private string _machineName = string.Empty;
    private string _domainOrWorkgroup = string.Empty;
    private string _os = string.Empty;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _tns = string.Empty;
    private string _databaseName = string.Empty;
    private string _oracleVersion = string.Empty;
    private string _tester = string.Empty;
    private string _notes = string.Empty;
    private string _expirationDate = "Vô hạn";
    private string _dbaUser = string.Empty;
    private string _dbaPassword = string.Empty;
    private bool _isFavorite;
    private bool _isPasswordMasked = true;
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

    public string Ip
    {
        get => _ip;
        set => SetField(ref _ip, value);
    }

    public string MachineName
    {
        get => _machineName;
        set => SetField(ref _machineName, value);
    }

    public string DomainOrWorkgroup
    {
        get => _domainOrWorkgroup;
        set => SetField(ref _domainOrWorkgroup, value);
    }

    public string Os
    {
        get => _os;
        set => SetField(ref _os, value);
    }

    public string Username
    {
        get => _username;
        set => SetField(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }

    public string Tns
    {
        get => _tns;
        set => SetField(ref _tns, value);
    }

    public string DatabaseName
    {
        get => _databaseName;
        set => SetField(ref _databaseName, value);
    }

    public string OracleVersion
    {
        get => _oracleVersion;
        set => SetField(ref _oracleVersion, value);
    }

    public string Tester
    {
        get => _tester;
        set => SetField(ref _tester, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetField(ref _notes, value);
    }

    public string ExpirationDate
    {
        get => _expirationDate;
        set => SetField(ref _expirationDate, value);
    }

    public string DbaUser
    {
        get => _dbaUser;
        set => SetField(ref _dbaUser, value);
    }

    public string DbaPassword
    {
        get => _dbaPassword;
        set
        {
            if (SetField(ref _dbaPassword, value))
            {
                OnPropertyChanged(nameof(DisplayDbaPassword));
            }
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetField(ref _isFavorite, value);
    }

    public bool IsPasswordMasked
    {
        get => _isPasswordMasked;
        set
        {
            if (SetField(ref _isPasswordMasked, value))
            {
                OnPropertyChanged(nameof(DisplayPassword));
                OnPropertyChanged(nameof(DisplayDbaPassword));
            }
        }
    }

    public string DisplayPassword => _isPasswordMasked && !string.IsNullOrEmpty(_password)
        ? new string('•', Math.Min(_password.Length, 8))
        : _password;

    public string DisplayDbaPassword => _isPasswordMasked && !string.IsNullOrEmpty(_dbaPassword)
        ? new string('•', Math.Min(_dbaPassword.Length, 8))
        : _dbaPassword;

    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public VirtualMachineEntry Clone()
    {
        return new VirtualMachineEntry
        {
            Id = Id,
            No = No,
            Ip = Ip,
            MachineName = MachineName,
            DomainOrWorkgroup = DomainOrWorkgroup,
            Os = Os,
            Username = Username,
            Password = Password,
            Tns = Tns,
            DatabaseName = DatabaseName,
            OracleVersion = OracleVersion,
            Tester = Tester,
            Notes = Notes,
            ExpirationDate = ExpirationDate,
            DbaUser = DbaUser,
            DbaPassword = DbaPassword,
            IsFavorite = IsFavorite,
            IsPasswordMasked = IsPasswordMasked,
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
