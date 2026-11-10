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
}
