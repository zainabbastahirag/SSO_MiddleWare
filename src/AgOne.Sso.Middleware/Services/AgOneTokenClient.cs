using System.Net.Http.Json;
using System.Text.Json;
using AgOne.Sso.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgOne.Sso.Services;

/// <summary>
/// HTTP client that calls AG ONE's central gateway to validate/refresh tokens.
/// Registered as a typed HttpClient via IHttpClientFactory for proper connection pooling.
/// </summary>
public class AgOneTokenClient : IAgOneTokenClient
{
    private readonly HttpClient _httpClient;
    private readonly AgOneSsoOptions _options;
    private readonly ILogger<AgOneTokenClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AgOneTokenClient(
        HttpClient httpClient,
        IOptions<AgOneSsoOptions> options,
        ILogger<AgOneTokenClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        // Set the base address from options if not already configured
        if (_httpClient.BaseAddress == null && !string.IsNullOrEmpty(_options.AgOneBaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_options.AgOneBaseUrl.TrimEnd('/') + "/");
        }
    }

    /// <inheritdoc />
    public async Task<string?> ValidateAndRefreshTokenAsync(string currentToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ExternalTokenValidationRequest
            {
                Token = currentToken
            };

            var endpoint = _options.TokenValidateEndpoint.TrimStart('/');

            _logger.LogDebug("Calling AG ONE token validation endpoint: {Endpoint}", endpoint);

            var response = await _httpClient.PostAsJsonAsync(endpoint, request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "AG ONE token validation returned HTTP {StatusCode}",
                    response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // The AG ONE API returns either:
            // Success: { "token": "..." }
            // Failure: { "isValid": false, "message": "..." }
            var result = JsonSerializer.Deserialize<TokenValidationResponse>(body, JsonOptions);

            if (result == null)
            {
                _logger.LogWarning("AG ONE token validation returned null response");
                return null;
            }

            // Check if we got a token back (success path)
            if (!string.IsNullOrEmpty(result.Token))
            {
                _logger.LogDebug("AG ONE returned a valid/refreshed token");
                return result.Token;
            }

            // Failure path
            if (result.IsValid == false)
            {
                _logger.LogInformation(
                    "AG ONE token validation failed: {Message}",
                    result.Message ?? "Unknown reason");
                return null;
            }

            _logger.LogWarning("AG ONE token validation returned unexpected response: {Body}", body);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error calling AG ONE token validation endpoint");
            return null;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Timeout calling AG ONE token validation endpoint");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling AG ONE token validation endpoint");
            return null;
        }
    }
}
