// ═══════════════════════════════════════════════════════════════════════════════
// FIXED B2CAuthenticationService — reads all SSO settings from IConfiguration
// using Configuration.GetSection("AgOneSso") instead of IOptions<EntraIdSettings>.
//
// CHANGES NEEDED IN YOUR EXISTING B2CAuthenticationService CLASS:
//
//   1. Replace the IOptions<EntraIdSettings> constructor parameter with
//      IConfiguration.
//
//   2. Replace the ExchangeCodeForTokensAsync method body with the one below.
//
//   Everything else (ExtractUserFromIdToken, UserExistsAsync, _pkceStorage,
//   StateData, Base64UrlDecode, etc.) stays the same.
// ═══════════════════════════════════════════════════════════════════════════════

// ─── CONSTRUCTOR CHANGE ──────────────────────────────────────────────────────
//
// BEFORE:
//   private readonly EntraIdSettings _settings;
//
//   public B2CAuthenticationService(
//       HttpClient httpClient,
//       IOptions<EntraIdSettings> settings,
//       IHttpContextAccessor httpContextAccessor,
//       ILogger<B2CAuthenticationService> logger)
//   {
//       _httpClient = httpClient;
//       _settings = settings.Value;
//       ...
//   }
//
// AFTER:
//   private readonly IConfiguration _configuration;
//
//   public B2CAuthenticationService(
//       HttpClient httpClient,
//       IConfiguration configuration,
//       IHttpContextAccessor httpContextAccessor,
//       ILogger<B2CAuthenticationService> logger)
//   {
//       _httpClient = httpClient;
//       _configuration = configuration;
//       ...
//   }
// ─────────────────────────────────────────────────────────────────────────────

// ─── REPLACEMENT METHOD ──────────────────────────────────────────────────────

public async Task<SSOAuthResult> ExchangeCodeForTokensAsync(string code, string? state = null)
{
    try
    {
        var ssoConfig    = _configuration.GetSection("AgOneSso");
        var instance     = ssoConfig["Instance"]?.TrimEnd('/') ?? "";
        var tenantId     = ssoConfig["TenantId"] ?? "";
        var clientId     = ssoConfig["ClientId"] ?? "";
        var clientSecret = ssoConfig["ClientSecret"] ?? "";
        var callbackPath = ssoConfig["CallbackPath"] ?? "/api/auth/sso/callback";
        var scopes       = ssoConfig.GetSection("Scopes").Get<string[]>()
                           ?? new[] { "openid", "profile", "email", "offline_access" };

        if (string.IsNullOrEmpty(instance) || string.IsNullOrEmpty(tenantId) ||
            string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            _logger.LogError(
                "AgOneSso settings incomplete. Instance={Instance}, TenantId={TenantId}, " +
                "ClientId={ClientId}, HasSecret={HasSecret}",
                instance, tenantId, clientId, !string.IsNullOrEmpty(clientSecret));

            return new SSOAuthResult
            {
                Success = false,
                Message = "SSO configuration is incomplete. Check AgOneSso section in appsettings."
            };
        }

        var scopeString = string.Join(" ", scopes);

        var http = _httpContextAccessor.HttpContext!;
        var redirectUri = $"{http.Request.Scheme}://{http.Request.Host}{callbackPath}";

        var tokenEndpoint = $"{instance}/{tenantId}/oauth2/v2.0/token";

        _logger.LogInformation("TOKEN EXCHANGE STARTED");
        _logger.LogInformation("Token Endpoint: {Endpoint}", tokenEndpoint);
        _logger.LogInformation("Redirect URI:   {RedirectUri}", redirectUri);
        _logger.LogInformation("ClientId:        {ClientId}", clientId);
        _logger.LogInformation("Scopes:          {Scopes}", scopeString);

        // ─── Extract PKCE code_verifier and metadata from state ───────────
        string? codeVerifier = null;
        string? returnUrl = "/";
        string? role = null;
        string? productId = null;

        if (!string.IsNullOrEmpty(state))
        {
            if (_pkceStorage.TryGetValue(state, out var pkceData))
            {
                codeVerifier = pkceData.CodeVerifier;
                _pkceStorage.Remove(state);
                _logger.LogInformation("CodeVerifier retrieved from PKCE storage");
            }

            try
            {
                var stateJson = Encoding.UTF8.GetString(Base64UrlDecode(state));
                var stateData = JsonSerializer.Deserialize<StateData>(stateJson);

                if (stateData != null)
                {
                    if (string.IsNullOrEmpty(codeVerifier))
                    {
                        codeVerifier = stateData.CodeVerifier;
                        _logger.LogInformation("CodeVerifier extracted from state payload");
                    }
                    returnUrl = stateData.ReturnUrl ?? "/";
                    role = stateData.Role;
                    productId = stateData.ProductId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not parse state: {Error}", ex.Message);
            }
        }

        if (string.IsNullOrEmpty(codeVerifier))
        {
            _logger.LogError("No code_verifier found — PKCE validation will fail at the token endpoint");
            return new SSOAuthResult
            {
                Success = false,
                Message = "PKCE code_verifier not found. Please try logging in again."
            };
        }

        _logger.LogInformation("CodeVerifier length: {Len}", codeVerifier.Length);

        // ─── Build token request ──────────────────────────────────────────
        var requestBody = new Dictionary<string, string>
        {
            ["grant_type"]    = "authorization_code",
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret,
            ["code"]          = code,
            ["redirect_uri"]  = redirectUri,
            ["scope"]         = scopeString,
            ["code_verifier"] = codeVerifier
        };

        var content = new FormUrlEncodedContent(requestBody);
        var response = await _httpClient.PostAsync(tokenEndpoint, content);
        var responseContent = await response.Content.ReadAsStringAsync();

        _logger.LogInformation("Token endpoint responded with {Status}", response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("TOKEN EXCHANGE FAILED — {Status}", response.StatusCode);
            _logger.LogError("Response body: {Body}", responseContent);

            var errorMessage = "Failed to exchange authorization code for tokens";
            try
            {
                var errorData = JsonSerializer.Deserialize<JsonElement>(responseContent);
                if (errorData.TryGetProperty("error", out var errorProp))
                {
                    var errorCode = errorProp.GetString();
                    var errorDesc = errorData.TryGetProperty("error_description", out var descProp)
                        ? descProp.GetString()
                        : "";
                    errorMessage = $"{errorCode}: {errorDesc}";
                }
            }
            catch
            {
                errorMessage = responseContent;
            }

            return new SSOAuthResult
            {
                Success = false,
                Message = errorMessage
            };
        }

        // ─── Parse tokens ─────────────────────────────────────────────────
        _logger.LogInformation("TOKEN EXCHANGE SUCCESSFUL");

        var tokenData = JsonSerializer.Deserialize<JsonElement>(responseContent);

        var tokens = new B2CTokenResponse
        {
            AccessToken = tokenData.TryGetProperty("access_token", out var accessToken)
                ? accessToken.GetString() ?? ""
                : "",
            IdToken = tokenData.TryGetProperty("id_token", out var idToken)
                ? idToken.GetString() ?? ""
                : "",
            RefreshToken = tokenData.TryGetProperty("refresh_token", out var refreshToken)
                ? refreshToken.GetString() ?? ""
                : "",
            TokenType = tokenData.TryGetProperty("token_type", out var tokenType)
                ? tokenType.GetString() ?? "Bearer"
                : "Bearer",
            ExpiresIn = tokenData.TryGetProperty("expires_in", out var expiresIn)
                ? expiresIn.GetInt32()
                : 3600,
        };
        tokens.ExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn);

        if (string.IsNullOrEmpty(tokens.IdToken))
        {
            _logger.LogError("No ID token in the response");
            return new SSOAuthResult
            {
                Success = false,
                Message = "No ID token received from authentication provider"
            };
        }

        var userInfo = ExtractUserFromIdToken(tokens.IdToken);

        if (userInfo == null)
        {
            _logger.LogError("Failed to extract user info from ID token");
            return new SSOAuthResult
            {
                Success = false,
                Message = "Failed to extract user information from token"
            };
        }

        _logger.LogInformation("User authenticated: {Email}, ObjectId: {Oid}", userInfo.Email, userInfo.ObjectId);

        var isNewUser = !await UserExistsAsync(userInfo.ObjectId, userInfo.Email);
        var defaultRole = role ?? (isNewUser ? "TenantAdmin" : null);

        _logger.LogInformation("IsNewUser: {IsNew}, ProductId: {ProductId}",
            isNewUser, productId ?? "(none)");

        return new SSOAuthResult
        {
            Success = true,
            User = userInfo,
            Tokens = tokens,
            IsNewUser = isNewUser,
            DefaultRole = defaultRole,
            ProductId = productId
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Exception during token exchange: {Message}", ex.Message);
        return new SSOAuthResult
        {
            Success = false,
            Message = $"An error occurred during authentication: {ex.Message}"
        };
    }
}
