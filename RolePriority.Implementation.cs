// ═══════════════════════════════════════════════════════════════════════════════
// ROLE PRIORITY SYSTEM — Full Implementation Guide
//
// This file contains ALL the pieces needed. Copy each section into the
// corresponding file in your project.
// ═══════════════════════════════════════════════════════════════════════════════


// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 1: ENTITY CHANGES                                               ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

// ─── Update UserRole entity — add Priority and ProductId ─────────────────────
// File: AGOne.Domain/Entities/User.cs (update existing UserRole class)

public class UserRole : BaseEntity
{
    public Guid UserId { get; set; }
    public virtual User? User { get; set; }
    public Guid RoleId { get; set; }
    public virtual Role? Role { get; set; }
    public Guid TenantId { get; set; }
    public virtual Tenant? Tenant { get; set; }

    // NEW: Which product this role applies to (null = platform-wide role)
    public Guid? ProductId { get; set; }
    public virtual Product? Product { get; set; }

    // NEW: Lower number = higher priority. Priority 1 is the "primary" role.
    // When user logs in, roles are sorted by Priority ascending.
    public int Priority { get; set; } = 100;
}


// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 2: EF CORE MIGRATION                                            ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

// Run these commands after updating the entity:
//   dotnet ef migrations add AddRolePriorityAndProductToUserRole
//   dotnet ef database update
//
// Or add this SQL manually:
//
//   ALTER TABLE UserRoles ADD ProductId UNIQUEIDENTIFIER NULL;
//   ALTER TABLE UserRoles ADD Priority INT NOT NULL DEFAULT 100;
//   ALTER TABLE UserRoles ADD CONSTRAINT FK_UserRoles_Products
//       FOREIGN KEY (ProductId) REFERENCES Products(Id);
//   CREATE INDEX IX_UserRoles_Priority ON UserRoles(UserId, Priority);


// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 3: DTOs                                                          ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

// File: AGOne.Shared/DTOs/UserRoleDtos.cs

public class UserRoleDto
{
    public Guid Id { get; set; }
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = "";
    public string RoleDisplayName { get; set; } = "";
    public Guid? ProductId { get; set; }
    public string? ProductName { get; set; }
    public int Priority { get; set; }
    public bool IsSystemRole { get; set; }
    public List<string> Permissions { get; set; } = new();
}

public class AssignRoleDto
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid? ProductId { get; set; }
    public int? Priority { get; set; }
}

public class UpdateRolePriorityDto
{
    public Guid UserRoleId { get; set; }
    public int NewPriority { get; set; }
}

public class BulkUpdatePrioritiesDto
{
    public Guid UserId { get; set; }
    public List<RolePriorityItem> Priorities { get; set; } = new();
}

public class RolePriorityItem
{
    public Guid UserRoleId { get; set; }
    public int Priority { get; set; }
}

// Update UserDto — add structured role data
// File: AGOne.Shared/DTOs/UserDto.cs (add these properties)

// Add to existing UserDto:
//   public List<UserRoleDto> UserProductRoles { get; set; } = new();
//   public string PrimaryRole => UserProductRoles.OrderBy(r => r.Priority).FirstOrDefault()?.RoleName ?? "No Role";


// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 4: BACKEND API CONTROLLER                                        ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

// File: Controllers/UserRolesController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/users/{userId}/roles")]
[Authorize]
public class UserRolesController : ControllerBase
{
    private readonly AGOneDbContext _db;
    private readonly ILogger<UserRolesController> _logger;

    public UserRolesController(AGOneDbContext db, ILogger<UserRolesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Get all roles for a user, ordered by priority (lowest number = highest priority)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<UserRoleDto>>> GetUserRoles(Guid userId)
    {
        var roles = await _db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Include(ur => ur.Role)
                .ThenInclude(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
            .Include(ur => ur.Product)
            .OrderBy(ur => ur.Priority)
            .Select(ur => new UserRoleDto
            {
                Id = ur.Id,
                RoleId = ur.RoleId,
                RoleName = ur.Role!.Name,
                RoleDisplayName = ur.Role.DisplayName,
                ProductId = ur.ProductId,
                ProductName = ur.Product != null ? ur.Product.Name : null,
                Priority = ur.Priority,
                IsSystemRole = ur.Role.IsSystemRole,
                Permissions = ur.Role.RolePermissions
                    .Select(rp => rp.Permission!.Name)
                    .ToList()
            })
            .ToListAsync();

        return Ok(roles);
    }

    /// <summary>
    /// Assign a new role to a user
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<UserRoleDto>> AssignRole(Guid userId, [FromBody] AssignRoleDto dto)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound("User not found");

        var role = await _db.Roles.FindAsync(dto.RoleId);
        if (role == null) return NotFound("Role not found");

        var exists = await _db.UserRoles.AnyAsync(ur =>
            ur.UserId == userId && ur.RoleId == dto.RoleId && ur.ProductId == dto.ProductId);
        if (exists) return Conflict("User already has this role for this product");

        var maxPriority = await _db.UserRoles
            .Where(ur => ur.UserId == userId)
            .MaxAsync(ur => (int?)ur.Priority) ?? 0;

        var userRole = new UserRole
        {
            UserId = userId,
            RoleId = dto.RoleId,
            TenantId = user.TenantId,
            ProductId = dto.ProductId,
            Priority = dto.Priority ?? (maxPriority + 10)
        };

        _db.UserRoles.Add(userRole);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Role {Role} assigned to user {User} with priority {Priority}",
            role.Name, userId, userRole.Priority);

        return CreatedAtAction(nameof(GetUserRoles), new { userId },
            new UserRoleDto
            {
                Id = userRole.Id,
                RoleId = userRole.RoleId,
                RoleName = role.Name,
                RoleDisplayName = role.DisplayName,
                ProductId = userRole.ProductId,
                Priority = userRole.Priority,
                IsSystemRole = role.IsSystemRole
            });
    }

    /// <summary>
    /// Remove a role from a user
    /// </summary>
    [HttpDelete("{userRoleId}")]
    public async Task<IActionResult> RemoveRole(Guid userId, Guid userRoleId)
    {
        var userRole = await _db.UserRoles
            .Include(ur => ur.Role)
            .FirstOrDefaultAsync(ur => ur.Id == userRoleId && ur.UserId == userId);

        if (userRole == null) return NotFound();
        if (userRole.Role?.IsSystemRole == true) return BadRequest("Cannot remove system roles");

        _db.UserRoles.Remove(userRole);
        await _db.SaveChangesAsync();

        // Re-sequence priorities so there are no gaps
        var remaining = await _db.UserRoles
            .Where(ur => ur.UserId == userId)
            .OrderBy(ur => ur.Priority)
            .ToListAsync();

        for (int i = 0; i < remaining.Count; i++)
            remaining[i].Priority = (i + 1) * 10;

        await _db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Reorder all roles for a user (drag-and-drop priority update)
    /// </summary>
    [HttpPut("reorder")]
    public async Task<IActionResult> ReorderRoles(Guid userId, [FromBody] BulkUpdatePrioritiesDto dto)
    {
        if (dto.UserId != userId) return BadRequest("UserId mismatch");

        var userRoles = await _db.UserRoles
            .Where(ur => ur.UserId == userId)
            .ToListAsync();

        foreach (var item in dto.Priorities)
        {
            var ur = userRoles.FirstOrDefault(r => r.Id == item.UserRoleId);
            if (ur != null) ur.Priority = item.Priority;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }
}


// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 5: TOKEN GENERATION — Include Prioritized Roles                  ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

// Update your ITokenService.GenerateJwtToken(User user) method.
// Add role claims sorted by priority so the first role claim is the primary one.
//
// In your TokenService class, update the method that creates JWT claims:

private async Task<List<Claim>> BuildClaims(User user)
{
    var claims = new List<Claim>
    {
        new Claim("sub", user.Id.ToString()),
        new Claim("email", user.Email),
        new Claim("name", user.FullName),
        new Claim("tenant_id", user.TenantId.ToString()),
    };

    // Load roles with priority
    var userRoles = await _db.UserRoles
        .Where(ur => ur.UserId == user.Id)
        .Include(ur => ur.Role)
        .Include(ur => ur.Product)
        .OrderBy(ur => ur.Priority)
        .ToListAsync();

    // Primary role = first by priority
    var primaryRole = userRoles.FirstOrDefault();
    if (primaryRole?.Role != null)
    {
        claims.Add(new Claim("primary_role", primaryRole.Role.Name));
    }

    // All roles as separate claims (ordered by priority)
    foreach (var ur in userRoles)
    {
        if (ur.Role == null) continue;

        // Standard role claim
        claims.Add(new Claim(ClaimTypes.Role, ur.Role.Name));

        // Product-specific role: "ProductCode:RoleName"
        if (ur.Product != null)
        {
            claims.Add(new Claim("product_role", $"{ur.Product.Code}:{ur.Role.Name}"));
        }
    }

    // Aggregate permissions from all roles
    var roleIds = userRoles.Select(ur => ur.RoleId).ToList();
    var permissions = await _db.RolePermissions
        .Where(rp => roleIds.Contains(rp.RoleId))
        .Include(rp => rp.Permission)
        .Select(rp => rp.Permission!.Name)
        .Distinct()
        .ToListAsync();

    foreach (var perm in permissions)
    {
        claims.Add(new Claim("permission", perm));
    }

    return claims;
}


// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 6: PROGRAM.CS — Authorization Policies                           ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

// Add these to your authorization configuration in Program.cs,
// inside the builder.Services.AddAuthorization(options => { ... }) block:

// Permission-based policies (fine-grained)
options.AddPolicy("CanManageUsers", policy =>
    policy.RequireClaim("permission", "users:manage"));

options.AddPolicy("CanManageMasterData", policy =>
    policy.RequireClaim("permission", "masterdata:manage"));

options.AddPolicy("CanManageSettings", policy =>
    policy.RequireClaim("permission", "manage:settings"));

options.AddPolicy("CanManageBillings", policy =>
    policy.RequireClaim("permission", "manage:billings"));

// Product-specific role check (use in controllers):
//   [Authorize(Policy = "ProductAdmin")]
//   Then in the action, check: User.FindAll("product_role")
//   to see which products the user is admin for.
