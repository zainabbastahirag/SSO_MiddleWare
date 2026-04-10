-- ═══════════════════════════════════════════════════════════════════════════════
-- PERMISSION SEED DATA — Run once against [agone-dev] database
-- Safe to re-run: skips existing permissions (WHERE NOT EXISTS check)
-- ═══════════════════════════════════════════════════════════════════════════════

USE [agone-dev]
GO

-- ═══ STEP 1: Resolve Product IDs ═══
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

-- ═══ STEP 2: Temp table for permission data ═══
CREATE TABLE #PermSeed (
    Code NVARCHAR(200),
    DisplayName NVARCHAR(200),
    [Group] NVARCHAR(200),
    [Resource] NVARCHAR(200),
    [Action] NVARCHAR(50),
    ProductId UNIQUEIDENTIFIER NULL
)

-- ═══ AG ONE (admin) — Platform permissions ═══
INSERT INTO #PermSeed VALUES ('agone.users.create',       'Create Users',       'User Management',   'Users',              'Create', NULL)
INSERT INTO #PermSeed VALUES ('agone.users.read',         'Read Users',         'User Management',   'Users',              'Read',   NULL)
INSERT INTO #PermSeed VALUES ('agone.users.update',       'Update Users',       'User Management',   'Users',              'Update', NULL)
INSERT INTO #PermSeed VALUES ('agone.users.delete',       'Delete Users',       'User Management',   'Users',              'Delete', NULL)
INSERT INTO #PermSeed VALUES ('agone.roles.create',       'Create Roles',       'User Management',   'Roles',              'Create', NULL)
INSERT INTO #PermSeed VALUES ('agone.roles.read',         'Read Roles',         'User Management',   'Roles',              'Read',   NULL)
INSERT INTO #PermSeed VALUES ('agone.roles.update',       'Update Roles',       'User Management',   'Roles',              'Update', NULL)
INSERT INTO #PermSeed VALUES ('agone.roles.delete',       'Delete Roles',       'User Management',   'Roles',              'Delete', NULL)
INSERT INTO #PermSeed VALUES ('agone.permissions.create', 'Create Permissions', 'User Management',   'Permissions',        'Create', NULL)
INSERT INTO #PermSeed VALUES ('agone.permissions.read',   'Read Permissions',   'User Management',   'Permissions',        'Read',   NULL)
INSERT INTO #PermSeed VALUES ('agone.permissions.update', 'Update Permissions', 'User Management',   'Permissions',        'Update', NULL)
INSERT INTO #PermSeed VALUES ('agone.permissions.delete', 'Delete Permissions', 'User Management',   'Permissions',        'Delete', NULL)
INSERT INTO #PermSeed VALUES ('agone.tenant.create',       'Create Tenant Info',    'Tenant Management', 'Tenant Information', 'Create', NULL)
INSERT INTO #PermSeed VALUES ('agone.tenant.read',         'Read Tenant Info',      'Tenant Management', 'Tenant Information', 'Read',   NULL)
INSERT INTO #PermSeed VALUES ('agone.tenant.update',       'Update Tenant Info',    'Tenant Management', 'Tenant Information', 'Update', NULL)
INSERT INTO #PermSeed VALUES ('agone.tenant.delete',       'Delete Tenant Info',    'Tenant Management', 'Tenant Information', 'Delete', NULL)
INSERT INTO #PermSeed VALUES ('agone.subscription.create', 'Create Subscriptions',  'Tenant Management', 'Subscriptions',      'Create', NULL)
INSERT INTO #PermSeed VALUES ('agone.subscription.read',   'Read Subscriptions',    'Tenant Management', 'Subscriptions',      'Read',   NULL)
INSERT INTO #PermSeed VALUES ('agone.subscription.update', 'Update Subscriptions',  'Tenant Management', 'Subscriptions',      'Update', NULL)
INSERT INTO #PermSeed VALUES ('agone.subscription.delete', 'Delete Subscriptions',  'Tenant Management', 'Subscriptions',      'Delete', NULL)
INSERT INTO #PermSeed VALUES ('agone.billing.create',      'Create Billing',        'Tenant Management', 'Billing',            'Create', NULL)
INSERT INTO #PermSeed VALUES ('agone.billing.read',        'Read Billing',          'Tenant Management', 'Billing',            'Read',   NULL)
INSERT INTO #PermSeed VALUES ('agone.billing.update',      'Update Billing',        'Tenant Management', 'Billing',            'Update', NULL)
INSERT INTO #PermSeed VALUES ('agone.billing.delete',      'Delete Billing',        'Tenant Management', 'Billing',            'Delete', NULL)
INSERT INTO #PermSeed VALUES ('agone.masterdata.create',   'Create Master Data',    'Master Data',       'Master Data',        'Create', NULL)
INSERT INTO #PermSeed VALUES ('agone.masterdata.read',     'Read Master Data',      'Master Data',       'Master Data',        'Read',   NULL)
INSERT INTO #PermSeed VALUES ('agone.masterdata.update',   'Update Master Data',    'Master Data',       'Master Data',        'Update', NULL)
INSERT INTO #PermSeed VALUES ('agone.masterdata.delete',   'Delete Master Data',    'Master Data',       'Master Data',        'Delete', NULL)
INSERT INTO #PermSeed VALUES ('agone.audit.read',          'Read Audit Logs',       'Audit',             'Audit Logs',         'Read',   NULL)

-- ═══ AG ONE Work ═══
INSERT INTO #PermSeed VALUES ('work.employee.create',    'Create Employee',          'Employee Management', 'Employee',         'Create', @WorkId)
INSERT INTO #PermSeed VALUES ('work.employee.read',      'Read Employee',            'Employee Management', 'Employee',         'Read',   @WorkId)
INSERT INTO #PermSeed VALUES ('work.employee.update',    'Update Employee',          'Employee Management', 'Employee',         'Update', @WorkId)
INSERT INTO #PermSeed VALUES ('work.employee.delete',    'Delete Employee',          'Employee Management', 'Employee',         'Delete', @WorkId)
INSERT INTO #PermSeed VALUES ('work.recruitment.create', 'Create Recruitment',       'Employee Management', 'Recruitment',      'Create', @WorkId)
INSERT INTO #PermSeed VALUES ('work.recruitment.read',   'Read Recruitment',         'Employee Management', 'Recruitment',      'Read',   @WorkId)
INSERT INTO #PermSeed VALUES ('work.recruitment.update', 'Update Recruitment',       'Employee Management', 'Recruitment',      'Update', @WorkId)
INSERT INTO #PermSeed VALUES ('work.recruitment.delete', 'Delete Recruitment',       'Employee Management', 'Recruitment',      'Delete', @WorkId)
INSERT INTO #PermSeed VALUES ('work.activate.create',    'Create Activate Profile',  'Profile Management',  'Activate Profile', 'Create', @WorkId)
INSERT INTO #PermSeed VALUES ('work.activate.read',      'Read Activate Profile',    'Profile Management',  'Activate Profile', 'Read',   @WorkId)
INSERT INTO #PermSeed VALUES ('work.activate.update',    'Update Activate Profile',  'Profile Management',  'Activate Profile', 'Update', @WorkId)
INSERT INTO #PermSeed VALUES ('work.activate.delete',    'Delete Activate Profile',  'Profile Management',  'Activate Profile', 'Delete', @WorkId)
INSERT INTO #PermSeed VALUES ('work.masterdata.create',  'Create Master Data',       'Master Data',         'Master Data',      'Create', @WorkId)
INSERT INTO #PermSeed VALUES ('work.masterdata.read',    'Read Master Data',         'Master Data',         'Master Data',      'Read',   @WorkId)
INSERT INTO #PermSeed VALUES ('work.masterdata.update',  'Update Master Data',       'Master Data',         'Master Data',      'Update', @WorkId)
INSERT INTO #PermSeed VALUES ('work.masterdata.delete',  'Delete Master Data',       'Master Data',         'Master Data',      'Delete', @WorkId)

-- ═══ AG ONE Learn ═══
INSERT INTO #PermSeed VALUES ('learn.path.create',       'Create Learning Path',       'Learning',   'Learning Path',       'Create', @LearnId)
INSERT INTO #PermSeed VALUES ('learn.path.read',         'Read Learning Path',         'Learning',   'Learning Path',       'Read',   @LearnId)
INSERT INTO #PermSeed VALUES ('learn.path.update',       'Update Learning Path',       'Learning',   'Learning Path',       'Update', @LearnId)
INSERT INTO #PermSeed VALUES ('learn.path.delete',       'Delete Learning Path',       'Learning',   'Learning Path',       'Delete', @LearnId)
INSERT INTO #PermSeed VALUES ('learn.datasource.create', 'Create Data Source',         'Learning',   'Data Source',         'Create', @LearnId)
INSERT INTO #PermSeed VALUES ('learn.datasource.read',   'Read Data Source',           'Learning',   'Data Source',         'Read',   @LearnId)
INSERT INTO #PermSeed VALUES ('learn.datasource.update', 'Update Data Source',         'Learning',   'Data Source',         'Update', @LearnId)
INSERT INTO #PermSeed VALUES ('learn.datasource.delete', 'Delete Data Source',         'Learning',   'Data Source',         'Delete', @LearnId)
INSERT INTO #PermSeed VALUES ('learn.assignment.create', 'Create Learning Assignment', 'Learning',   'Learning Assignment', 'Create', @LearnId)
INSERT INTO #PermSeed VALUES ('learn.assignment.read',   'Read Learning Assignment',   'Learning',   'Learning Assignment', 'Read',   @LearnId)
INSERT INTO #PermSeed VALUES ('learn.assignment.update', 'Update Learning Assignment', 'Learning',   'Learning Assignment', 'Update', @LearnId)
INSERT INTO #PermSeed VALUES ('learn.assignment.delete', 'Delete Learning Assignment', 'Learning',   'Learning Assignment', 'Delete', @LearnId)
INSERT INTO #PermSeed VALUES ('learn.assessment.create', 'Create Learning Assessment', 'Assessment', 'Learning Assessment', 'Create', @LearnId)
INSERT INTO #PermSeed VALUES ('learn.assessment.read',   'Read Learning Assessment',   'Assessment', 'Learning Assessment', 'Read',   @LearnId)
INSERT INTO #PermSeed VALUES ('learn.assessment.update', 'Update Learning Assessment', 'Assessment', 'Learning Assessment', 'Update', @LearnId)
INSERT INTO #PermSeed VALUES ('learn.assessment.delete', 'Delete Learning Assessment', 'Assessment', 'Learning Assessment', 'Delete', @LearnId)

-- ═══ AG ONE Safe ═══
INSERT INTO #PermSeed VALUES ('safe.policy.create',      'Create Policies',      'Compliance',   'Policies',     'Create', @SafeId)
INSERT INTO #PermSeed VALUES ('safe.policy.read',        'Read Policies',        'Compliance',   'Policies',     'Read',   @SafeId)
INSERT INTO #PermSeed VALUES ('safe.policy.update',      'Update Policies',      'Compliance',   'Policies',     'Update', @SafeId)
INSERT INTO #PermSeed VALUES ('safe.policy.delete',      'Delete Policies',      'Compliance',   'Policies',     'Delete', @SafeId)
INSERT INTO #PermSeed VALUES ('safe.compliance.create',  'Create Compliance',    'Compliance',   'Compliance',   'Create', @SafeId)
INSERT INTO #PermSeed VALUES ('safe.compliance.read',    'Read Compliance',      'Compliance',   'Compliance',   'Read',   @SafeId)
INSERT INTO #PermSeed VALUES ('safe.compliance.update',  'Update Compliance',    'Compliance',   'Compliance',   'Update', @SafeId)
INSERT INTO #PermSeed VALUES ('safe.compliance.delete',  'Delete Compliance',    'Compliance',   'Compliance',   'Delete', @SafeId)
INSERT INTO #PermSeed VALUES ('safe.datalibrary.create', 'Create Data Library',  'Data Library', 'Data Library', 'Create', @SafeId)
INSERT INTO #PermSeed VALUES ('safe.datalibrary.read',   'Read Data Library',    'Data Library', 'Data Library', 'Read',   @SafeId)
INSERT INTO #PermSeed VALUES ('safe.datalibrary.update', 'Update Data Library',  'Data Library', 'Data Library', 'Update', @SafeId)
INSERT INTO #PermSeed VALUES ('safe.datalibrary.delete', 'Delete Data Library',  'Data Library', 'Data Library', 'Delete', @SafeId)

-- ═══ AG ONE Pulse ═══
INSERT INTO #PermSeed VALUES ('pulse.survey.create',   'Create Survey',   'Surveys',   'Survey',    'Create', @PulseId)
INSERT INTO #PermSeed VALUES ('pulse.survey.read',     'Read Survey',     'Surveys',   'Survey',    'Read',   @PulseId)
INSERT INTO #PermSeed VALUES ('pulse.survey.update',   'Update Survey',   'Surveys',   'Survey',    'Update', @PulseId)
INSERT INTO #PermSeed VALUES ('pulse.survey.delete',   'Delete Survey',   'Surveys',   'Survey',    'Delete', @PulseId)
INSERT INTO #PermSeed VALUES ('pulse.analytics.read',  'Read Analytics',  'Analytics', 'Analytics', 'Read',   @PulseId)

-- ═══ AG ONE Spot ═══
INSERT INTO #PermSeed VALUES ('spot.city.create', 'Create City Data', 'City Management', 'City Data', 'Create', @SpotId)
INSERT INTO #PermSeed VALUES ('spot.city.read',   'Read City Data',   'City Management', 'City Data', 'Read',   @SpotId)
INSERT INTO #PermSeed VALUES ('spot.city.update', 'Update City Data', 'City Management', 'City Data', 'Update', @SpotId)
INSERT INTO #PermSeed VALUES ('spot.city.delete', 'Delete City Data', 'City Management', 'City Data', 'Delete', @SpotId)

-- ═══ STEP 3: Insert into Permissions table (skip existing) ═══
INSERT INTO [core].[Permissions] ([Id], [Code], [DisplayName], [Description], [Group], [Resource], [Action], [ProductId], [IsSystemPermission], [CreatedAt], [UpdatedAt], [IsDeleted])
SELECT
    NEWID(),
    s.Code,
    s.DisplayName,
    NULL,
    s.[Group],
    s.[Resource],
    s.[Action],
    s.ProductId,
    1,
    GETUTCDATE(),
    NULL,
    0
FROM #PermSeed s
WHERE NOT EXISTS (
    SELECT 1 FROM [core].[Permissions] p WHERE p.Code = s.Code AND p.IsDeleted = 0
)

DECLARE @inserted INT = @@ROWCOUNT
DECLARE @total INT
SELECT @total = COUNT(*) FROM [core].[Permissions] WHERE IsDeleted = 0

PRINT ''
PRINT 'Inserted: ' + CAST(@inserted AS NVARCHAR(10)) + ' new permissions'
PRINT 'Total in table: ' + CAST(@total AS NVARCHAR(10))

DROP TABLE #PermSeed
GO
