// Drop this method into your service class, replacing the existing EnsureValidTokenAsync.
// Requires: _configuration, _httpClientFactory, _db, _tokenService, _logger as fields.

private async Task<string?> EnsureValidTokenAsync(UserToken token)
{
    if (token.AccessTokenExpiresUtc > DateTime.UtcNow.AddMinutes(5))
    {
        return token.AccessToken;
    }

    if (string.IsNullOrEmpty(token.RefreshToken))
    {
        _logger.LogWarning("No refresh token available for user {UserId}", token.UserId);
        return null;
    }

    if (token.RefreshTokenExpiresUtc.HasValue && token.RefreshTokenExpiresUtc < DateTime.UtcNow)
    {
        _logger.LogWarning("Refresh token expired for user {UserId}", token.UserId);
        return null;
    }

    var ssoConfig = _configuration.GetSection("AgOneSso");
    var instance = ssoConfig["Instance"]?.TrimEnd('/');
    var tenantId = ssoConfig["TenantId"];
    var clientId = ssoConfig["ClientId"];
    var clientSecret = ssoConfig["ClientSecret"];

    if (string.IsNullOrEmpty(instance) || string.IsNullOrEmpty(tenantId) ||
        string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
    {
        _logger.LogError("AgOneSso configuration incomplete — cannot refresh token");
        return null;
    }

    var tokenEndpoint = $"{instance}/{tenantId}/oauth2/v2.0/token";

    try
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = token.RefreshToken
        };

        if (!string.IsNullOrEmpty(token.GrantedScopes))
        {
            form["scope"] = token.GrantedScopes;
        }

        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Token refresh failed ({Status}) for user {UserId}: {Body}",
                response.StatusCode, token.UserId, body);
            return null;
        }

        var entraResponse = JsonSerializer.Deserialize<EntraTokenResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (entraResponse == null || string.IsNullOrEmpty(entraResponse.AccessToken))
        {
            _logger.LogError("Token refresh returned empty access token for user {UserId}", token.UserId);
            return null;
        }

        // Re-issue our own application JWT using the refreshed identity
        if (!string.IsNullOrWhiteSpace(token.UserId) &&
            Guid.TryParse(token.UserId, out var userId))
        {
            var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId);

            if (user != null)
            {
                entraResponse.AccessToken = _tokenService.GenerateJwtToken(user);
            }
            else
            {
                _logger.LogWarning("User {UserId} not found in DB during token refresh", userId);
            }
        }

        // Persist refreshed tokens
        token.AccessToken = entraResponse.AccessToken;
        token.RefreshToken = entraResponse.RefreshToken ?? token.RefreshToken;
        token.AccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(entraResponse.ExpiresIn > 0 ? entraResponse.ExpiresIn : 3600);
        token.UpdatedUtc = DateTime.UtcNow;
        token.RefreshCount++;

        _db.UserTokens.Update(token);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Token refreshed successfully for user {UserId}", token.UserId);

        return token.AccessToken;
    }
    catch (HttpRequestException ex)
    {
        _logger.LogError(ex, "Network error while refreshing token for user {UserId}", token.UserId);
        return null;
    }
    catch (JsonException ex)
    {
        _logger.LogError(ex, "Failed to parse token refresh response for user {UserId}", token.UserId);
        return null;
    }
    catch (DbUpdateException ex)
    {
        _logger.LogError(ex, "Failed to save refreshed token to DB for user {UserId}", token.UserId);
        return null;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error refreshing token for user {UserId}", token.UserId);
        return null;
    }
}
