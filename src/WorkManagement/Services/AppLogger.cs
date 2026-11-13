using System;
using System.IO;

namespace WorkManagement.Services;

public class AppLogger
{
    private string _logDir;
    public string LogDirectory => _logDir;
    public event Action<string>? OnLogMessage;

    public AppLogger(string? logDir = null)
    {
        _logDir = !string.IsNullOrWhiteSpace(logDir)
            ? logDir
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        EnsureDirectory();
    }

    public void SetLogDirectory(string? logDir)
    {
        if (string.IsNullOrWhiteSpace(logDir)) return;
        _logDir = logDir;
        EnsureDirectory();
    }

    private void EnsureDirectory()
    {
        try
        {
            if (!Directory.Exists(_logDir))
            {
                Directory.CreateDirectory(_logDir);
            }
        }
        catch { }
    }

    public void Info(string message) => Write("INFO", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        Console.WriteLine(line);
        OnLogMessage?.Invoke(line);

        try
        {
            string logFile = Path.Combine(_logDir, $"WorkManagement_{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(logFile, line + Environment.NewLine);
        }
        catch
        {
            // Tránh crash ứng dụng nếu file log bị khóa
        }
    }
}
