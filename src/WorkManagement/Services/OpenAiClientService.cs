using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WorkManagement.Models;

namespace WorkManagement.Services;

public class AiSchemaMappingResult
{
    public bool Success { get; set; }
    public int HeaderRowIndex { get; set; } = -1;
    public Dictionary<string, string> ColumnMapping { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public enum DatabaseRiskLevel
{
    Low,       // An toàn
    Medium,    // Cần lưu ý
    High       // Nguy hiểm cao
}

public class DatabaseSafetyAnalysisResult
{
    public bool Success { get; set; }
    public DatabaseRiskLevel RiskLevel { get; set; } = DatabaseRiskLevel.Low;
    public List<string> Warnings { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public class OracleScriptGenerationResult
{
    public bool Success { get; set; }
    public string Script { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public class OpenAiClientService
{
    private readonly HttpMessageHandler? _customHandler;

    public OpenAiClientService(HttpMessageHandler? customHandler = null)
    {
        _customHandler = customHandler;
    }

    public HttpClient CreateHttpClient(AiSettings settings)
    {
        HttpMessageHandler handler;
        if (_customHandler != null)
        {
            handler = _customHandler;
        }
        else if (settings.BypassProxy)
        {
            // Bỏ qua hoàn toàn Proxy doanh nghiệp và System Proxy
            handler = new SocketsHttpHandler
            {
                UseProxy = false,
                Proxy = null
            };
        }
        else
        {
            handler = new SocketsHttpHandler();
        }

        var client = new HttpClient(handler, disposeHandler: _customHandler == null)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(5, settings.TimeoutSeconds))
        };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
        }

        return client;
    }

    public static string NormalizeModelsUrl(string baseUrl)
    {
        string trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - "/chat/completions".Length);
        }
        if (trimmed.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }
        return $"{trimmed}/models";
    }

    public async Task<(bool Success, List<string> Models, string Message)> GetAvailableModelsAsync(
        AiSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return (false, new List<string>(), "URL Endpoint của AI Model không được để trống.");
        }

        try
        {
            using var client = CreateHttpClient(settings);
            string endpoint = NormalizeModelsUrl(settings.BaseUrl);

            using var response = await client.GetAsync(endpoint, cancellationToken);
            string responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return (false, new List<string>(), $"AI Server trả về mã lỗi {response.StatusCode}: {responseText}");
            }

            var models = ParseModelsJson(responseText);
            if (models.Count == 0)
            {
                return (true, models, "Kết nối thành công nhưng không tìm thấy model nào trong danh sách trả về.");
            }

            return (true, models, $"Lấy danh sách thành công! Tìm thấy {models.Count} model.");
        }
        catch (HttpRequestException ex)
        {
            return (false, new List<string>(), $"Không thể kết nối đến AI Endpoint (BypassProxy: {settings.BypassProxy}): {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, new List<string>(), $"Quá thời gian chờ phản hồi từ AI Server ({settings.TimeoutSeconds}s).");
        }
        catch (Exception ex)
        {
            return (false, new List<string>(), $"Lỗi khi tải danh sách Model: {ex.Message}");
        }
    }

    public static List<string> ParseModelsJson(string json)
    {
        var modelList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Định dạng chuẩn OpenAI: { "data": [ { "id": "model-id" } ] }
            if (root.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataElem.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idElem) && !string.IsNullOrWhiteSpace(idElem.GetString()))
                    {
                        modelList.Add(idElem.GetString()!.Trim());
                    }
                }
            }

            // Định dạng Ollama / Local servers: { "models": [ { "name": "qwen2.5" } ] }
            if (root.TryGetProperty("models", out var modelsElem) && modelsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in modelsElem.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameElem) && !string.IsNullOrWhiteSpace(nameElem.GetString()))
                    {
                        modelList.Add(nameElem.GetString()!.Trim());
                    }
                    else if (item.TryGetProperty("id", out var idElem) && !string.IsNullOrWhiteSpace(idElem.GetString()))
                    {
                        modelList.Add(idElem.GetString()!.Trim());
                    }
                }
            }

            // Định dạng mảng chuỗi đơn giản: [ "model1", "model2" ]
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        modelList.Add(item.GetString()!.Trim());
                    }
                }
            }
        }
        catch
        {
            // Bỏ qua lỗi parse
        }

        return modelList.OrderBy(m => m).ToList();
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(AiSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                return (false, "URL Endpoint của AI Model không được để trống.");
            }

            using var client = CreateHttpClient(settings);
            string endpoint = NormalizeChatCompletionsUrl(settings.BaseUrl);

            var requestBody = new
            {
                model = string.IsNullOrWhiteSpace(settings.ModelName) ? "default" : settings.ModelName.Trim(),
                messages = new object[]
                {
                    new { role = "user", content = "Ping test. Please reply: OK" }
                },
                max_tokens = 10,
                temperature = 0.0
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            using var response = await client.PostAsync(endpoint, jsonContent, cancellationToken);
            string responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return (true, $"Kết nối AI thành công ({response.StatusCode}). Phản hồi: {ExtractContentFromChatCompletion(responseText)}");
            }
            else
            {
                return (false, $"Lỗi từ AI Server ({response.StatusCode}): {responseText}");
            }
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Không thể kết nối đến AI Endpoint (Bỏ qua Proxy: {settings.BypassProxy}): {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, $"Quá thời gian chờ phản hồi từ AI Server ({settings.TimeoutSeconds}s).");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối AI: {ex.Message}");
        }
    }

    public async Task<AiSchemaMappingResult> InferVmSchemaMappingAsync(
        AiSettings settings,
        Dictionary<string, string> candidateHeaders,
        List<Dictionary<string, string>> sampleRows,
        CancellationToken cancellationToken = default)
    {
        var targetProps = new Dictionary<string, string>
        {
            { "No", "Số thứ tự dòng (STT, No)" },
            { "Ip", "Địa chỉ IP máy ảo hoặc server (ví dụ: 192.168.x.x, 10.x.x.x)" },
            { "MachineName", "Tên máy ảo, hostname hoặc computer name" },
            { "Domain", "Domain hoặc tên Workgroup" },
            { "Os", "Hệ điều hành máy ảo (Windows Server, RedHat, Ubuntu, CentOS...)" },
            { "Username", "Tên tài khoản đăng nhập máy ảo (Administrator, user...)" },
            { "Password", "Mật khẩu đăng nhập máy ảo (RDP/SSH Password)" },
            { "Tns", "Chuỗi kết nối Oracle TNS, Service Name hoặc TNS Alias" },
            { "DatabaseName", "Tên cơ sở dữ liệu (DB Name, SID)" },
            { "OracleVersion", "Phiên bản Oracle (11g, 12c, 19c, 21c...)" },
            { "Tester", "Người kiểm tra, Tester, QA phụ trách hoặc PIC" },
            { "Notes", "Ghi chú, mục đích sử dụng máy ảo" },
            { "ExpirationDate", "Ngày hết hạn máy ảo (Hạn dùng, Expire date...)" },
            { "DbaUser", "Tài khoản DBA hoặc SYSDBA Oracle" },
            { "DbaPassword", "Mật khẩu tài khoản DBA/SYSDBA" }
        };

        return await InferSchemaMappingCoreAsync(
            settings,
            entityName: "VirtualMachine (Danh mục máy ảo & RDP)",
            candidateHeaders,
            sampleRows,
            targetProps,
            cancellationToken);
    }

    public async Task<AiSchemaMappingResult> InferProjectSchemaMappingAsync(
        AiSettings settings,
        Dictionary<string, string> candidateHeaders,
        List<Dictionary<string, string>> sampleRows,
        CancellationToken cancellationToken = default)
    {
        var targetProps = new Dictionary<string, string>
        {
            { "No", "Số thứ tự dòng (STT, No)" },
            { "ProjectName", "Tên dự án" },
            { "SourceFolder", "Đường dẫn thư mục mã nguồn cục bộ" },
            { "SizeGb", "Dung lượng thư mục dự án (GB)" },
            { "HostServer", "Địa chỉ máy chủ cơ sở dữ liệu Oracle (Host IP)" },
            { "Port", "Cổng kết nối Oracle (mặc định 1521)" },
            { "ServiceName", "Tên Service của Oracle Database" },
            { "SysDbaUser", "Tài khoản SYSDBA Oracle (mặc định SYS)" },
            { "SysDbaPassword", "Mật khẩu SYSDBA" },
            { "DbUsers", "Danh sách DB Users / Schemas cần dọn dẹp" },
            { "Tablespaces", "Danh sách Tablespaces cần dọn dẹp" },
            { "PersonInCharge", "Người phụ trách dự án (PIC)" },
            { "Status", "Trạng thái dự án" }
        };

        return await InferSchemaMappingCoreAsync(
            settings,
            entityName: "Project (Danh mục dự án & Oracle DB)",
            candidateHeaders,
            sampleRows,
            targetProps,
            cancellationToken);
    }

    private async Task<AiSchemaMappingResult> InferSchemaMappingCoreAsync(
        AiSettings settings,
        string entityName,
        Dictionary<string, string> candidateHeaders,
        List<Dictionary<string, string>> sampleRows,
        Dictionary<string, string> targetProps,
        CancellationToken cancellationToken)
    {
        var result = new AiSchemaMappingResult();

        if (candidateHeaders.Count == 0)
        {
            result.ErrorMessage = "Không có cột nào trong dữ liệu Excel đầu vào.";
            return result;
        }

        try
        {
            using var client = CreateHttpClient(settings);
            string endpoint = NormalizeChatCompletionsUrl(settings.BaseUrl);

            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine($"Bạn là trợ lý AI chuyên phân tích và ánh xạ cấu trúc cột file Excel cho hệ sinh thái quản lý: {entityName}.");
            promptBuilder.AppendLine("Nhiệm vụ của bạn: Dựa vào các tên cột Excel (tương ứng với các chữ cái cột A, B, C...) và dữ liệu mẫu của các dòng, hãy xác định chính xác mỗi cột Excel map vào thuộc tính mục tiêu nào.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("DANH SÁCH THUỘC TÍNH MỤC TIÊU:");
            foreach (var kv in targetProps)
            {
                promptBuilder.AppendLine($"- {kv.Key}: {kv.Value}");
            }
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("CÁC CỘT EXCEL PHÁT HIỆN:");
            foreach (var kv in candidateHeaders)
            {
                promptBuilder.AppendLine($"- Cột [{kv.Key}]: \"{kv.Value}\"");
            }
            promptBuilder.AppendLine();

            if (sampleRows.Count > 0)
            {
                promptBuilder.AppendLine("DỮ LIỆU MẪU CÁC DÒNG (Dùng để suy luận kiểu dữ liệu, ví dụ phân biệt IP, ngày tháng, tên người):");
                for (int i = 0; i < sampleRows.Count; i++)
                {
                    promptBuilder.AppendLine($"Dòng mẫu {i + 1}:");
                    foreach (var col in sampleRows[i])
                    {
                        promptBuilder.AppendLine($"  [{col.Key}]: {col.Value}");
                    }
                }
                promptBuilder.AppendLine();
            }

            promptBuilder.AppendLine("QUY TẮC:");
            promptBuilder.AppendLine("1. Chỉ map các cột chắc chắn có ý nghĩa vào thuộc tính mục tiêu. Nếu cột không khớp thuộc tính nào thì không đưa vào mapping.");
            promptBuilder.AppendLine("2. Mỗi thuộc tính mục tiêu chỉ nên được map vào 1 cột Excel phù hợp nhất.");
            promptBuilder.AppendLine("3. Bắt buộc trả về định dạng JSON thuần túy (không bọc text ngoài JSON):");
            promptBuilder.AppendLine("{");
            promptBuilder.AppendLine("  \"headerRowIndex\": 1,");
            promptBuilder.AppendLine("  \"mapping\": {");
            promptBuilder.AppendLine("    \"A\": \"Ip\",");
            promptBuilder.AppendLine("    \"B\": \"MachineName\"");
            promptBuilder.AppendLine("  },");
            promptBuilder.AppendLine("  \"explanation\": \"Mô tả ngắn gọn lý do ánh xạ\"");
            promptBuilder.AppendLine("}");

            var requestBody = new
            {
                model = string.IsNullOrWhiteSpace(settings.ModelName) ? "default" : settings.ModelName.Trim(),
                messages = new object[]
                {
                    new { role = "system", content = "Bạn là hệ thống ánh xạ schema dữ liệu chính xác cao, luôn trả về JSON." },
                    new { role = "user", content = promptBuilder.ToString() }
                },
                temperature = settings.Temperature
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
                result.ErrorMessage = $"AI Server trả về mã lỗi {response.StatusCode}: {responseText}";
                return result;
            }

            string aiContent = ExtractContentFromChatCompletion(responseText);
            if (string.IsNullOrWhiteSpace(aiContent))
            {
                result.ErrorMessage = "AI Model không trả về nội dung.";
                return result;
            }

            ParseAiMappingJson(aiContent, result, targetProps);
        }
        catch (HttpRequestException ex)
        {
            result.ErrorMessage = $"Không thể kết nối đến AI Server nội bộ (BypassProxy: {settings.BypassProxy}): {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            result.ErrorMessage = $"Quá thời gian chờ phản hồi từ AI Server ({settings.TimeoutSeconds}s).";
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Lỗi khi gọi AI Schema Mapping: {ex.Message}";
        }

        return result;
    }

    public static string NormalizeChatCompletionsUrl(string baseUrl)
    {
        string trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }
        return $"{trimmed}/chat/completions";
    }

    public static string ExtractContentFromChatCompletion(string jsonResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    return content.GetString()?.Trim() ?? string.Empty;
                }
            }
        }
        catch
        {
            // Trả về nguyên văn nếu không parse được
        }

        return jsonResponse;
    }

    public static void ParseAiMappingJson(string aiContent, AiSchemaMappingResult result, Dictionary<string, string> validProperties)
    {
        try
        {
            // Bóc tách JSON nếu model bọc trong ```json ... ```
            string cleanJson = aiContent;
            var match = Regex.Match(aiContent, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                cleanJson = match.Groups[1].Value.Trim();
            }
            else
            {
                int firstBrace = aiContent.IndexOf('{');
                int lastBrace = aiContent.LastIndexOf('}');
                if (firstBrace >= 0 && lastBrace > firstBrace)
                {
                    cleanJson = aiContent.Substring(firstBrace, lastBrace - firstBrace + 1);
                }
            }

            using var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("headerRowIndex", out var headerElem) && headerElem.TryGetInt32(out int headerIdx))
            {
                result.HeaderRowIndex = headerIdx;
            }

            if (root.TryGetProperty("explanation", out var expElem))
            {
                result.Explanation = expElem.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("mapping", out var mappingElem) && mappingElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in mappingElem.EnumerateObject())
                {
                    string colKey = prop.Name.Trim().ToUpperInvariant();
                    string targetField = prop.Value.GetString()?.Trim() ?? string.Empty;

                    // Chuẩn hóa so sánh không phân biệt hoa thường với thuộc tính hợp lệ
                    var matchedProp = validProperties.Keys
                        .FirstOrDefault(k => string.Equals(k, targetField, StringComparison.OrdinalIgnoreCase));

                    if (matchedProp != null)
                    {
                        result.ColumnMapping[colKey] = matchedProp;
                    }
                }
            }

            result.Success = result.ColumnMapping.Count > 0;
            if (!result.Success && string.IsNullOrEmpty(result.ErrorMessage))
            {
                result.ErrorMessage = "AI không nhận diện được cột phù hợp nào.";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Không thể đọc kết quả JSON từ AI: {ex.Message}";
        }
    }

    private static readonly HashSet<string> ReservedOracleEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM", "SYSAUX", "USERS", "TEMP", "UNDOTBS1", "UNDOTBS2", "EXAMPLE", "DEFAULT", "SYS", "OUTLN"
    };

    public async Task<DatabaseSafetyAnalysisResult> AnalyzeCleanupSafetyAsync(
        AiSettings settings,
        List<ProjectEntry> projectsToDelete,
        List<ProjectEntry> allProjects,
        CancellationToken cancellationToken = default)
    {
        var result = new DatabaseSafetyAnalysisResult();
        var deletedIds = new HashSet<string>(projectsToDelete.Select(p => p.Id));
        var remainingProjects = allProjects.Where(p => !deletedIds.Contains(p.Id)).ToList();

        // 1. Phân tích nền tảng (Baseline Heuristics)
        var baselineWarnings = new List<string>();
        var baselineRecommendations = new List<string>();
        var highRiskDetected = false;

        foreach (var proj in projectsToDelete)
        {
            var users = ProjectCleanupService.ParseUserList(proj.DbUsers);
            var tablespaces = ProjectCleanupService.ParseTablespaceList(proj.Tablespace);

            // Kiểm tra đối tượng hệ thống Oracle nhạy cảm
            foreach (var u in users)
            {
                if (ReservedOracleEntities.Contains(u))
                {
                    highRiskDetected = true;
                    baselineWarnings.Add($"[NGUY HIỂM] Dự án '{proj.ProjectName}' khai báo User hệ thống: '{u}'. Tuyệt đối không được DROP user này!");
                }
            }

            foreach (var ts in tablespaces)
            {
                if (ReservedOracleEntities.Contains(ts))
                {
                    highRiskDetected = true;
                    baselineWarnings.Add($"[NGUY HIỂM] Dự án '{proj.ProjectName}' khai báo Tablespace hệ thống: '{ts}'. Việc xóa tablespace này sẽ phá hỏng Database!");
                }
            }

            // Kiểm tra xung đột với các dự án còn lại trong hệ thống
            foreach (var other in remainingProjects)
            {
                var otherUsers = ProjectCleanupService.ParseUserList(other.DbUsers);
                var otherTs = ProjectCleanupService.ParseTablespaceList(other.Tablespace);

                var commonUsers = users.Intersect(otherUsers, StringComparer.OrdinalIgnoreCase).ToList();
                if (commonUsers.Count > 0)
                {
                    string sameHost = string.Equals(proj.HostServer, other.HostServer, StringComparison.OrdinalIgnoreCase)
                        ? $"CÙNG HOST '{proj.HostServer}'"
                        : "Khác Host";
                    baselineWarnings.Add($"[CẢNH BÁO TRÙNG LẶP] User '{string.Join(", ", commonUsers)}' của dự án '{proj.ProjectName}' cũng được sử dụng bởi dự án đang hoạt động '{other.ProjectName}' ({sameHost}).");
                }

                var commonTs = tablespaces.Intersect(otherTs, StringComparer.OrdinalIgnoreCase).ToList();
                if (commonTs.Count > 0)
                {
                    string sameHost = string.Equals(proj.HostServer, other.HostServer, StringComparison.OrdinalIgnoreCase)
                        ? $"CÙNG HOST '{proj.HostServer}'"
                        : "Khác Host";
                    baselineWarnings.Add($"[CẢNH BÁO TRÙNG LẶP] Tablespace '{string.Join(", ", commonTs)}' của dự án '{proj.ProjectName}' cũng được sử dụng bởi dự án đang hoạt động '{other.ProjectName}' ({sameHost}).");
                }
            }
        }

        if (baselineWarnings.Count == 0)
        {
            baselineRecommendations.Add("Không phát hiện xung đột hay đối tượng hệ thống nhạy cảm.");
            baselineRecommendations.Add("Khuyến nghị luôn sao lưu (Export DDL / Data Pump) trước khi thực thi xóa.");
        }
        else
        {
            baselineRecommendations.Add("Xem xét kỹ danh sách đối tượng trùng lặp hoặc đối tượng hệ thống trước khi xóa.");
            baselineRecommendations.Add("Bắt buộc tích chọn 'Sao lưu Database' trước khi thực thi.");
        }

        result.Warnings = baselineWarnings;
        result.Recommendations = baselineRecommendations;
        result.RiskLevel = highRiskDetected ? DatabaseRiskLevel.High : (baselineWarnings.Count > 0 ? DatabaseRiskLevel.Medium : DatabaseRiskLevel.Low);
        result.Summary = result.RiskLevel == DatabaseRiskLevel.High
            ? "Mức độ rủi ro CAO: Phát hiện đối tượng hệ thống hoặc xung đột nghiêm trọng!"
            : (result.RiskLevel == DatabaseRiskLevel.Medium
                ? "Mức độ rủi ro TRUNG BÌNH: Có tài nguyên trùng với các dự án khác."
                : "Mức độ rủi ro THẤP: Có thể tiến hành dọn dẹp an toàn kèm sao lưu.");

        // Nếu AI chưa được kích hoạt hoặc không có URL, trả về kết quả phân tích nền tảng
        if (!settings.IsEnabled || string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            result.Success = true;
            return result;
        }

        // 2. Nâng cao chất lượng đánh giá bằng AI Model nội bộ
        try
        {
            using var client = CreateHttpClient(settings);
            string endpoint = NormalizeChatCompletionsUrl(settings.BaseUrl);

            var sb = new StringBuilder();
            sb.AppendLine("Bạn là chuyên gia an toàn cơ sở dữ liệu Oracle Database (Senior Oracle DBA Safety Advisor).");
            sb.AppendLine("Hãy phân tích các rủi ro có thể xảy ra khi người dùng dọn dẹp (xóa source code, drop user cascade và drop tablespace) cho danh sách dự án dưới đây.");
            sb.AppendLine();
            sb.AppendLine("DANH SÁCH DỰ ÁN DỰ KIẾN XÓA:");
            foreach (var p in projectsToDelete)
            {
                sb.AppendLine($"- Dự án: {p.ProjectName} (Host: {p.HostServer}:{p.Port}, Service: {p.ServiceName})");
                sb.AppendLine($"  Thư mục Source: {p.SourceFolder}");
                sb.AppendLine($"  DB Users: {p.DbUsers}");
                sb.AppendLine($"  Tablespaces: {p.Tablespace}");
            }
            sb.AppendLine();

            if (baselineWarnings.Count > 0)
            {
                sb.AppendLine("CÁC XUNG ĐỘT HỆ THỐNG ĐÃ PHÁT HIỆN:");
                foreach (var w in baselineWarnings)
                {
                    sb.AppendLine($"- {w}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("YÊU CẦU TRẢ VỀ:");
            sb.AppendLine("Trả về DUY NHẤT một JSON hợp lệ (không kèm văn bản ngoài JSON):");
            sb.AppendLine("{");
            sb.AppendLine("  \"riskLevel\": \"Low\" | \"Medium\" | \"High\",");
            sb.AppendLine("  \"summary\": \"Tóm tắt đánh giá an toàn ngắn gọn\",");
            sb.AppendLine("  \"warnings\": [\"Cảnh báo 1\", \"Cảnh báo 2\"],");
            sb.AppendLine("  \"recommendations\": [\"Khuyến nghị bảo vệ an toàn 1\", \"Khuyến nghị 2\"]");
            sb.AppendLine("}");

            var requestBody = new
            {
                model = string.IsNullOrWhiteSpace(settings.ModelName) ? "default" : settings.ModelName.Trim(),
                messages = new object[]
                {
                    new { role = "system", content = "Bạn là hệ thống cố vấn an toàn Database, luôn trả về JSON hợp lệ." },
                    new { role = "user", content = sb.ToString() }
                },
                temperature = 0.1
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            using var response = await client.PostAsync(endpoint, jsonContent, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                string aiContent = ExtractContentFromChatCompletion(responseText);
                ParseSafetyAnalysisJson(aiContent, result);
            }
        }
        catch (Exception ex)
        {
            // Nếu gọi AI thất bại (mạng, timeout), vẫn giữ kết quả baseline phân tích sẵn
            result.Summary += $" (Lưu ý: Không thể kết nối AI Model: {ex.Message})";
        }

        result.Success = true;
        return result;
    }

    public static void ParseSafetyAnalysisJson(string aiContent, DatabaseSafetyAnalysisResult result)
    {
        try
        {
            string cleanJson = aiContent;
            var match = Regex.Match(aiContent, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                cleanJson = match.Groups[1].Value.Trim();
            }
            else
            {
                int firstBrace = aiContent.IndexOf('{');
                int lastBrace = aiContent.LastIndexOf('}');
                if (firstBrace >= 0 && lastBrace > firstBrace)
                {
                    cleanJson = aiContent.Substring(firstBrace, lastBrace - firstBrace + 1);
                }
            }

            using var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("riskLevel", out var riskElem))
            {
                string rStr = riskElem.GetString() ?? string.Empty;
                if (Enum.TryParse<DatabaseRiskLevel>(rStr, ignoreCase: true, out var level))
                {
                    // Nếu baseline đã là High thì không hạ xuống Low/Medium
                    if (result.RiskLevel != DatabaseRiskLevel.High)
                    {
                        result.RiskLevel = level;
                    }
                }
            }

            if (root.TryGetProperty("summary", out var sumElem) && !string.IsNullOrWhiteSpace(sumElem.GetString()))
            {
                result.Summary = sumElem.GetString()!;
            }

            if (root.TryGetProperty("warnings", out var wElem) && wElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in wElem.EnumerateArray())
                {
                    string w = item.GetString()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(w) && !result.Warnings.Contains(w))
                    {
                        result.Warnings.Add(w);
                    }
                }
            }

            if (root.TryGetProperty("recommendations", out var rElem) && rElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rElem.EnumerateArray())
                {
                    string r = item.GetString()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(r) && !result.Recommendations.Contains(r))
                    {
                        result.Recommendations.Add(r);
                    }
                }
            }
        }
        catch
        {
            // Bỏ qua lỗi parse JSON AI và giữ baseline
        }
    }

    public async Task<OracleScriptGenerationResult> GenerateOracleScriptAsync(
        AiSettings settings,
        ProjectEntry project,
        string requestPrompt,
        CancellationToken cancellationToken = default)
    {
        var result = new OracleScriptGenerationResult();

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            result.ErrorMessage = "Chưa cấu hình URL Endpoint của AI Model nội bộ.";
            return result;
        }

        try
        {
            using var client = CreateHttpClient(settings);
            string endpoint = NormalizeChatCompletionsUrl(settings.BaseUrl);

            var sb = new StringBuilder();
            sb.AppendLine("Bạn là chuyên gia Oracle Database (Senior Oracle DBA).");
            sb.AppendLine("Nhiệm vụ: Viết mã Oracle SQL / PL*SQL / Tiện ích Data Pump (expdp) chính xác, chuyên nghiệp, có chú thích chi tiết dựa trên thông tin dự án.");
            sb.AppendLine();
            sb.AppendLine("THÔNG TIN DỰ ÁN:");
            sb.AppendLine($"- Tên dự án: {project.ProjectName}");
            sb.AppendLine($"- Host Server: {project.HostServer}:{project.Port}");
            sb.AppendLine($"- Service Name: {project.ServiceName}");
            sb.AppendLine($"- Tài khoản SYSDBA: {project.DbaUsername}");
            sb.AppendLine($"- DB Users / Schemas: {project.DbUsers}");
            sb.AppendLine($"- Tablespaces: {project.Tablespace}");
            sb.AppendLine();
            sb.AppendLine($"YÊU CẦU CỤ THỂ:");
            sb.AppendLine(requestPrompt);
            sb.AppendLine();
            sb.AppendLine("QUY TẮC:");
            sb.AppendLine("1. Sử dụng cú pháp Oracle Database chuẩn.");
            sb.AppendLine("2. Các câu lệnh thao tác nguy hiểm (DROP, KILL SESSION...) phải có chú thích an toàn rõ ràng.");
            sb.AppendLine("3. Đặt toàn bộ mã lệnh SQL/Script trong khối ```sql ... ```.");
            sb.AppendLine("4. Giải thích ngắn gọn mục đích và cách chạy script.");

            var requestBody = new
            {
                model = string.IsNullOrWhiteSpace(settings.ModelName) ? "default" : settings.ModelName.Trim(),
                messages = new object[]
                {
                    new { role = "system", content = "Bạn là trợ lý chuyên sâu về Oracle Database SQL & Administration." },
                    new { role = "user", content = sb.ToString() }
                },
                temperature = 0.2
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
                result.ErrorMessage = $"AI Server trả về mã lỗi {response.StatusCode}: {responseText}";
                return result;
            }

            string aiContent = ExtractContentFromChatCompletion(responseText);
            ExtractScriptAndExplanation(aiContent, result);
            result.Success = !string.IsNullOrWhiteSpace(result.Script);
            if (!result.Success && string.IsNullOrEmpty(result.ErrorMessage))
            {
                result.ErrorMessage = "AI không tạo được script nào.";
            }
        }
        catch (HttpRequestException ex)
        {
            result.ErrorMessage = $"Lỗi kết nối AI Server (BypassProxy: {settings.BypassProxy}): {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            result.ErrorMessage = $"Quá thời gian chờ phản hồi từ AI Server ({settings.TimeoutSeconds}s).";
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Lỗi khi sinh script: {ex.Message}";
        }

        return result;
    }

    public static void ExtractScriptAndExplanation(string aiContent, OracleScriptGenerationResult result)
    {
        var match = Regex.Match(aiContent, @"```(?:sql|plsql|bash|sh)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            result.Script = match.Groups[1].Value.Trim();
            // Phần text còn lại làm giải thích
            string explanation = aiContent.Replace(match.Value, string.Empty).Trim();
            result.Explanation = explanation;
        }
        else
        {
            result.Script = aiContent.Trim();
            result.Explanation = "Đã sinh script theo yêu cầu.";
        }
    }
}
