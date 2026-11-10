using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WorkManagement.Models;
using WorkManagement.Services;
using Xunit;

namespace WorkManagement.Tests;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handlerFunc;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc)
    {
        _handlerFunc = handlerFunc;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handlerFunc(request));
    }
}

public class AiSchemaMappingTests
{
    [Fact]
    public void NormalizeChatCompletionsUrl_HandlesTrailingSlashesAndSubpaths()
    {
        Assert.Equal("http://ai.internal:8000/v1/chat/completions",
            OpenAiClientService.NormalizeChatCompletionsUrl("http://ai.internal:8000/v1"));

        Assert.Equal("http://ai.internal:8000/v1/chat/completions",
            OpenAiClientService.NormalizeChatCompletionsUrl("http://ai.internal:8000/v1/"));

        Assert.Equal("http://ai.internal:8000/v1/chat/completions",
            OpenAiClientService.NormalizeChatCompletionsUrl("http://ai.internal:8000/v1/chat/completions"));
    }

    [Fact]
    public void ParseAiMappingJson_ExtractsPropertiesFromMarkdownFencedJson()
    {
        string rawAiResponse = @"Dưới đây là kết quả phân tích ánh xạ cột:
```json
{
  ""headerRowIndex"": 1,
  ""mapping"": {
    ""A"": ""No"",
    ""B"": ""Ip"",
    ""C"": ""MachineName"",
    ""D"": ""Tester"",
    ""E"": ""ExpirationDate""
  },
  ""explanation"": ""Ánh xạ dựa trên tiêu đề tiếng Việt và định dạng IP""
}
```
Chúc bạn làm việc hiệu quả!";

        var validProps = new Dictionary<string, string>
        {
            { "No", "STT" },
            { "Ip", "IP Address" },
            { "MachineName", "Tên máy" },
            { "Tester", "Người kiểm tra" },
            { "ExpirationDate", "Ngày hết hạn" }
        };

        var result = new AiSchemaMappingResult();
        OpenAiClientService.ParseAiMappingJson(rawAiResponse, result, validProps);

        Assert.True(result.Success);
        Assert.Equal(1, result.HeaderRowIndex);
        Assert.Equal(5, result.ColumnMapping.Count);
        Assert.Equal("Ip", result.ColumnMapping["B"]);
        Assert.Equal("MachineName", result.ColumnMapping["C"]);
        Assert.Equal("Tester", result.ColumnMapping["D"]);
        Assert.Contains("tiêu đề tiếng Việt", result.Explanation);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsSuccess_WhenModelResponds200()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var response = new
            {
                id = "chatcmpl-test",
                choices = new[]
                {
                    new
                    {
                        message = new { role = "assistant", content = "OK" }
                    }
                }
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var service = new OpenAiClientService(mockHandler);
        var settings = new AiSettings
        {
            BaseUrl = "http://ai.internal:8000/v1",
            ModelName = "test-model",
            BypassProxy = true
        };

        var (success, message) = await service.TestConnectionAsync(settings);

        Assert.True(success);
        Assert.Contains("Kết nối AI thành công", message);
        Assert.Contains("OK", message);
    }

    [Fact]
    public async Task InferVmSchemaMappingAsync_CallsEndpoint_AndParsesResponse()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var response = new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "{\"headerRowIndex\":1,\"mapping\":{\"A\":\"No\",\"B\":\"Ip\",\"C\":\"MachineName\",\"D\":\"Tester\"},\"explanation\":\"Nhận diện cột thành công\"}"
                        }
                    }
                }
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json")
            };
        });

        var service = new OpenAiClientService(mockHandler);
        var settings = new AiSettings
        {
            BaseUrl = "http://ai.internal:8000/v1",
            BypassProxy = true
        };

        var headers = new Dictionary<string, string>
        {
            { "A", "Số TT" },
            { "B", "Địa chỉ IP máy chủ" },
            { "C", "Tên máy ảo" },
            { "D", "Kỹ sư kiểm thử" }
        };

        var sampleRows = new List<Dictionary<string, string>>
        {
            new() { { "A", "1" }, { "B", "192.168.1.100" }, { "C", "SVR-APP-01" }, { "D", "Nguyễn Văn A" } }
        };

        var result = await service.InferVmSchemaMappingAsync(settings, headers, sampleRows);

        Assert.True(result.Success);
        Assert.Equal(1, result.HeaderRowIndex);
        Assert.Equal("Ip", result.ColumnMapping["B"]);
        Assert.Equal("MachineName", result.ColumnMapping["C"]);
        Assert.Equal("Tester", result.ColumnMapping["D"]);
    }

    [Fact]
    public void ExtractVmEntries_AppliesColumnMapping_Correctly()
    {
        var rawRows = new List<IDictionary<string, object>>
        {
            new Dictionary<string, object> { { "A", "DANH SÁCH MÁY CHỦ" } },
            new Dictionary<string, object>
            {
                { "A", "STT" },
                { "B", "IP Máy" },
                { "C", "Tên Server" },
                { "D", "Tester" },
                { "E", "Hạn Sử Dụng" }
            },
            new Dictionary<string, object>
            {
                { "A", "1" },
                { "B", "10.20.30.40" },
                { "C", "VM-WEB-TEST" },
                { "D", "Tran Van B" },
                { "E", "31/12/2026" }
            }
        };

        var mapping = new Dictionary<string, string>
        {
            { "A", "No" },
            { "B", "Ip" },
            { "C", "MachineName" },
            { "D", "Tester" },
            { "E", "ExpirationDate" }
        };

        var entries = ExcelImportService.ExtractVmEntries(rawRows, headerRowIndex: 1, mapping);

        Assert.Single(entries);
        var entry = entries[0];
        Assert.Equal("1", entry.No);
        Assert.Equal("10.20.30.40", entry.Ip);
        Assert.Equal("VM-WEB-TEST", entry.MachineName);
        Assert.Equal("Tran Van B", entry.Tester);
        Assert.Equal("31/12/2026", entry.ExpirationDate);
    }

    [Fact]
    public void ExtractProjectEntries_AppliesColumnMapping_Correctly()
    {
        var rawRows = new List<IDictionary<string, object>>
        {
            new Dictionary<string, object> { { "A", "DỰ ÁN CÔNG TY" } },
            new Dictionary<string, object> { { "A", "BÁO CÁO 2026" } },
            new Dictionary<string, object>
            {
                { "A", "No" },
                { "B", "Tên Dự Án" },
                { "C", "Thư Mục Mã Nguồn" },
                { "D", "Database Host" },
                { "E", "PIC" }
            },
            new Dictionary<string, object>
            {
                { "A", "1" },
                { "B", "WorkManagement" },
                { "C", @"D:\Sources\WorkManagement" },
                { "D", "192.168.10.55" },
                { "E", "Nguyen Van C" }
            }
        };

        var mapping = new Dictionary<string, string>
        {
            { "A", "No" },
            { "B", "ProjectName" },
            { "C", "SourceFolder" },
            { "D", "HostServer" },
            { "E", "PersonInCharge" }
        };

        var entries = ProjectExcelService.ExtractProjectEntries(rawRows, titleRowIndex: 2, mapping);

        Assert.Single(entries);
        var p = entries[0];
        Assert.Equal("1", p.No);
        Assert.Equal("WorkManagement", p.ProjectName);
        Assert.Equal(@"D:\Sources\WorkManagement", p.SourceFolder);
        Assert.Equal("192.168.10.55", p.HostServer);
        Assert.Equal("Nguyen Van C", p.PersonInCharge);
    }

    [Fact]
    public void ConfigService_AiSettings_EncryptsApiKey_WithDPAPI()
    {
        string tempConfig = Path.Combine(Path.GetTempPath(), $"ai_config_{Guid.NewGuid()}.json");
        try
        {
            var configService = new ConfigService();
            var settings = new FtpSettings
            {
                Ai = new AiSettings
                {
                    IsEnabled = true,
                    BaseUrl = "http://ai.internal:8000/v1",
                    ApiKey = "my-secret-internal-api-key",
                    ModelName = "qwen2.5-coder-32b",
                    BypassProxy = true
                }
            };

            configService.Save(settings, tempConfig);

            // Xác minh file JSON lưu xuống đĩa chứa chuỗi mã hóa DPAPI
            string rawJson = File.ReadAllText(tempConfig);
            Assert.DoesNotContain("my-secret-internal-api-key", rawJson);
            Assert.Contains("ENC:DPAPI:", rawJson);

            // Đọc lại file config bằng ConfigService
            var loaded = configService.Load(tempConfig);
            Assert.NotNull(loaded.Ai);
            Assert.True(loaded.Ai.IsEnabled);
            Assert.Equal("http://ai.internal:8000/v1", loaded.Ai.BaseUrl);
            Assert.Equal("qwen2.5-coder-32b", loaded.Ai.ModelName);
            Assert.True(loaded.Ai.BypassProxy);
            Assert.Equal("my-secret-internal-api-key", loaded.Ai.ApiKey);
        }
        finally
        {
            if (File.Exists(tempConfig))
            {
                File.Delete(tempConfig);
            }
        }
    }
}
