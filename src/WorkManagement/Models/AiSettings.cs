namespace WorkManagement.Models;

public class AiSettings
{
    public bool IsEnabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Bỏ qua toàn bộ Proxy của công ty / System Proxy để kết nối trực tiếp trong mạng Intranet.
    /// Mặc định luôn là true theo yêu cầu bảo mật và định tuyến nội bộ.
    /// </summary>
    public bool BypassProxy { get; set; } = true;

    public double Temperature { get; set; } = 0.1;
}
