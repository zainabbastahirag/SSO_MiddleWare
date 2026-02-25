// ═══════════════════════════════════════════════════════════════════════════════
// Copy into your Blazor Server project (e.g. Services/IInvitationApiService.cs)
// This is the client-side service that calls your backend invitation + role APIs.
// ═══════════════════════════════════════════════════════════════════════════════

using System.Net.Http.Json;

namespace AGOne.UI.Services;

// ─── DTOs (match your backend models) ───

public class CreateInvitationDto
{
    public string Email { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public List<Guid>? RoleIds { get; set; }
    public List<InvitationProductRoleDto>? ProductRoles { get; set; }
    public string? PersonalMessage { get; set; }
    public int? ExpirationDays { get; set; }
}

public class InvitationProductRoleDto
{
    public Guid ProductId { get; set; }
    public Guid RoleId { get; set; }
}

public class InvitationResponse
{
    public bool Success { get; set; }
    public Guid? InvitationId { get; set; }
    public string? Token { get; set; }
    public string? Message { get; set; }
}

public class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ProductId { get; set; }
    public string? ProductName { get; set; }
    public bool IsSystemRole { get; set; }
    public int PermissionCount { get; set; }
}

// ─── Service Interface ───

public interface IInvitationApiService
{
    Task<InvitationResponse> CreateInvitationAsync(CreateInvitationDto dto);
    Task<List<RoleDto>> GetTenantRolesAsync();
}

// ─── Implementation ───

public class InvitationApiService : IInvitationApiService
{
    private readonly HttpClient _http;
    private readonly ILogger<InvitationApiService> _logger;

    public InvitationApiService(HttpClient http, ILogger<InvitationApiService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<InvitationResponse> CreateInvitationAsync(CreateInvitationDto dto)
    {
        var response = await _http.PostAsJsonAsync("api/invitations", dto);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<InvitationResponse>()
                ?? new InvitationResponse { Success = false, Message = "Empty response" };
        }

        var error = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Invitation API failed: {Status} {Error}", response.StatusCode, error);

        try
        {
            return await response.Content.ReadFromJsonAsync<InvitationResponse>()
                ?? new InvitationResponse { Success = false, Message = $"HTTP {response.StatusCode}" };
        }
        catch
        {
            return new InvitationResponse { Success = false, Message = $"HTTP {response.StatusCode}: {error}" };
        }
    }

    public async Task<List<RoleDto>> GetTenantRolesAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<RoleDto>>("api/roles") ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load roles");
            return new();
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Register in Program.cs:
//   builder.Services.AddScoped<IInvitationApiService, InvitationApiService>();
//
// The HttpClient should be your backend API client (same origin for Blazor Server).
// If you're using a named HttpClient, inject IHttpClientFactory instead and create it.
// ═══════════════════════════════════════════════════════════════════════════════
