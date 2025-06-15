namespace TerminalManagement.Shared.Models;

public class TerminalRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string RequestData { get; set; } = string.Empty;
    public string RequestType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string SourcePod { get; set; } = string.Empty;
}

public class TerminalResponse
{
    public string RequestId { get; set; } = string.Empty;
    public string ResponseData { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; }
}

public class TerminalInfo
{
    public string TerminalId { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int Branch { get; set; }
    public string? SessionId { get; set; }
    public DateTime? LastUsed { get; set; }
    public bool IsAvailable { get; set; } = true;
}

public class TerminalConfiguration
{
    public string PodName { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string Scheme { get; set; } = "http";
    public int SessionTimeoutSeconds { get; set; } = 300;
    public string RedisConnectionString { get; set; } = string.Empty;
    public List<string> TerminalsData { get; set; } = new();
}
