-- ═══════════════════════════════════════════════════════════════════════════════
-- PERMISSION SEED DATA — Run once against [agone-dev] database
-- Safe to re-run: skips existing permissions (WHERE NOT EXISTS check)
--
-- BEFORE RUNNING: Update the @ProductId variables below with your actual
-- Product GUIDs from [core].[Products] table.
-- ═══════════════════════════════════════════════════════════════════════════════

USE [agone-dev]
GO

-- ═══ STEP 1: Set your Product IDs here ═══
DECLARE @WorkId    UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM [core].[Products] WHERE Code = 'AGOneWork' OR Name LIKE '%Work%')
DECLARE @LearnId   UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM [core].[Products] WHERE Code = 'AGOneLearn' OR Name LIKE '%Learn%')
DECLARE @SafeId    UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM [core].[Products] WHERE Code = 'AGOneSafe' OR Name LIKE '%Safe%')
DECLARE @PulseId   UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM [core].[Products] WHERE Code = 'AGOnePulse' OR Name LIKE '%Pulse%')
DECLARE @SpotId    UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM [core].[Products] WHERE Code = 'AGOneSpot' OR Name LIKE '%Spot%')

PRINT 'Product IDs:'
PRINT '  Work:  ' + ISNULL(CAST(@WorkId AS NVARCHAR(50)), 'NOT FOUND')
PRINT '  Learn: ' + ISNULL(CAST(@LearnId AS NVARCHAR(50)), 'NOT FOUND')
PRINT '  Safe:  ' + ISNULL(CAST(@SafeId AS NVARCHAR(50)), 'NOT FOUND')
PRINT '  Pulse: ' + ISNULL(CAST(@PulseId AS NVARCHAR(50)), 'NOT FOUND')
PRINT '  Spot:  ' + ISNULL(CAST(@SpotId AS NVARCHAR(50)), 'NOT FOUND')

-- ═══ STEP 2: Insert permissions (idempotent — skips if Code already exists) ═══

-- Helper: insert only if not exists
;WITH PermData AS (
    SELECT * FROM (VALUES

    -- ═══════════════════════════════════════════════════════════════════════════
    -- AG ONE (admin) — Platform permissions (ProductId = NULL)
    -- ═══════════════════════════════════════════════════════════════════════════

    -- User Management
    ('agone.users.create',       'Create Users',       'User Management',   'Users',              'Create', NULL),
    ('agone.users.read',         'Read Users',         'User Management',   'Users',              'Read',   NULL),
    ('agone.users.update',       'Update Users',       'User Management',   'Users',              'Update', NULL),
    ('agone.users.delete',       'Delete Users',       'User Management',   'Users',              'Delete', NULL),
    ('agone.roles.create',       'Create Roles',       'User Management',   'Roles',              'Create', NULL),
    ('agone.roles.read',         'Read Roles',         'User Management',   'Roles',              'Read',   NULL),
    ('agone.roles.update',       'Update Roles',       'User Management',   'Roles',              'Update', NULL),
    ('agone.roles.delete',       'Delete Roles',       'User Management',   'Roles',              'Delete', NULL),
    ('agone.permissions.create', 'Create Permissions', 'User Management',   'Permissions',        'Create', NULL),
    ('agone.permissions.read',   'Read Permissions',   'User Management',   'Permissions',        'Read',   NULL),
    ('agone.permissions.update', 'Update Permissions', 'User Management',   'Permissions',        'Update', NULL),
    ('agone.permissions.delete', 'Delete Permissions', 'User Management',   'Permissions',        'Delete', NULL),

    -- Tenant Management
    ('agone.tenant.create',       'Create Tenant Info',    'Tenant Management', 'Tenant Information', 'Create', NULL),
    ('agone.tenant.read',         'Read Tenant Info',      'Tenant Management', 'Tenant Information', 'Read',   NULL),
    ('agone.tenant.update',       'Update Tenant Info',    'Tenant Management', 'Tenant Information', 'Update', NULL),
    ('agone.tenant.delete',       'Delete Tenant Info',    'Tenant Management', 'Tenant Information', 'Delete', NULL),
    ('agone.subscription.create', 'Create Subscriptions',  'Tenant Management', 'Subscriptions',      'Create', NULL),
    ('agone.subscription.read',   'Read Subscriptions',    'Tenant Management', 'Subscriptions',      'Read',   NULL),
    ('agone.subscription.update', 'Update Subscriptions',  'Tenant Management', 'Subscriptions',      'Update', NULL),
    ('agone.subscription.delete', 'Delete Subscriptions',  'Tenant Management', 'Subscriptions',      'Delete', NULL),
    ('agone.billing.create',      'Create Billing',        'Tenant Management', 'Billing',            'Create', NULL),
    ('agone.billing.read',        'Read Billing',          'Tenant Management', 'Billing',            'Read',   NULL),
    ('agone.billing.update',      'Update Billing',        'Tenant Management', 'Billing',            'Update', NULL),
    ('agone.billing.delete',      'Delete Billing',        'Tenant Management', 'Billing',            'Delete', NULL),

    -- Master Data
    ('agone.masterdata.create', 'Create Master Data', 'Master Data', 'Master Data', 'Create', NULL),
    ('agone.masterdata.read',   'Read Master Data',   'Master Data', 'Master Data', 'Read',   NULL),
    ('agone.masterdata.update', 'Update Master Data', 'Master Data', 'Master Data', 'Update', NULL),
    ('agone.masterdata.delete', 'Delete Master Data', 'Master Data', 'Master Data', 'Delete', NULL),

    -- Audit
    ('agone.audit.read', 'Read Audit Logs', 'Audit', 'Audit Logs', 'Read', NULL),

    -- ═══════════════════════════════════════════════════════════════════════════
    -- AG ONE Work
    -- ═══════════════════════════════════════════════════════════════════════════

    -- Employee Management
    ('work.employee.create',    'Create Employee',    'Employee Management', 'Employee',    'Create', @WorkId),
    ('work.employee.read',      'Read Employee',      'Employee Management', 'Employee',    'Read',   @WorkId),
    ('work.employee.update',    'Update Employee',    'Employee Management', 'Employee',    'Update', @WorkId),
    ('work.employee.delete',    'Delete Employee',    'Employee Management', 'Employee',    'Delete', @WorkId),
    ('work.recruitment.create', 'Create Recruitment', 'Employee Management', 'Recruitment', 'Create', @WorkId),
    ('work.recruitment.read',   'Read Recruitment',   'Employee Management', 'Recruitment', 'Read',   @WorkId),
    ('work.recruitment.update', 'Update Recruitment', 'Employee Management', 'Recruitment', 'Update', @WorkId),
    ('work.recruitment.delete', 'Delete Recruitment', 'Employee Management', 'Recruitment', 'Delete', @WorkId),

    -- Profile Management
    ('work.activate.create', 'Create Activate Profile', 'Profile Management', 'Activate Profile', 'Create', @WorkId),
    ('work.activate.read',   'Read Activate Profile',   'Profile Management', 'Activate Profile', 'Read',   @WorkId),
    ('work.activate.update', 'Update Activate Profile', 'Profile Management', 'Activate Profile', 'Update', @WorkId),
    ('work.activate.delete', 'Delete Activate Profile', 'Profile Management', 'Activate Profile', 'Delete', @WorkId),

    -- Master Data (Work)
    ('work.masterdata.create', 'Create Master Data', 'Master Data', 'Master Data', 'Create', @WorkId),
    ('work.masterdata.read',   'Read Master Data',   'Master Data', 'Master Data', 'Read',   @WorkId),
    ('work.masterdata.update', 'Update Master Data', 'Master Data', 'Master Data', 'Update', @WorkId),
    ('work.masterdata.delete', 'Delete Master Data', 'Master Data', 'Master Data', 'Delete', @WorkId),

    -- ═══════════════════════════════════════════════════════════════════════════
    -- AG ONE Learn
    -- ═══════════════════════════════════════════════════════════════════════════

    -- Learning
    ('learn.path.create',       'Create Learning Path',       'Learning', 'Learning Path',       'Create', @LearnId),
    ('learn.path.read',         'Read Learning Path',         'Learning', 'Learning Path',       'Read',   @LearnId),
    ('learn.path.update',       'Update Learning Path',       'Learning', 'Learning Path',       'Update', @LearnId),
    ('learn.path.delete',       'Delete Learning Path',       'Learning', 'Learning Path',       'Delete', @LearnId),
    ('learn.datasource.create', 'Create Data Source',         'Learning', 'Data Source',         'Create', @LearnId),
    ('learn.datasource.read',   'Read Data Source',           'Learning', 'Data Source',         'Read',   @LearnId),
    ('learn.datasource.update', 'Update Data Source',         'Learning', 'Data Source',         'Update', @LearnId),
    ('learn.datasource.delete', 'Delete Data Source',         'Learning', 'Data Source',         'Delete', @LearnId),
    ('learn.assignment.create', 'Create Learning Assignment', 'Learning', 'Learning Assignment', 'Create', @LearnId),
    ('learn.assignment.read',   'Read Learning Assignment',   'Learning', 'Learning Assignment', 'Read',   @LearnId),
    ('learn.assignment.update', 'Update Learning Assignment', 'Learning', 'Learning Assignment', 'Update', @LearnId),
    ('learn.assignment.delete', 'Delete Learning Assignment', 'Learning', 'Learning Assignment', 'Delete', @LearnId),

    -- Assessment
    ('learn.assessment.create', 'Create Learning Assessment', 'Assessment', 'Learning Assessment', 'Create', @LearnId),
    ('learn.assessment.read',   'Read Learning Assessment',   'Assessment', 'Learning Assessment', 'Read',   @LearnId),
    ('learn.assessment.update', 'Update Learning Assessment', 'Assessment', 'Learning Assessment', 'Update', @LearnId),
    ('learn.assessment.delete', 'Delete Learning Assessment', 'Assessment', 'Learning Assessment', 'Delete', @LearnId),

    -- ═══════════════════════════════════════════════════════════════════════════
    -- AG ONE Safe
    -- ═══════════════════════════════════════════════════════════════════════════

    -- Compliance
    ('safe.policy.create',     'Create Policies',   'Compliance',   'Policies',   'Create', @SafeId),
    ('safe.policy.read',       'Read Policies',     'Compliance',   'Policies',   'Read',   @SafeId),
    ('safe.policy.update',     'Update Policies',   'Compliance',   'Policies',   'Update', @SafeId),
    ('safe.policy.delete',     'Delete Policies',   'Compliance',   'Policies',   'Delete', @SafeId),
    ('safe.compliance.create', 'Create Compliance', 'Compliance',   'Compliance', 'Create', @SafeId),
    ('safe.compliance.read',   'Read Compliance',   'Compliance',   'Compliance', 'Read',   @SafeId),
    ('safe.compliance.update', 'Update Compliance', 'Compliance',   'Compliance', 'Update', @SafeId),
    ('safe.compliance.delete', 'Delete Compliance', 'Compliance',   'Compliance', 'Delete', @SafeId),

    -- Data Library
    ('safe.datalibrary.create', 'Create Data Library', 'Data Library', 'Data Library', 'Create', @SafeId),
    ('safe.datalibrary.read',   'Read Data Library',   'Data Library', 'Data Library', 'Read',   @SafeId),
    ('safe.datalibrary.update', 'Update Data Library', 'Data Library', 'Data Library', 'Update', @SafeId),
    ('safe.datalibrary.delete', 'Delete Data Library', 'Data Library', 'Data Library', 'Delete', @SafeId),

    -- ═══════════════════════════════════════════════════════════════════════════
    -- AG ONE Pulse
    -- ═══════════════════════════════════════════════════════════════════════════

    -- Surveys
    ('pulse.survey.create', 'Create Survey', 'Surveys',   'Survey',    'Create', @PulseId),
    ('pulse.survey.read',   'Read Survey',   'Surveys',   'Survey',    'Read',   @PulseId),
    ('pulse.survey.update', 'Update Survey', 'Surveys',   'Survey',    'Update', @PulseId),
    ('pulse.survey.delete', 'Delete Survey', 'Surveys',   'Survey',    'Delete', @PulseId),

    -- Analytics
    ('pulse.analytics.read', 'Read Analytics', 'Analytics', 'Analytics', 'Read', @PulseId),

    -- ═══════════════════════════════════════════════════════════════════════════
    -- AG ONE Spot
    -- ═══════════════════════════════════════════════════════════════════════════

    -- City Management
    ('spot.city.create', 'Create City Data', 'City Management', 'City Data', 'Create', @SpotId),
    ('spot.city.read',   'Read City Data',   'City Management', 'City Data', 'Read',   @SpotId),
    ('spot.city.update', 'Update City Data', 'City Management', 'City Data', 'Update', @SpotId),
    ('spot.city.delete', 'Delete City Data', 'City Management', 'City Data', 'Delete', @SpotId)

    ) AS p (Code, DisplayName, [Group], [Resource], [Action], ProductId)
)
INSERT INTO [core].[Permissions] ([Id], [Code], [DisplayName], [Description], [Group], [Resource], [Action], [ProductId], [IsSystemPermission], [CreatedAt], [UpdatedAt], [IsDeleted])
SELECT
    NEWID(),
    pd.Code,
    pd.DisplayName,
    NULL,
    pd.[Group],
    pd.[Resource],
    pd.[Action],
    pd.ProductId,
    1,
    GETUTCDATE(),
    NULL,
    0
FROM PermData pd
WHERE NOT EXISTS (
    SELECT 1 FROM [core].[Permissions] ex WHERE ex.Code = pd.Code AND ex.IsDeleted = 0
)

PRINT ''
PRINT 'Permissions seeded. Total in table: ' + CAST((SELECT COUNT(*) FROM [core].[Permissions] WHERE IsDeleted = 0) AS NVARCHAR(10))
GO
