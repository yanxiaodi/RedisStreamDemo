using System.Collections.Concurrent;
using TerminalManagement.Shared.Models;

namespace TerminalManagement.Shared.Services;

public interface ITerminalPool
{
    Task<TerminalInfo?> AcquireTerminalAsync(CancellationToken cancellationToken = default);
    Task ReleaseTerminalAsync(TerminalInfo terminal);
    Task<bool> ValidateSessionAsync(TerminalInfo terminal);
    Task<string> CreateSessionAsync(TerminalInfo terminal);
    int AvailableCount { get; }
}

public class TerminalPool : ITerminalPool
{
    private readonly ConcurrentQueue<TerminalInfo> _availableTerminals;
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger<TerminalPool> _logger;
    private readonly TerminalConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public TerminalPool(
        IEnumerable<TerminalInfo> terminals, 
        ILogger<TerminalPool> logger,
        TerminalConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _availableTerminals = new ConcurrentQueue<TerminalInfo>(terminals);
        _semaphore = new SemaphoreSlim(terminals.Count(), terminals.Count());
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    public int AvailableCount => _availableTerminals.Count;

    public async Task<TerminalInfo?> AcquireTerminalAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            
            if (_availableTerminals.TryDequeue(out var terminal))
            {
                _logger.LogDebug("Acquired terminal {TerminalId}", terminal.TerminalId);
                return terminal;
            }
            
            // Release semaphore if we couldn't get a terminal
            _semaphore.Release();
            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Terminal acquisition cancelled");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring terminal");
            _semaphore.Release();
            throw;
        }
    }

    public async Task ReleaseTerminalAsync(TerminalInfo terminal)
    {
        try
        {
            terminal.LastUsed = DateTime.UtcNow;
            terminal.IsAvailable = true;
            
            _availableTerminals.Enqueue(terminal);
            _semaphore.Release();
            
            _logger.LogDebug("Released terminal {TerminalId}", terminal.TerminalId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing terminal {TerminalId}", terminal.TerminalId);
            throw;
        }
    }

    public async Task<bool> ValidateSessionAsync(TerminalInfo terminal)
    {
        try
        {
            if (string.IsNullOrEmpty(terminal.SessionId))
            {
                return false;
            }

            if (terminal.LastUsed.HasValue && 
                DateTime.UtcNow - terminal.LastUsed.Value > TimeSpan.FromSeconds(_configuration.SessionTimeoutSeconds))
            {
                _logger.LogDebug("Session expired for terminal {TerminalId}", terminal.TerminalId);
                terminal.SessionId = null;
                return false;
            }

            // Additional validation logic can be added here
            // For now, we'll assume the session is valid if it exists and hasn't expired
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating session for terminal {TerminalId}", terminal.TerminalId);
            return false;
        }
    }

    public async Task<string> CreateSessionAsync(TerminalInfo terminal)
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient();
            
            // Simulate session creation - replace with actual terminal API call
            var sessionId = Guid.NewGuid().ToString("N");
            
            // Here you would make the actual HTTP call to the terminal system
            // var response = await httpClient.PostAsync($"{_configuration.Scheme}://{terminal.Host}:{terminal.Port}/login", ...);
            
            terminal.SessionId = sessionId;
            terminal.LastUsed = DateTime.UtcNow;
            
            _logger.LogDebug("Created new session {SessionId} for terminal {TerminalId}", 
                sessionId, terminal.TerminalId);
            
            return sessionId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating session for terminal {TerminalId}", terminal.TerminalId);
            throw;
        }
    }
}
