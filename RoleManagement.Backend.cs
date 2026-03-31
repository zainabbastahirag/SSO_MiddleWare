// ═══════════════════════════════════════════════════════════════════════════════
// ROLE MANAGEMENT — Backend Service + Controller + DTOs + Permission Seed
//
// Files to create in your project:
//   1. AGOne.Shared/DTOs/RoleManagementDtos.cs      → DTOs below
//   2. AGOne.Infrastructure/Services/IRoleManagementService.cs → Interface
//   3. AGOne.Infrastructure/Services/RoleManagementService.cs  → Implementation
//   4. AGOne/Controllers/RoleManagementController.cs           → API Controller
//
// Register in Program.cs:
//   builder.Services.AddScoped<IRoleManagementService, RoleManagementService>();
// ═══════════════════════════════════════════════════════════════════════════════


// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  DTOs — AGOne.Shared/DTOs/RoleManagementDtos.cs                          ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

namespace AGOne.Shared.DTOs;

public class RoleListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ProductId { get; set; }
    public string? ProductName { get; set; }
    public bool IsSystemRole { get; set; }
    public int UserCount { get; set; }
}

public class RoleDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ProductId { get; set; }
    public string? ProductName { get; set; }
    public bool IsSystemRole { get; set; }
    public List<Guid> ProductAccessIds { get; set; } = new();
    public List<Guid> PermissionIds { get; set; } = new();
}

public class CreateRoleDto
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<Guid> ProductAccessIds { get; set; } = new();
    public List<Guid> PermissionIds { get; set; } = new();
}

public class UpdateRoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<Guid> ProductAccessIds { get; set; } = new();
    public List<Guid> PermissionIds { get; set; } = new();
}

public class PermissionGroupDto
{
    public string GroupName { get; set; } = "";
    public Guid? ProductId { get; set; }
    public string? ProductName { get; set; }
    public List<PermissionItemDto> Permissions { get; set; } = new();
}

public class PermissionItemDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Resource { get; set; }
    public string? Group { get; set; }
    public bool HasCreate { get; set; }
    public bool HasRead { get; set; }
    public bool HasUpdate { get; set; }
    public bool HasDelete { get; set; }
    public Guid? ProductId { get; set; }
}

public class PermissionMatrixItemDto
{
    public string Resource { get; set; } = "";
    public string Group { get; set; } = "";
    public Guid? CreatePermId { get; set; }
    public Guid? ReadPermId { get; set; }
    public Guid? UpdatePermId { get; set; }
    public Guid? DeletePermId { get; set; }
}

public class ProductPermissionMatrixDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public List<PermissionGroupMatrixDto> Groups { get; set; } = new();
}

public class PermissionGroupMatrixDto
{
    public string GroupName { get; set; } = "";
    public List<PermissionRowDto> Rows { get; set; } = new();
}

public class PermissionRowDto
{
    public string Resource { get; set; } = "";
    public Guid? CreateId { get; set; }
    public Guid? ReadId { get; set; }
    public Guid? UpdateId { get; set; }
    public Guid? DeleteId { get; set; }
}


// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  SERVICE INTERFACE                                                        ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

public interface IRoleManagementService
{
    Task<List<RoleListItemDto>> GetRolesAsync(Guid? productFilter = null);
    Task<RoleDetailDto?> GetRoleByIdAsync(Guid roleId);
    Task<RoleDetailDto> CreateRoleAsync(CreateRoleDto dto);
    Task<RoleDetailDto?> UpdateRoleAsync(UpdateRoleDto dto);
    Task<bool> DeleteRoleAsync(Guid roleId);
    Task<List<ProductPermissionMatrixDto>> GetPermissionMatrixAsync();
    Task<ProductPermissionMatrixDto?> GetPermissionMatrixForProductAsync(Guid productId);
}


// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  SERVICE IMPLEMENTATION                                                   ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

using Microsoft.EntityFrameworkCore;

public class RoleManagementService : IRoleManagementService
{
    private readonly AGOneDbContext _db;
    private readonly ITenantProvider _tenantProvider;

    public RoleManagementService(AGOneDbContext db, ITenantProvider tenantProvider)
    {
        _db = db;
        _tenantProvider = tenantProvider;
    }

    public async Task<List<RoleListItemDto>> GetRolesAsync(Guid? productFilter = null)
    {
        var tenantId = _tenantProvider.TenantId;

        var query = _db.Roles
            .Where(r => !r.IsDeleted)
            .Where(r => r.TenantId == null || r.TenantId == tenantId);

        if (productFilter.HasValue)
            query = query.Where(r => r.ProductId == productFilter || r.ProductId == null);

        return await query
            .Select(r => new RoleListItemDto
            {
                Id = r.Id,
                Name = r.Name,
                DisplayName = r.DisplayName,
                Description = r.Description,
                ProductId = r.ProductId,
                ProductName = r.Product != null ? r.Product.Name : "AG ONE",
                IsSystemRole = r.IsSystemRole,
                UserCount = _db.UserRoles.Count(ur => ur.RoleId == r.Id && !ur.IsDeleted)
            })
            .OrderBy(r => r.ProductName)
            .ThenBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<RoleDetailDto?> GetRoleByIdAsync(Guid roleId)
    {
        var role = await _db.Roles
            .Include(r => r.Product)
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == roleId && !r.IsDeleted);

        if (role == null) return null;

        return new RoleDetailDto
        {
            Id = role.Id,
            Name = role.Name,
            DisplayName = role.DisplayName,
            Description = role.Description,
            ProductId = role.ProductId,
            ProductName = role.Product?.Name,
            IsSystemRole = role.IsSystemRole,
            PermissionIds = role.RolePermissions.Where(rp => !rp.IsDeleted).Select(rp => rp.PermissionId).ToList()
        };
    }

    public async Task<RoleDetailDto> CreateRoleAsync(CreateRoleDto dto)
    {
        var tenantId = _tenantProvider.TenantId;

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            DisplayName = dto.Name,
            Description = dto.Description,
            TenantId = tenantId,
            ProductId = dto.ProductAccessIds.Count == 1 ? dto.ProductAccessIds[0] : null,
            IsSystemRole = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.Roles.Add(role);

        foreach (var permId in dto.PermissionIds)
        {
            _db.RolePermissions.Add(new RolePermission
            {
                Id = Guid.NewGuid(),
                RoleId = role.Id,
                PermissionId = permId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        return new RoleDetailDto
        {
            Id = role.Id,
            Name = role.Name,
            DisplayName = role.DisplayName,
            Description = role.Description,
            ProductId = role.ProductId,
            PermissionIds = dto.PermissionIds
        };
    }

    public async Task<RoleDetailDto?> UpdateRoleAsync(UpdateRoleDto dto)
    {
        var role = await _db.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == dto.Id && !r.IsDeleted);

        if (role == null) return null;
        if (role.IsSystemRole) return null;

        role.Name = dto.Name;
        role.DisplayName = dto.Name;
        role.Description = dto.Description;
        role.UpdatedAt = DateTime.UtcNow;

        var existingPermIds = role.RolePermissions.Where(rp => !rp.IsDeleted).Select(rp => rp.PermissionId).ToHashSet();
        var newPermIds = dto.PermissionIds.ToHashSet();

        foreach (var rp in role.RolePermissions.Where(rp => !rp.IsDeleted && !newPermIds.Contains(rp.PermissionId)))
            rp.IsDeleted = true;

        foreach (var permId in newPermIds.Where(id => !existingPermIds.Contains(id)))
        {
            _db.RolePermissions.Add(new RolePermission
            {
                Id = Guid.NewGuid(),
                RoleId = role.Id,
                PermissionId = permId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        return new RoleDetailDto
        {
            Id = role.Id,
            Name = role.Name,
            DisplayName = role.DisplayName,
            Description = role.Description,
            ProductId = role.ProductId,
            PermissionIds = dto.PermissionIds
        };
    }

    public async Task<bool> DeleteRoleAsync(Guid roleId)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId && !r.IsDeleted);
        if (role == null || role.IsSystemRole) return false;

        role.IsDeleted = true;
        role.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<ProductPermissionMatrixDto>> GetPermissionMatrixAsync()
    {
        var permissions = await _db.Permissions
            .Where(p => !p.IsDeleted)
            .Include(p => p.Product)
            .OrderBy(p => p.Product != null ? p.Product.Name : "AG ONE")
            .ThenBy(p => p.Group)
            .ThenBy(p => p.Resource)
            .ToListAsync();

        return BuildMatrix(permissions);
    }

    public async Task<ProductPermissionMatrixDto?> GetPermissionMatrixForProductAsync(Guid productId)
    {
        var permissions = await _db.Permissions
            .Where(p => !p.IsDeleted && p.ProductId == productId)
            .Include(p => p.Product)
            .OrderBy(p => p.Group)
            .ThenBy(p => p.Resource)
            .ToListAsync();

        var matrices = BuildMatrix(permissions);
        return matrices.FirstOrDefault();
    }

    private List<ProductPermissionMatrixDto> BuildMatrix(List<Permission> permissions)
    {
        var result = new List<ProductPermissionMatrixDto>();

        var byProduct = permissions.GroupBy(p => new { p.ProductId, ProductName = p.Product?.Name ?? "AG ONE (admin)" });

        foreach (var productGroup in byProduct)
        {
            var matrix = new ProductPermissionMatrixDto
            {
                ProductId = productGroup.Key.ProductId ?? Guid.Empty,
                ProductName = productGroup.Key.ProductName
            };

            var byGroup = productGroup.GroupBy(p => p.Group ?? "General");

            foreach (var group in byGroup)
            {
                var groupMatrix = new PermissionGroupMatrixDto { GroupName = group.Key };

                var byResource = group.GroupBy(p => p.Resource ?? p.DisplayName);

                foreach (var resource in byResource)
                {
                    var row = new PermissionRowDto { Resource = resource.Key };
                    foreach (var perm in resource)
                    {
                        var action = (perm.Action ?? "").ToLower();
                        if (action == "create") row.CreateId = perm.Id;
                        else if (action == "read") row.ReadId = perm.Id;
                        else if (action == "update") row.UpdateId = perm.Id;
                        else if (action == "delete") row.DeleteId = perm.Id;
                        else row.ReadId ??= perm.Id;
                    }
                    groupMatrix.Rows.Add(row);
                }
                matrix.Groups.Add(groupMatrix);
            }
            result.Add(matrix);
        }

        return result;
    }
}


// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  API CONTROLLER                                                           ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/roles")]
[Authorize]
public class RoleManagementController : ControllerBase
{
    private readonly IRoleManagementService _service;

    public RoleManagementController(IRoleManagementService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<List<RoleListItemDto>>> GetRoles([FromQuery] Guid? productId = null)
    {
        return Ok(await _service.GetRolesAsync(productId));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RoleDetailDto>> GetRole(Guid id)
    {
        var role = await _service.GetRoleByIdAsync(id);
        return role != null ? Ok(role) : NotFound();
    }

    [HttpPost]
    public async Task<ActionResult<RoleDetailDto>> CreateRole([FromBody] CreateRoleDto dto)
    {
        var result = await _service.CreateRoleAsync(dto);
        return CreatedAtAction(nameof(GetRole), new { id = result.Id }, result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<RoleDetailDto>> UpdateRole(Guid id, [FromBody] UpdateRoleDto dto)
    {
        dto.Id = id;
        var result = await _service.UpdateRoleAsync(dto);
        return result != null ? Ok(result) : NotFound();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRole(Guid id)
    {
        return await _service.DeleteRoleAsync(id) ? NoContent() : NotFound();
    }

    [HttpGet("permissions/matrix")]
    public async Task<ActionResult<List<ProductPermissionMatrixDto>>> GetPermissionMatrix()
    {
        return Ok(await _service.GetPermissionMatrixAsync());
    }

    [HttpGet("permissions/matrix/{productId}")]
    public async Task<ActionResult<ProductPermissionMatrixDto>> GetPermissionMatrixForProduct(Guid productId)
    {
        var result = await _service.GetPermissionMatrixForProductAsync(productId);
        return result != null ? Ok(result) : NotFound();
    }
}


// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  PERMISSION SEED DATA — Run once to populate Permissions table            ║
// ╚═══════════════════════════════════════════════════════════════════════════╝

// Call this from your database initializer or a one-time migration.
// Each permission has: Code, DisplayName, Group, Resource, Action, ProductId

public static class PermissionSeeder
{
    public static async Task SeedPermissionsAsync(AGOneDbContext db, Dictionary<string, Guid> productIds)
    {
        var existing = await db.Permissions.Select(p => p.Code).ToHashSetAsync();

        var permissions = new List<(string Code, string Display, string Group, string Resource, string Action, string? ProductKey)>
        {
            // ═══ AG ONE (admin) — platform permissions ═══
            ("agone.users.create", "Create Users", "User Management", "Users", "Create", null),
            ("agone.users.read", "Read Users", "User Management", "Users", "Read", null),
            ("agone.users.update", "Update Users", "User Management", "Users", "Update", null),
            ("agone.users.delete", "Delete Users", "User Management", "Users", "Delete", null),
            ("agone.roles.create", "Create Roles", "User Management", "Roles", "Create", null),
            ("agone.roles.read", "Read Roles", "User Management", "Roles", "Read", null),
            ("agone.roles.update", "Update Roles", "User Management", "Roles", "Update", null),
            ("agone.roles.delete", "Delete Roles", "User Management", "Roles", "Delete", null),
            ("agone.permissions.create", "Create Permissions", "User Management", "Permissions", "Create", null),
            ("agone.permissions.read", "Read Permissions", "User Management", "Permissions", "Read", null),
            ("agone.permissions.update", "Update Permissions", "User Management", "Permissions", "Update", null),
            ("agone.permissions.delete", "Delete Permissions", "User Management", "Permissions", "Delete", null),
            ("agone.tenant.create", "Create Tenant Info", "Tenant Management", "Tenant Information", "Create", null),
            ("agone.tenant.read", "Read Tenant Info", "Tenant Management", "Tenant Information", "Read", null),
            ("agone.tenant.update", "Update Tenant Info", "Tenant Management", "Tenant Information", "Update", null),
            ("agone.tenant.delete", "Delete Tenant Info", "Tenant Management", "Tenant Information", "Delete", null),
            ("agone.subscription.create", "Create Subscriptions", "Tenant Management", "Subscriptions", "Create", null),
            ("agone.subscription.read", "Read Subscriptions", "Tenant Management", "Subscriptions", "Read", null),
            ("agone.subscription.update", "Update Subscriptions", "Tenant Management", "Subscriptions", "Update", null),
            ("agone.subscription.delete", "Delete Subscriptions", "Tenant Management", "Subscriptions", "Delete", null),
            ("agone.billing.create", "Create Billing", "Tenant Management", "Billing", "Create", null),
            ("agone.billing.read", "Read Billing", "Tenant Management", "Billing", "Read", null),
            ("agone.billing.update", "Update Billing", "Tenant Management", "Billing", "Update", null),
            ("agone.billing.delete", "Delete Billing", "Tenant Management", "Billing", "Delete", null),
            ("agone.masterdata.create", "Create Master Data", "Master Data", "Master Data", "Create", null),
            ("agone.masterdata.read", "Read Master Data", "Master Data", "Master Data", "Read", null),
            ("agone.masterdata.update", "Update Master Data", "Master Data", "Master Data", "Update", null),
            ("agone.masterdata.delete", "Delete Master Data", "Master Data", "Master Data", "Delete", null),
            ("agone.audit.read", "Read Audit Logs", "Audit", "Audit Logs", "Read", null),

            // ═══ AG ONE Work ═══
            ("work.employee.create", "Create Employee", "Employee Management", "Employee", "Create", "AGOneWork"),
            ("work.employee.read", "Read Employee", "Employee Management", "Employee", "Read", "AGOneWork"),
            ("work.employee.update", "Update Employee", "Employee Management", "Employee", "Update", "AGOneWork"),
            ("work.employee.delete", "Delete Employee", "Employee Management", "Employee", "Delete", "AGOneWork"),
            ("work.recruitment.create", "Create Recruitment", "Employee Management", "Recruitment", "Create", "AGOneWork"),
            ("work.recruitment.read", "Read Recruitment", "Employee Management", "Recruitment", "Read", "AGOneWork"),
            ("work.recruitment.update", "Update Recruitment", "Employee Management", "Recruitment", "Update", "AGOneWork"),
            ("work.recruitment.delete", "Delete Recruitment", "Employee Management", "Recruitment", "Delete", "AGOneWork"),
            ("work.activate.create", "Create Activate Profile", "Profile Management", "Activate Profile", "Create", "AGOneWork"),
            ("work.activate.read", "Read Activate Profile", "Profile Management", "Activate Profile", "Read", "AGOneWork"),
            ("work.activate.update", "Update Activate Profile", "Profile Management", "Activate Profile", "Update", "AGOneWork"),
            ("work.activate.delete", "Delete Activate Profile", "Profile Management", "Activate Profile", "Delete", "AGOneWork"),
            ("work.masterdata.create", "Create Master Data", "Master Data", "Master Data", "Create", "AGOneWork"),
            ("work.masterdata.read", "Read Master Data", "Master Data", "Master Data", "Read", "AGOneWork"),
            ("work.masterdata.update", "Update Master Data", "Master Data", "Master Data", "Update", "AGOneWork"),
            ("work.masterdata.delete", "Delete Master Data", "Master Data", "Master Data", "Delete", "AGOneWork"),

            // ═══ AG ONE Learn ═══
            ("learn.path.create", "Create Learning Path", "Learning", "Learning Path", "Create", "AGOneLearn"),
            ("learn.path.read", "Read Learning Path", "Learning", "Learning Path", "Read", "AGOneLearn"),
            ("learn.path.update", "Update Learning Path", "Learning", "Learning Path", "Update", "AGOneLearn"),
            ("learn.path.delete", "Delete Learning Path", "Learning", "Learning Path", "Delete", "AGOneLearn"),
            ("learn.datasource.create", "Create Data Source", "Learning", "Data Source", "Create", "AGOneLearn"),
            ("learn.datasource.read", "Read Data Source", "Learning", "Data Source", "Read", "AGOneLearn"),
            ("learn.datasource.update", "Update Data Source", "Learning", "Data Source", "Update", "AGOneLearn"),
            ("learn.datasource.delete", "Delete Data Source", "Learning", "Data Source", "Delete", "AGOneLearn"),
            ("learn.assignment.create", "Create Learning Assignment", "Learning", "Learning Assignment", "Create", "AGOneLearn"),
            ("learn.assignment.read", "Read Learning Assignment", "Learning", "Learning Assignment", "Read", "AGOneLearn"),
            ("learn.assignment.update", "Update Learning Assignment", "Learning", "Learning Assignment", "Update", "AGOneLearn"),
            ("learn.assignment.delete", "Delete Learning Assignment", "Learning", "Learning Assignment", "Delete", "AGOneLearn"),
            ("learn.assessment.create", "Create Learning Assessment", "Assessment", "Learning Assessment", "Create", "AGOneLearn"),
            ("learn.assessment.read", "Read Learning Assessment", "Assessment", "Learning Assessment", "Read", "AGOneLearn"),
            ("learn.assessment.update", "Update Learning Assessment", "Assessment", "Learning Assessment", "Update", "AGOneLearn"),
            ("learn.assessment.delete", "Delete Learning Assessment", "Assessment", "Learning Assessment", "Delete", "AGOneLearn"),

            // ═══ AG ONE Safe ═══
            ("safe.policy.create", "Create Policies", "Compliance", "Policies", "Create", "AGOneSafe"),
            ("safe.policy.read", "Read Policies", "Compliance", "Policies", "Read", "AGOneSafe"),
            ("safe.policy.update", "Update Policies", "Compliance", "Policies", "Update", "AGOneSafe"),
            ("safe.policy.delete", "Delete Policies", "Compliance", "Policies", "Delete", "AGOneSafe"),
            ("safe.datalibrary.create", "Create Data Library", "Data Library", "Data Library", "Create", "AGOneSafe"),
            ("safe.datalibrary.read", "Read Data Library", "Data Library", "Data Library", "Read", "AGOneSafe"),
            ("safe.datalibrary.update", "Update Data Library", "Data Library", "Data Library", "Update", "AGOneSafe"),
            ("safe.datalibrary.delete", "Delete Data Library", "Data Library", "Data Library", "Delete", "AGOneSafe"),
            ("safe.compliance.create", "Create Compliance", "Compliance", "Compliance", "Create", "AGOneSafe"),
            ("safe.compliance.read", "Read Compliance", "Compliance", "Compliance", "Read", "AGOneSafe"),
            ("safe.compliance.update", "Update Compliance", "Compliance", "Compliance", "Update", "AGOneSafe"),
            ("safe.compliance.delete", "Delete Compliance", "Compliance", "Compliance", "Delete", "AGOneSafe"),

            // ═══ AG ONE Pulse ═══
            ("pulse.survey.create", "Create Survey", "Surveys", "Survey", "Create", "AGOnePulse"),
            ("pulse.survey.read", "Read Survey", "Surveys", "Survey", "Read", "AGOnePulse"),
            ("pulse.survey.update", "Update Survey", "Surveys", "Survey", "Update", "AGOnePulse"),
            ("pulse.survey.delete", "Delete Survey", "Surveys", "Survey", "Delete", "AGOnePulse"),
            ("pulse.analytics.read", "Read Analytics", "Analytics", "Analytics", "Read", "AGOnePulse"),

            // ═══ AG ONE Spot ═══
            ("spot.city.create", "Create City Data", "City Management", "City Data", "Create", "AGOneSpot"),
            ("spot.city.read", "Read City Data", "City Management", "City Data", "Read", "AGOneSpot"),
            ("spot.city.update", "Update City Data", "City Management", "City Data", "Update", "AGOneSpot"),
            ("spot.city.delete", "Delete City Data", "City Management", "City Data", "Delete", "AGOneSpot"),
        };

        foreach (var (code, display, group, resource, action, productKey) in permissions)
        {
            if (existing.Contains(code)) continue;

            Guid? productId = null;
            if (productKey != null && productIds.TryGetValue(productKey, out var pid))
                productId = pid;

            db.Permissions.Add(new Permission
            {
                Id = Guid.NewGuid(),
                Code = code,
                DisplayName = display,
                Group = group,
                Resource = resource,
                Action = action,
                ProductId = productId,
                IsSystemPermission = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }
}
