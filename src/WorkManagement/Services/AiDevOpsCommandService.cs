using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WorkManagement.Models;

namespace WorkManagement.Services;

public class AiDevOpsCommandService
{
    private readonly OpenAiClientService _aiClient;
    private readonly ConfigService _configService;
    private readonly VmManagerService _vmService;
    private readonly ProjectManagerService _projectService;

    public AiDevOpsCommandService(
        OpenAiClientService? aiClient = null,
        ConfigService? configService = null,
        VmManagerService? vmService = null,
        ProjectManagerService? projectService = null)
    {
        _aiClient = aiClient ?? new OpenAiClientService();
        _configService = configService ?? new ConfigService();
        _vmService = vmService ?? new VmManagerService();
        _projectService = projectService ?? new ProjectManagerService();
    }

    public async Task<AiDevOpsActionResult> DispatchCommandAsync(
        string userQuery,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
        {
            return new AiDevOpsActionResult
            {
                Success = false,
                ErrorMessage = "Vui lòng nhập câu lệnh hoặc yêu cầu."
            };
        }

        string query = userQuery.Trim();
        var settings = _configService.Load();

        // 1. Kiểm tra cấu hình AI Model nội bộ
        if (settings.Ai != null && settings.Ai.IsEnabled && !string.IsNullOrWhiteSpace(settings.Ai.BaseUrl))
        {
            try
            {
                var aiResult = await DispatchViaAiModelAsync(settings.Ai, query, cancellationToken);
                if (aiResult.Success && !string.IsNullOrWhiteSpace(aiResult.DisplayTitle))
                {
                    return aiResult;
                }
            }
            catch
            {
                // Fallback xuống heuristic local nếu gọi AI gặp trục trặc
            }
        }

        // 2. Fallback heuristic local thông minh
        return LocalHeuristicDispatch(query);
    }

    private async Task<AiDevOpsActionResult> DispatchViaAiModelAsync(
        AiSettings aiSettings,
        string userQuery,
        CancellationToken cancellationToken)
    {
        using var client = _aiClient.CreateHttpClient(aiSettings);
        string endpoint = OpenAiClientService.NormalizeChatCompletionsUrl(aiSettings.BaseUrl);

        var vms = _vmService.LoadAll();
        var projects = _projectService.LoadAll();

        var systemPrompt = BuildSystemPrompt(vms, projects);

        var requestBody = new
        {
            model = string.IsNullOrWhiteSpace(aiSettings.ModelName) ? "default" : aiSettings.ModelName.Trim(),
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userQuery }
            },
            temperature = 0.1
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        using var response = await client.PostAsync(endpoint, jsonContent, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return LocalHeuristicDispatch(userQuery);
        }

        string aiContent = OpenAiClientService.ExtractContentFromChatCompletion(responseText);
        return ParseAiCommandJson(aiContent, userQuery);
    }

    private string BuildSystemPrompt(List<VirtualMachineEntry> vms, List<ProjectEntry> projects)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Bạn là AI DevOps Command Center chuyên điều phối hành động và hỗ trợ vận hành cho hệ thống WorkManagement.");
        sb.AppendLine("Nhiệm vụ: Phân tích câu lệnh bằng ngôn ngữ tự nhiên của người dùng và quyết định một hành động phù hợp nhất.");
        sb.AppendLine();
        sb.AppendLine("TÀI NGUYÊN HỆ THỐNG:");

        sb.AppendLine("- DANH SÁCH MÁY ẢO HIỆN CÓ (VMS):");
        foreach (var vm in vms.Take(40))
        {
            if (string.IsNullOrWhiteSpace(vm.Ip)) continue;
            sb.AppendLine($"  * No: {vm.No} | Tên: {vm.MachineName} | IP: {vm.Ip} | DB: {vm.DatabaseName} | KT: {vm.Tester} | OS: {vm.Os}");
        }

        sb.AppendLine("- DANH SÁCH DỰ ÁN (PROJECTS):");
        foreach (var proj in projects.Take(30))
        {
            sb.AppendLine($"  * No: {proj.No} | Tên: {proj.ProjectName} | Host: {proj.HostServer} | Service: {proj.ServiceName} | DB Users: {proj.DbUsers}");
        }

        sb.AppendLine("- CÁC TRANG ĐIỀU HƯỚNG CÓ SẴN (VIEW NAMES):");
        sb.AppendLine("  * Dashboard: Trang tổng quan thống kê, KPI và nhật ký hoạt động");
        sb.AppendLine("  * FtpCleaner: Quét và dọn dẹp file cũ trên FTP Server");
        sb.AppendLine("  * VmManager: Danh sách máy ảo và kết nối RDP");
        sb.AppendLine("  * ProjectManager: Danh sách dự án và dọn dẹp Oracle DB");
        sb.AppendLine("  * TaskScheduler: Lịch chạy ngầm tự động");
        sb.AppendLine("  * FtpConfig: Cấu hình FTP site, proxy và bộ lọc file");
        sb.AppendLine("  * EmailConfig: Cấu hình gửi mail báo cáo SMTP");
        sb.AppendLine("  * AiConfig: Cấu hình AI Model nội bộ");
        sb.AppendLine();
        sb.AppendLine("ĐỊNH DẠNG ĐẦU RA (BẮT BUỘC TRẢ VỀ DUY NHẤT 1 KHỐI JSON HỢP LỆ, KHÔNG KÈM TEXT THỪA):");
        sb.AppendLine("{");
        sb.AppendLine("  \"action\": \"ConnectRdp\" | \"FilterVms\" | \"NavigateView\" | \"FilterProjects\" | \"OpenOracleScript\" | \"OpenProjectFolder\" | \"DirectAnswer\",");
        sb.AppendLine("  \"target\": \"<IP máy ảo | View name | từ khóa lọc | tên dự án | mã SQL>\",");
        sb.AppendLine("  \"title\": \"<Tiêu đề hành động ngắn gọn (dưới 60 ký tự)>\",");
        sb.AppendLine("  \"explanation\": \"<Lý do chọn hoặc câu trả lời tư vấn ngắn gọn>\"");
        sb.AppendLine("}");

        return sb.ToString();
    }

    public static AiDevOpsActionResult ParseAiCommandJson(string rawJson, string originalQuery)
    {
        var result = new AiDevOpsActionResult
        {
            RawAiResponse = rawJson
        };

        try
        {
            string cleanJson = rawJson.Trim();
            var match = Regex.Match(cleanJson, @"\{[\s\S]*\}");
            if (match.Success)
            {
                cleanJson = match.Value;
            }

            using var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            string actionStr = root.TryGetProperty("action", out var pAct) ? pAct.GetString() ?? "" : "";
            string target = root.TryGetProperty("target", out var pTar) ? pTar.GetString() ?? "" : "";
            string title = root.TryGetProperty("title", out var pTit) ? pTit.GetString() ?? "" : "";
            string explanation = root.TryGetProperty("explanation", out var pExp) ? pExp.GetString() ?? "" : "";

            if (Enum.TryParse<AiDevOpsActionType>(actionStr, ignoreCase: true, out var parsedAction))
            {
                result.ActionType = parsedAction;
            }
            else
            {
                result.ActionType = AiDevOpsActionType.DirectAnswer;
            }

            result.TargetParam = target;
            result.DisplayTitle = string.IsNullOrWhiteSpace(title) ? $"AI Thực thi: {actionStr}" : title;
            result.DisplayExplanation = explanation;
            result.Success = true;
            return result;
        }
        catch
        {
            // Nếu AI trả về text thông thường mà không phải JSON, chuyển thành DirectAnswer
            result.ActionType = AiDevOpsActionType.DirectAnswer;
            result.DisplayTitle = "AI DevOps Copilot Tư Vấn";
            result.DisplayExplanation = rawJson;
            result.TargetParam = rawJson;
            result.Success = true;
            return result;
        }
    }

    public AiDevOpsActionResult LocalHeuristicDispatch(string query)
    {
        string q = query.ToLowerInvariant();

        // 1. Kết nối RDP
        if (q.Contains("rdp") || q.Contains("remote") || q.Contains("kết nối máy") || q.Contains("vào máy"))
        {
            var vms = _vmService.LoadAll();
            var targetVm = vms.FirstOrDefault(v =>
                (!string.IsNullOrWhiteSpace(v.Ip) && q.Contains(v.Ip.ToLowerInvariant())) ||
                (!string.IsNullOrWhiteSpace(v.MachineName) && q.Contains(v.MachineName.ToLowerInvariant())) ||
                (!string.IsNullOrWhiteSpace(v.Tester) && q.Contains(v.Tester.ToLowerInvariant())) ||
                (!string.IsNullOrWhiteSpace(v.DatabaseName) && q.Contains(v.DatabaseName.ToLowerInvariant())));

            if (targetVm != null && !string.IsNullOrWhiteSpace(targetVm.Ip))
            {
                return new AiDevOpsActionResult
                {
                    ActionType = AiDevOpsActionType.ConnectRdp,
                    TargetParam = targetVm.Ip,
                    DisplayTitle = $"Kết nối RDP: {targetVm.MachineName} ({targetVm.Ip})",
                    DisplayExplanation = $"Phát hiện yêu cầu kết nối Remote Desktop tới máy ảo của {targetVm.Tester} (DB: {targetVm.DatabaseName})."
                };
            }
        }

        // 2. Sinh script Oracle
        if (q.Contains("script") || q.Contains("oracle") || q.Contains("xóa user") || q.Contains("tablespace") || q.Contains("kill session"))
        {
            var projects = _projectService.LoadAll();
            var proj = projects.FirstOrDefault(p =>
                (!string.IsNullOrWhiteSpace(p.ProjectName) && q.Contains(p.ProjectName.ToLowerInvariant())) ||
                (!string.IsNullOrWhiteSpace(p.No) && q.Contains(p.No.ToLowerInvariant())));

            string targetName = proj?.ProjectName ?? projects.FirstOrDefault()?.ProjectName ?? "";
            return new AiDevOpsActionResult
            {
                ActionType = AiDevOpsActionType.OpenOracleScript,
                TargetParam = targetName,
                DisplayTitle = $"Mở Trợ lý sinh Script Oracle: {targetName}",
                DisplayExplanation = "Khởi chạy công cụ AI Cố vấn an toàn Database & sinh script Oracle tương ứng."
            };
        }

        // 3. Điều hướng trang
        if (q.Contains("máy ảo") || q.Contains("vm"))
        {
            return new AiDevOpsActionResult
            {
                ActionType = AiDevOpsActionType.NavigateView,
                TargetParam = "VmManager",
                DisplayTitle = "Mở Danh Sách Máy Ảo (VMs & RDP)",
                DisplayExplanation = "Điều hướng tới trang quản lý máy ảo và kết nối RDP."
            };
        }

        if (q.Contains("dự án") || q.Contains("project"))
        {
            return new AiDevOpsActionResult
            {
                ActionType = AiDevOpsActionType.NavigateView,
                TargetParam = "ProjectManager",
                DisplayTitle = "Mở Quản Lý Dự Án & Oracle Cleanup",
                DisplayExplanation = "Điều hướng tới trang quản lý source code và schema database dự án."
            };
        }

        if (q.Contains("ftp") || q.Contains("dọn dẹp") || q.Contains("cleaner") || q.Contains("xóa file"))
        {
            return new AiDevOpsActionResult
            {
                ActionType = AiDevOpsActionType.NavigateView,
                TargetParam = "FtpCleaner",
                DisplayTitle = "Mở Quản Lý File & Dọn Dẹp FTP",
                DisplayExplanation = "Điều hướng tới trang quản lý file và dọn dẹp FTP tự động."
            };
        }

        if (q.Contains("lịch") || q.Contains("schedule") || q.Contains("tự động"))
        {
            return new AiDevOpsActionResult
            {
                ActionType = AiDevOpsActionType.NavigateView,
                TargetParam = "TaskScheduler",
                DisplayTitle = "Mở Lịch Chạy Tự Động (Task Scheduler)",
                DisplayExplanation = "Điều hướng tới trang cấu hình lịch chạy tác vụ tự động Windows."
            };
        }

        if (q.Contains("email") || q.Contains("mail") || q.Contains("smtp"))
        {
            return new AiDevOpsActionResult
            {
                ActionType = AiDevOpsActionType.NavigateView,
                TargetParam = "EmailConfig",
                DisplayTitle = "Mở Cấu Hình Gửi Mail (SMTP)",
                DisplayExplanation = "Điều hướng tới trang cấu hình máy chủ gửi email báo cáo."
            };
        }

        if (q.Contains("ai") || q.Contains("model") || q.Contains("llm") || q.Contains("endpoint"))
        {
            return new AiDevOpsActionResult
            {
                ActionType = AiDevOpsActionType.NavigateView,
                TargetParam = "AiConfig",
                DisplayTitle = "Mở Cấu Hình AI Model Nội Bộ",
                DisplayExplanation = "Điều hướng tới trang thiết lập AI Endpoint và kiểm tra model."
            };
        }

        // Mặc định: lọc danh sách máy ảo theo từ khóa
        return new AiDevOpsActionResult
        {
            ActionType = AiDevOpsActionType.FilterVms,
            TargetParam = query,
            DisplayTitle = $"Tìm kiếm máy ảo: \"{query}\"",
            DisplayExplanation = $"Lọc danh sách máy ảo theo từ khóa \"{query}\"."
        };
    }
}
