using System;

namespace WorkManagement.Models;

public enum AiDevOpsActionType
{
    ConnectRdp,           // Kết nối RDP tới máy ảo (Target: IP hoặc MachineName)
    FilterVms,            // Mở danh sách máy ảo và lọc theo từ khóa (Target: query)
    NavigateView,         // Điều hướng tới màn hình (Target: Dashboard, FtpCleaner, VmManager, ProjectManager, TaskScheduler, FtpConfig, EmailConfig, AiConfig)
    FilterProjects,       // Mở quản lý dự án và lọc theo từ khóa (Target: query)
    OpenOracleScript,     // Mở trợ lý sinh script Oracle theo dự án (Target: ProjectName)
    OpenProjectFolder,    // Mở thư mục nguồn của dự án trong Explorer (Target: ProjectName hoặc Path)
    DirectAnswer          // Trả lời tư vấn DevOps / DBA trực tiếp (Target: lệnh hoặc nội dung trả lời)
}

public class AiDevOpsActionResult
{
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public AiDevOpsActionType ActionType { get; set; } = AiDevOpsActionType.DirectAnswer;
    public string TargetParam { get; set; } = string.Empty;
    public string DisplayTitle { get; set; } = string.Empty;
    public string DisplayExplanation { get; set; } = string.Empty;
    public string RawAiResponse { get; set; } = string.Empty;
    public Action? ExecutableAction { get; set; }
}
