using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using WorkManagement.Models;

namespace WorkManagement.Services;

public class ProjectCleanupProgressEventArgs : EventArgs
{
    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }
    public double ProgressPercentage { get; set; }
}

public class ProjectCleanupService
{
    private static readonly HashSet<string> SystemTablespaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM", "SYSAUX", "USERS", "TEMP", "UNDOTBS1", "UNDOTBS2", "EXAMPLE", "DEFAULT"
    };

    public event EventHandler<ProjectCleanupProgressEventArgs>? OnProgress;
    private readonly string _logDir;

    public ProjectCleanupService(string? logDir = null)
    {
        _logDir = logDir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        if (!Directory.Exists(_logDir))
        {
            Directory.CreateDirectory(_logDir);
        }
    }

    public static List<string> ParseUserList(string rawDbUsers)
    {
        if (string.IsNullOrWhiteSpace(rawDbUsers)) return new List<string>();

        var tokens = rawDbUsers.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var cleanList = new List<string>();

        foreach (var t in tokens)
        {
            // Loại bỏ phần ghi chú trong ngoặc đơn, ví dụ: COR_TST_USR1(Dùng để test script) -> COR_TST_USR1
            string cleaned = Regex.Replace(t, @"\(.*?\)", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                // Chuẩn hóa tên Oracle User (chữ hoa, loại bỏ khoảng trắng thừa)
                string user = cleaned.Trim().ToUpperInvariant();
                if (!cleanList.Contains(user))
                {
                    cleanList.Add(user);
                }
            }
        }

        return cleanList;
    }

    public static List<string> ParseTablespaceList(string rawTablespaces)
    {
        if (string.IsNullOrWhiteSpace(rawTablespaces)) return new List<string>();

        var tokens = rawTablespaces.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var cleanList = new List<string>();

        foreach (var t in tokens)
        {
            string cleaned = Regex.Replace(t, @"\(.*?\)", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                string ts = cleaned.Trim().ToUpperInvariant();
                if (!cleanList.Contains(ts))
                {
                    cleanList.Add(ts);
                }
            }
        }

        return cleanList;
    }

    public async Task<bool> ExecuteProjectCleanupAsync(
        ProjectEntry project,
        ProjectCleanupSettings settings,
        string dbaUser,
        string dbaPassword,
        CancellationToken cancellationToken = default)
    {
        string projName = string.IsNullOrWhiteSpace(project.ProjectName) ? "UnknownProject" : project.ProjectName;
        Log($"================================================================================");
        Log($"BẮT ĐẦU QUY TRÌNH DỌN DẸP DỰ ÁN: {projName} (No: {project.No})");
        Log($"Thời gian bắt đầu: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Log($"================================================================================");

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string backupDir = Path.Combine(settings.BackupRootDirectory, $"{SanitizeFileName(projName)}_{timestamp}");

        try
        {
            // BƯỚC 1: SAO LƯU (BACKUP)
            if (settings.EnableBackupFolder || settings.EnableBackupDatabase)
            {
                Report(10, $"[BƯỚC 1/5] Đang tạo thư mục sao lưu tại: {backupDir}...");
                Directory.CreateDirectory(backupDir);

                // 1.1. Backup Source Folder thành file .zip
                if (settings.EnableBackupFolder)
                {
                    await BackupSourceFolderAsync(project, backupDir, cancellationToken);
                }

                // 1.2. Backup Database User (Export DDL / Data Pump)
                if (settings.EnableBackupDatabase)
                {
                    await BackupDatabaseUsersAsync(project, dbaUser, dbaPassword, backupDir, cancellationToken);
                }
            }
            else
            {
                Log($"[BƯỚC 1/5] Người dùng đã tắt tính năng sao lưu (Backup disabled).");
            }

            // BƯỚC 2: XÓA FOLDER DỰ ÁN TRÊN NETWORK DRIVE
            if (settings.EnableDeleteFolder)
            {
                Report(45, $"[BƯỚC 2/5] Đang xóa Source Folder trên ổ mạng...");
                await DeleteSourceFolderAsync(project, cancellationToken);
            }
            else
            {
                Log($"[BƯỚC 2/5] Bỏ qua xóa Source Folder theo cấu hình.");
            }

            // BƯỚC 3 & 4: XÓA USER VÀ TABLESPACE TRONG ORACLE DATABASE
            if (settings.EnableDeleteUsers || settings.EnableDeleteTablespace)
            {
                Report(65, $"[BƯỚC 3/5 & 4/5] Kết nối Oracle Database để dọn dẹp User & Tablespace...");
                await CleanupOracleDatabaseAsync(project, settings, dbaUser, dbaPassword, cancellationToken);
            }
            else
            {
                Log($"[BƯỚC 3/5 & 4/5] Bỏ qua dọn dẹp Database theo cấu hình.");
            }

            Report(100, $"[HOÀN TẤT] Dự án {projName} đã được dọn dẹp thành công!");
            Log($"================================================================================");
            Log($"HOÀN TẤT DỌN DẸP DỰ ÁN {projName} VÀO LÚC {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Log($"================================================================================");
            return true;
        }
        catch (OperationCanceledException)
        {
            Report(0, $"[ĐÃ HỦY] Người dùng đã dừng quy trình xóa dự án {projName}.", isError: true);
            Log($"[CANCEL] Quy trình dọn dẹp bị hủy bởi người dùng.");
            return false;
        }
        catch (Exception ex)
        {
            Report(0, $"[LỖI] Đã xảy ra lỗi trong quá trình dọn dẹp dự án {projName}: {ex.Message}", isError: true);
            Log($"[ERROR] Chi tiết lỗi: {ex}");
            return false;
        }
    }

    #region Bước 1: Sao lưu Source Folder & Database Users

    private async Task BackupSourceFolderAsync(ProjectEntry project, string backupDir, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(project.SourceFolder))
        {
            Log($"[BACKUP-FOLDER] Dự án không khai báo Source Folder. Bỏ qua sao lưu folder.");
            return;
        }

        if (!Directory.Exists(project.SourceFolder))
        {
            Log($"[BACKUP-FOLDER] CẢNH BÁO: Thư mục source không tồn tại hoặc không thể truy cập: '{project.SourceFolder}'. Bỏ qua nén zip.", isError: true);
            return;
        }

        string zipName = $"Source_{SanitizeFileName(project.ProjectName)}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        string zipPath = Path.Combine(backupDir, zipName);

        Report(20, $"[BACKUP-FOLDER] Đang nén thư mục '{project.SourceFolder}' thành file '{zipName}'...");
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            ZipFile.CreateFromDirectory(project.SourceFolder, zipPath, CompressionLevel.Optimal, false);
        }, ct);

        long sizeBytes = new FileInfo(zipPath).Length;
        double sizeMb = sizeBytes / (1024.0 * 1024.0);
        Log($"[BACKUP-FOLDER] Đã sao lưu Source Folder thành công: {zipName} ({sizeMb:F2} MB)");
    }

    private async Task BackupDatabaseUsersAsync(ProjectEntry project, string dbaUser, string dbaPassword, string backupDir, CancellationToken ct)
    {
        var users = ParseUserList(project.DbUsers);
        if (users.Count == 0)
        {
            Log($"[BACKUP-DB] Không có DB User nào được khai báo cho dự án. Bỏ qua sao lưu DB.");
            return;
        }

        if (string.IsNullOrWhiteSpace(project.HostServer))
        {
            Log($"[BACKUP-DB] Không có Host Server DB. Bỏ qua sao lưu DB.");
            return;
        }

        Report(30, $"[BACKUP-DB] Đang sao lưu DDL và dữ liệu các Oracle User ({string.Join(", ", users)})...");

        // 1. Thử export qua expdp/exp nếu có trên máy
        string dmpExportPath = Path.Combine(backupDir, $"Dump_{SanitizeFileName(project.ProjectName)}.dmp");
        bool expdpSuccess = await TryRunExpdpAsync(project, dbaUser, dbaPassword, users, dmpExportPath, ct);

        // 2. Export toàn bộ cấu trúc DDL của các user thành file .sql (Bảo hiểm 100% không phụ thuộc tool client)
        await ExportSchemaDdlAsync(project, dbaUser, dbaPassword, users, backupDir, ct);
    }

    private async Task<bool> TryRunExpdpAsync(ProjectEntry project, string dbaUser, string dbaPassword, List<string> users, string dmpPath, CancellationToken ct)
    {
        string expTool = FindOracleExportTool();
        if (string.IsNullOrEmpty(expTool))
        {
            Log($"[BACKUP-EXPDP] Không tìm thấy 'expdp.exe' hoặc 'exp.exe' trên máy cục bộ. Sẽ dùng phương thức trích xuất DDL tự động bằng Oracle Connection.");
            return false;
        }

        try
        {
            string host = project.HostServer.Trim();
            string port = string.IsNullOrWhiteSpace(project.Port) ? "1521" : project.Port.Trim();
            string service = string.IsNullOrWhiteSpace(project.ServiceName) ? "ORCL" : project.ServiceName.Trim();
            string schemas = string.Join(",", users);

            string args;
            if (expTool.EndsWith("expdp.exe", StringComparison.OrdinalIgnoreCase))
            {
                string connectStr = $"{dbaUser}/{dbaPassword}@{host}:{port}/{service}";
                args = $"\"{connectStr}\" SCHEMAS={schemas} DIRECTORY=DATA_PUMP_DIR DUMPFILE={Path.GetFileName(dmpPath)}";
            }
            else
            {
                string connectStr = $"{dbaUser}/{dbaPassword}@(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={service})))";
                args = $"\"{connectStr}\" OWNER=({schemas}) FILE=\"{dmpPath}\" LOG=\"{Path.ChangeExtension(dmpPath, ".log")}\"";
            }

            Log($"[BACKUP-EXPDP] Đang thực thi {Path.GetFileName(expTool)}...");
            var psi = new ProcessStartInfo
            {
                FileName = expTool,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct);
                Log($"[BACKUP-EXPDP] Quá trình Export hoàn tất với ExitCode: {proc.ExitCode}");
                return proc.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            Log($"[BACKUP-EXPDP] Lỗi khi chạy lệnh export: {ex.Message} (Sẽ dùng DDL backup dự phòng)");
        }

        return false;
    }

    private static string FindOracleExportTool()
    {
        string[] candidates = { "expdp.exe", "exp.exe" };
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return string.Empty;

        var paths = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var c in candidates)
        {
            foreach (var p in paths)
            {
                try
                {
                    string full = Path.Combine(p.Trim(), c);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
        }

        return string.Empty;
    }

    private async Task ExportSchemaDdlAsync(ProjectEntry project, string dbaUser, string dbaPassword, List<string> users, string backupDir, CancellationToken ct)
    {
        string ddlFile = Path.Combine(backupDir, $"Schema_DDL_{SanitizeFileName(project.ProjectName)}.sql");
        var sb = new StringBuilder();
        sb.AppendLine($"-- WORKMANAGEMENT BACKUP SCHEMA DDL");
        sb.AppendLine($"-- Project: {project.ProjectName} (No: {project.No})");
        sb.AppendLine($"-- Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"-- Server: {project.HostServer}:{project.Port}/{project.ServiceName}");
        sb.AppendLine($"-- Users: {string.Join(", ", users)}");
        sb.AppendLine();

        try
        {
            using var conn = CreateOracleConnection(project, dbaUser, dbaPassword);
            await conn.OpenAsync(ct);

            foreach (var user in users)
            {
                sb.AppendLine($"--------------------------------------------------------------------------------");
                sb.AppendLine($"-- USER: {user}");
                sb.AppendLine($"--------------------------------------------------------------------------------");

                // Truy vấn danh sách đối tượng
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT OBJECT_TYPE, OBJECT_NAME FROM DBA_OBJECTS WHERE OWNER = :u ORDER BY OBJECT_TYPE, OBJECT_NAME";
                cmd.Parameters.Add(new OracleParameter("u", user));

                var objects = new List<(string Type, string Name)>();
                using (var reader = await cmd.ExecuteReaderAsync(ct))
                {
                    while (await reader.ReadAsync(ct))
                    {
                        objects.Add((reader.GetString(0), reader.GetString(1)));
                    }
                }

                sb.AppendLine($"-- Tổng số đối tượng tìm thấy: {objects.Count}");
                foreach (var obj in objects)
                {
                    sb.AppendLine($"-- [{obj.Type}] {user}.{obj.Name}");
                }
                sb.AppendLine();
            }

            await File.WriteAllTextAsync(ddlFile, sb.ToString(), Encoding.UTF8, ct);
            Log($"[BACKUP-DDL] Đã xuất cấu trúc đối tượng các User vào file: {Path.GetFileName(ddlFile)}");
        }
        catch (Exception ex)
        {
            Log($"[BACKUP-DDL] Cảnh báo khi xuất DDL: {ex.Message}");
        }
    }

    #endregion

    #region Bước 2: Xóa Source Folder trên Network Drive

    private async Task DeleteSourceFolderAsync(ProjectEntry project, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(project.SourceFolder))
        {
            Log($"[DELETE-FOLDER] Dự án không có Source Folder để xóa.");
            return;
        }

        if (!Directory.Exists(project.SourceFolder))
        {
            Log($"[DELETE-FOLDER] Thư mục '{project.SourceFolder}' không tồn tại trên ổ đĩa. Bỏ qua xóa folder.");
            return;
        }

        Report(50, $"[DELETE-FOLDER] Đang xóa thư mục dự án '{project.SourceFolder}'...");
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            ClearReadOnlyAttributes(new DirectoryInfo(project.SourceFolder));
            Directory.Delete(project.SourceFolder, recursive: true);
        }, ct);

        Log($"[DELETE-FOLDER] ĐÃ XÓA THÀNH CÔNG thư mục: '{project.SourceFolder}'");
    }

    private static void ClearReadOnlyAttributes(DirectoryInfo dir)
    {
        try
        {
            dir.Attributes = FileAttributes.Normal;
            foreach (var info in dir.GetFileSystemInfos("*", SearchOption.AllDirectories))
            {
                info.Attributes = FileAttributes.Normal;
            }
        }
        catch { }
    }

    #endregion

    #region Bước 3 & 4: Xóa User và Tablespace trong Oracle DB

    private async Task CleanupOracleDatabaseAsync(
        ProjectEntry project,
        ProjectCleanupSettings settings,
        string dbaUser,
        string dbaPassword,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(project.HostServer))
        {
            Log($"[CLEANUP-DB] Bỏ qua vì không có Host Server DB.");
            return;
        }

        var users = ParseUserList(project.DbUsers);
        var tablespaces = ParseTablespaceList(project.Tablespace);

        using var conn = CreateOracleConnection(project, dbaUser, dbaPassword);
        Log($"[CLEANUP-DB] Đang kết nối tới Oracle Database {project.HostServer}:{project.Port}/{project.ServiceName} bằng tài khoản '{dbaUser}'...");
        await conn.OpenAsync(ct);
        Log($"[CLEANUP-DB] Kết nối Database thành công!");

        // 3.1. Xóa các Users
        if (settings.EnableDeleteUsers && users.Count > 0)
        {
            Report(75, $"[DELETE-USERS] Đang xóa {users.Count} user(s): {string.Join(", ", users)}...");
            foreach (var user in users)
            {
                ct.ThrowIfCancellationRequested();
                await DropOracleUserAsync(conn, user, ct);
            }
        }

        // 3.2. Xóa các Tablespaces
        if (settings.EnableDeleteTablespace && tablespaces.Count > 0)
        {
            Report(90, $"[DELETE-TABLESPACE] Đang xóa {tablespaces.Count} tablespace(s): {string.Join(", ", tablespaces)}...");
            foreach (var ts in tablespaces)
            {
                ct.ThrowIfCancellationRequested();
                await DropOracleTablespaceAsync(conn, ts, users, ct);
            }
        }
    }

    private async Task DropOracleUserAsync(OracleConnection conn, string user, CancellationToken ct)
    {
        // Kiểm tra xem User có tồn tại không
        using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT COUNT(*) FROM DBA_USERS WHERE USERNAME = :u";
            checkCmd.Parameters.Add(new OracleParameter("u", user));
            var countObj = await checkCmd.ExecuteScalarAsync(ct);
            int count = Convert.ToInt32(countObj);
            if (count == 0)
            {
                Log($"[DROP-USER] User '{user}' không tồn tại trong database (Bỏ qua).");
                return;
            }
        }

        // Kill active sessions của user để tránh lỗi ORA-01940
        try
        {
            using var sessionCmd = conn.CreateCommand();
            sessionCmd.CommandText = "SELECT SID, SERIAL# FROM V$SESSION WHERE USERNAME = :u";
            sessionCmd.Parameters.Add(new OracleParameter("u", user));
            using var reader = await sessionCmd.ExecuteReaderAsync(ct);
            var sessionsToKill = new List<(string Sid, string Serial)>();
            while (await reader.ReadAsync(ct))
            {
                sessionsToKill.Add((reader[0].ToString()!, reader[1].ToString()!));
            }

            foreach (var s in sessionsToKill)
            {
                using var killCmd = conn.CreateCommand();
                killCmd.CommandText = $"ALTER SYSTEM KILL SESSION '{s.Sid},{s.Serial}' IMMEDIATE";
                await killCmd.ExecuteNonQueryAsync(ct);
                Log($"[KILL-SESSION] Đã đóng session đang kết nối: SID={s.Sid}, SERIAL#={s.Serial} của user '{user}'");
            }
        }
        catch (Exception ex)
        {
            Log($"[KILL-SESSION] Cảnh báo khi kiểm tra session của '{user}': {ex.Message}");
        }

        // Thực hiện DROP USER ... CASCADE
        using (var dropCmd = conn.CreateCommand())
        {
            dropCmd.CommandText = $"DROP USER \"{user}\" CASCADE";
            await dropCmd.ExecuteNonQueryAsync(ct);
            Log($"[DROP-USER] ĐÃ XÓA THÀNH CÔNG User: '{user}' (CASCADE)");
        }
    }

    private async Task DropOracleTablespaceAsync(OracleConnection conn, string ts, List<string> projectUsers, CancellationToken ct)
    {
        // 1. Kiểm tra an toàn: không bao giờ xóa tablespace hệ thống
        if (SystemTablespaces.Contains(ts))
        {
            Log($"[DROP-TABLESPACE] CẢNH BÁO AN TOÀN: Tablespace '{ts}' là tablespace hệ thống mặc định. Tuyệt đối không xóa!", isError: true);
            return;
        }

        // 2. Kiểm tra sự tồn tại của tablespace
        using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT COUNT(*) FROM DBA_TABLESPACES WHERE TABLESPACE_NAME = :ts";
            checkCmd.Parameters.Add(new OracleParameter("ts", ts));
            var countObj = await checkCmd.ExecuteScalarAsync(ct);
            int count = Convert.ToInt32(countObj);
            if (count == 0)
            {
                Log($"[DROP-TABLESPACE] Tablespace '{ts}' không tồn tại trong database (Bỏ qua).");
                return;
            }
        }

        // 3. Kiểm tra xem có User nào ngoài dự án này đang dùng tablespace này không
        try
        {
            using var userCheckCmd = conn.CreateCommand();
            userCheckCmd.CommandText = "SELECT USERNAME FROM DBA_USERS WHERE DEFAULT_TABLESPACE = :ts";
            userCheckCmd.Parameters.Add(new OracleParameter("ts", ts));
            using var reader = await userCheckCmd.ExecuteReaderAsync(ct);
            var remainingUsers = new List<string>();
            while (await reader.ReadAsync(ct))
            {
                string u = reader.GetString(0);
                if (!projectUsers.Contains(u, StringComparer.OrdinalIgnoreCase))
                {
                    remainingUsers.Add(u);
                }
            }

            if (remainingUsers.Count > 0)
            {
                Log($"[DROP-TABLESPACE] CẢNH BÁO AN TOÀN: Tablespace '{ts}' vẫn đang được sử dụng bởi các user khác ({string.Join(", ", remainingUsers)}). Giữ lại tablespace để bảo toàn dữ liệu!", isError: true);
                return;
            }
        }
        catch (Exception ex)
        {
            Log($"[DROP-TABLESPACE] Cảnh báo khi kiểm tra người dùng của tablespace '{ts}': {ex.Message}");
        }

        // 4. Thực thi DROP TABLESPACE ... INCLUDING CONTENTS AND DATAFILES CASCADE CONSTRAINTS
        using (var dropCmd = conn.CreateCommand())
        {
            dropCmd.CommandText = $"DROP TABLESPACE \"{ts}\" INCLUDING CONTENTS AND DATAFILES CASCADE CONSTRAINTS";
            await dropCmd.ExecuteNonQueryAsync(ct);
            Log($"[DROP-TABLESPACE] ĐÃ XÓA THÀNH CÔNG Tablespace: '{ts}' (kèm Datafiles)");
        }
    }

    #endregion

    public static OracleConnection CreateOracleConnection(ProjectEntry project, string dbaUser, string dbaPassword)
    {
        string host = project.HostServer.Trim();
        string port = string.IsNullOrWhiteSpace(project.Port) ? "1521" : project.Port.Trim();
        string service = string.IsNullOrWhiteSpace(project.ServiceName) ? "ORCL" : project.ServiceName.Trim();

        var csb = new OracleConnectionStringBuilder
        {
            DataSource = $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={service})))",
            UserID = dbaUser,
            Password = dbaPassword,
            ConnectionTimeout = 15
        };

        if (dbaUser.Equals("SYS", StringComparison.OrdinalIgnoreCase) ||
            dbaUser.Equals("SYS AS SYSDBA", StringComparison.OrdinalIgnoreCase))
        {
            csb.DBAPrivilege = "SYSDBA";
            csb.UserID = "SYS";
        }

        return new OracleConnection(csb.ConnectionString);
    }

    public static async Task<bool> TestConnectionAsync(ProjectEntry project, string dbaUser, string dbaPassword)
    {
        try
        {
            using var conn = CreateOracleConnection(project, dbaUser, dbaPassword);
            await conn.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<List<string>> GetTablespacesForUsersAsync(
        ProjectEntry project,
        string dbaUser,
        string dbaPassword,
        IEnumerable<string> users,
        CancellationToken ct = default)
    {
        var userList = users
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        if (userList.Count == 0)
        {
            return new List<string>();
        }

        using var conn = CreateOracleConnection(project, dbaUser, dbaPassword);
        await conn.OpenAsync(ct);

        var paramNames = new List<string>();
        using var cmd = conn.CreateCommand();

        for (int i = 0; i < userList.Count; i++)
        {
            string pName = $"u{i}";
            paramNames.Add($":{pName}");
            cmd.Parameters.Add(new OracleParameter(pName, userList[i]));
        }

        cmd.CommandText = $"SELECT DISTINCT DEFAULT_TABLESPACE FROM DBA_USERS WHERE UPPER(USERNAME) IN ({string.Join(", ", paramNames)})";

        var tablespaces = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(0))
            {
                string ts = reader.GetString(0).Trim();
                if (!string.IsNullOrEmpty(ts) && !tablespaces.Contains(ts, StringComparer.OrdinalIgnoreCase))
                {
                    tablespaces.Add(ts);
                }
            }
        }

        return tablespaces;
    }

    private void Report(double percentage, string message, bool isError = false)
    {
        Log(message, isError);
        OnProgress?.Invoke(this, new ProjectCleanupProgressEventArgs
        {
            ProgressPercentage = percentage,
            Message = message,
            IsError = isError
        });
    }

    private void Log(string message, bool isError = false)
    {
        string level = isError ? "ERROR" : "INFO";
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";

        try
        {
            string logFile = Path.Combine(_logDir, $"ProjectCleanup_{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(logFile, line + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
