using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WorkManagement.Models;
using WorkManagement.Services;
using Xunit;

namespace WorkManagement.Tests;

public class AiDevOpsCommandServiceTests
{
    [Fact]
    public void ParseAiCommandJson_ValidJson_MapsActionAndProperties()
    {
        string rawJson = @"{
            ""action"": ""ConnectRdp"",
            ""target"": ""172.16.6.16"",
            ""title"": ""Kết nối RDP Máy Ảo Test"",
            ""explanation"": ""Mở kết nối tới server 172.16.6.16 theo yêu cầu.""
        }";

        var result = AiDevOpsCommandService.ParseAiCommandJson(rawJson, "kết nối máy test");

        Assert.True(result.Success);
        Assert.Equal(AiDevOpsActionType.ConnectRdp, result.ActionType);
        Assert.Equal("172.16.6.16", result.TargetParam);
        Assert.Equal("Kết nối RDP Máy Ảo Test", result.DisplayTitle);
        Assert.Equal("Mở kết nối tới server 172.16.6.16 theo yêu cầu.", result.DisplayExplanation);
    }

    [Fact]
    public void ParseAiCommandJson_MarkdownFencedJson_ExtractsSuccessfully()
    {
        string rawAiResponse = @"Dưới đây là hành động được AI lựa chọn:
```json
{
    ""action"": ""OpenOracleScript"",
    ""target"": ""DMS BUTSURYU"",
    ""title"": ""Mở Trợ lý sinh Script Oracle"",
    ""explanation"": ""Dự án DMS BUTSURYU cần tạo script dọn dẹp schema.""
}
```
Chúc bạn thực hiện thành công.";

        var result = AiDevOpsCommandService.ParseAiCommandJson(rawAiResponse, "tạo script dọn db cho dự án DMS");

        Assert.True(result.Success);
        Assert.Equal(AiDevOpsActionType.OpenOracleScript, result.ActionType);
        Assert.Equal("DMS BUTSURYU", result.TargetParam);
        Assert.Equal("Mở Trợ lý sinh Script Oracle", result.DisplayTitle);
    }

    [Fact]
    public void ParseAiCommandJson_InvalidJson_FallsBackToDirectAnswer()
    {
        string plainText = "Để xóa tablespace trong Oracle, bạn có thể chạy: DROP TABLESPACE SWG_TS INCLUDING CONTENTS AND DATAFILES;";

        var result = AiDevOpsCommandService.ParseAiCommandJson(plainText, "cách xóa tablespace");

        Assert.True(result.Success);
        Assert.Equal(AiDevOpsActionType.DirectAnswer, result.ActionType);
        Assert.Equal("AI DevOps Copilot Tư Vấn", result.DisplayTitle);
        Assert.Equal(plainText, result.DisplayExplanation);
        Assert.Equal(plainText, result.TargetParam);
    }

    [Fact]
    public void LocalHeuristicDispatch_ScriptKeywords_ReturnsOpenOracleScript()
    {
        var service = new AiDevOpsCommandService();
        var result = service.LocalHeuristicDispatch("hãy sinh script oracle dọn dẹp tablespace");

        Assert.Equal(AiDevOpsActionType.OpenOracleScript, result.ActionType);
        Assert.Contains("Oracle", result.DisplayTitle);
    }

    [Fact]
    public void LocalHeuristicDispatch_NavigationKeywords_ReturnsNavigateView()
    {
        var service = new AiDevOpsCommandService();

        var resVm = service.LocalHeuristicDispatch("mở trang máy ảo");
        Assert.Equal(AiDevOpsActionType.NavigateView, resVm.ActionType);
        Assert.Equal("VmManager", resVm.TargetParam);

        var resProj = service.LocalHeuristicDispatch("xem danh sách dự án");
        Assert.Equal(AiDevOpsActionType.NavigateView, resProj.ActionType);
        Assert.Equal("ProjectManager", resProj.TargetParam);

        var resFtp = service.LocalHeuristicDispatch("dọn dẹp file ftp");
        Assert.Equal(AiDevOpsActionType.NavigateView, resFtp.ActionType);
        Assert.Equal("FtpCleaner", resFtp.TargetParam);

        var resSched = service.LocalHeuristicDispatch("cấu hình lịch tự động");
        Assert.Equal(AiDevOpsActionType.NavigateView, resSched.ActionType);
        Assert.Equal("TaskScheduler", resSched.TargetParam);

        var resEmail = service.LocalHeuristicDispatch("cấu hình mail smtp");
        Assert.Equal(AiDevOpsActionType.NavigateView, resEmail.ActionType);
        Assert.Equal("EmailConfig", resEmail.TargetParam);

        var resAi = service.LocalHeuristicDispatch("cài đặt ai model endpoint");
        Assert.Equal(AiDevOpsActionType.NavigateView, resAi.ActionType);
        Assert.Equal("AiConfig", resAi.TargetParam);
    }

    [Fact]
    public void LocalHeuristicDispatch_DefaultQuery_ReturnsFilterVms()
    {
        var service = new AiDevOpsCommandService();
        var result = service.LocalHeuristicDispatch("Server Windows 2019");

        Assert.Equal(AiDevOpsActionType.FilterVms, result.ActionType);
        Assert.Equal("Server Windows 2019", result.TargetParam);
    }

    [Fact]
    public async Task DispatchCommandAsync_EmptyQuery_ReturnsFailure()
    {
        var service = new AiDevOpsCommandService();
        var result = await service.DispatchCommandAsync("   ");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }
}
