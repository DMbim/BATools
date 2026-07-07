-- BA_FamilyVersioning catalog schema, v3
-- Target: SQLite 3, accessed via Microsoft.Data.Sqlite
-- One catalog database per PROJECT.
--
-- v3 changes vs v2:
--   AuditLog: added DiffSummary column (separate from Detail which is now user comment only)
--   TrackedCategories: new table for category filter (BuiltInCategory int key + display label)
--   SchemaVersion: bumped to 3
--
-- Migration from v2 to v3 is handled in CatalogConnectionFactory.EnsureSchema via
-- ALTER TABLE statements guarded by a SchemaVersion check. The CREATE TABLE IF NOT EXISTS
-- statements below are for fresh installs only.

CREATE TABLE IF NOT EXISTS ProjectInfo (
    ProjectInfoId          INTEGER PRIMARY KEY CHECK (ProjectInfoId = 1),
    ProjectName            TEXT    NOT NULL,
    CreatedUtc             TEXT    NOT NULL,
    SharedParameterFilePath      TEXT
);

CREATE TABLE IF NOT EXISTS Buildings (
    BuildingId             INTEGER PRIMARY KEY AUTOINCREMENT,
    BuildingName            TEXT    NOT NULL UNIQUE,
    CentralModelPath          TEXT    NOT NULL,
    Enabled               INTEGER NOT NULL DEFAULT 1,
    CreatedUtc             TEXT    NOT NULL,
    ModifiedUtc             TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS Families (
    FamilyId              INTEGER PRIMARY KEY AUTOINCREMENT,
    FamilyName             TEXT    NOT NULL,
    CategoryName            TEXT    NOT NULL,
    CanonicalVersion          TEXT    NOT NULL DEFAULT '0.0.0',
    CanonicalHash            TEXT    NOT NULL DEFAULT '',
    CanonicalSourcePath         TEXT,
    CreatedUtc             TEXT    NOT NULL,
    ModifiedUtc             TEXT    NOT NULL,
    UNIQUE (FamilyName, CategoryName)
);

CREATE INDEX IF NOT EXISTS IX_Families_Name ON Families (FamilyName);

CREATE TABLE IF NOT EXISTS FamilyBuildingState (
    StateId               INTEGER PRIMARY KEY AUTOINCREMENT,
    FamilyId              INTEGER NOT NULL,
    BuildingId             INTEGER NOT NULL,
    LoadedVersion            TEXT    NOT NULL DEFAULT '0.0.0',
    LoadedHash              TEXT    NOT NULL DEFAULT '',
    LastLoadedByUser          TEXT,
    LastLoadedUtc            TEXT,
    LastBumpKind             TEXT    NOT NULL DEFAULT 'Unknown',
    LastDiffSummary           TEXT,
    FOREIGN KEY (FamilyId) REFERENCES Families (FamilyId) ON DELETE CASCADE,
    FOREIGN KEY (BuildingId) REFERENCES Buildings (BuildingId) ON DELETE CASCADE,
    UNIQUE (FamilyId, BuildingId)
);

CREATE INDEX IF NOT EXISTS IX_FamilyBuildingState_Building ON FamilyBuildingState (BuildingId);
CREATE INDEX IF NOT EXISTS IX_FamilyBuildingState_FamilyId ON FamilyBuildingState (FamilyId);

CREATE TABLE IF NOT EXISTS ExceptionTable (
    ExceptionId             INTEGER PRIMARY KEY AUTOINCREMENT,
    FamilyId              INTEGER NOT NULL,
    BuildingId             INTEGER NOT NULL,
    Reason                TEXT    NOT NULL,
    ApprovedByUser            TEXT    NOT NULL,
    CreatedUtc             TEXT    NOT NULL,
    Active                INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (FamilyId) REFERENCES Families (FamilyId) ON DELETE CASCADE,
    FOREIGN KEY (BuildingId) REFERENCES Buildings (BuildingId) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_ExceptionTable_OneActive
    ON ExceptionTable (FamilyId, BuildingId)
    WHERE Active = 1;

CREATE INDEX IF NOT EXISTS IX_ExceptionTable_Building ON ExceptionTable (BuildingId);

CREATE TABLE IF NOT EXISTS PendingRequests (
    RequestId              INTEGER PRIMARY KEY AUTOINCREMENT,
    FamilyId              INTEGER NOT NULL,
    TargetBuildingId          INTEGER NOT NULL,
    RequestedVersion          TEXT    NOT NULL,
    RequestedHash            TEXT    NOT NULL,
    RequestedByUser           TEXT    NOT NULL,
    RequestedUtc            TEXT    NOT NULL,
    Status                TEXT    NOT NULL DEFAULT 'Pending',
    ResolvedByUser            TEXT,
    ResolvedUtc             TEXT,
    ResolutionNote            TEXT,
    FOREIGN KEY (FamilyId) REFERENCES Families (FamilyId) ON DELETE CASCADE,
    FOREIGN KEY (TargetBuildingId) REFERENCES Buildings (BuildingId) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_PendingRequests_Target ON PendingRequests (TargetBuildingId, Status);

-- AuditLog v3: Detail is now user comment only. DiffSummary is the structural diff text.
-- Existing v2 rows have DiffSummary = NULL (the migration does not attempt to parse
-- and split the old concatenated Detail string, that data stays as-is in Detail).
CREATE TABLE IF NOT EXISTS AuditLog (
    AuditId               INTEGER PRIMARY KEY AUTOINCREMENT,
    FamilyId              INTEGER,
    BuildingId             INTEGER NOT NULL,
    EventType              TEXT    NOT NULL,
    EventUtc              TEXT    NOT NULL,
    EventUser              TEXT    NOT NULL,
    Detail                TEXT,
    DiffSummary             TEXT,
    FOREIGN KEY (FamilyId) REFERENCES Families (FamilyId) ON DELETE SET NULL,
    FOREIGN KEY (BuildingId) REFERENCES Buildings (BuildingId) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_AuditLog_Family ON AuditLog (FamilyId);
CREATE INDEX IF NOT EXISTS IX_AuditLog_EventUtc ON AuditLog (EventUtc);

-- TrackedCategories: controls which Revit family categories trigger detection.
-- BuiltInCategoryId stores the integer value of the Revit BuiltInCategory enum
-- (e.g. -2000023 for OST_Doors). This is the authoritative filter key used in the
-- DocumentChanged hook. CategoryLabel is the human-readable display name cached
-- at the time the category was added, used only for UI display, not for filtering.
-- Using the integer key makes the filter locale-independent: the hook compares
-- family.FamilyCategory.Id.Value against BuiltInCategoryId, not the string name.
CREATE TABLE IF NOT EXISTS TrackedCategories (
    TrackedCategoryId        INTEGER PRIMARY KEY AUTOINCREMENT,
    BuiltInCategoryId        INTEGER NOT NULL UNIQUE,
    CategoryLabel            TEXT    NOT NULL,
    Enabled               INTEGER NOT NULL DEFAULT 1,
    CreatedUtc             TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_TrackedCategories_Enabled ON TrackedCategories (Enabled);

CREATE TABLE IF NOT EXISTS SchemaVersion (
    Version               INTEGER NOT NULL
);

INSERT INTO SchemaVersion (Version)
SELECT 3
WHERE NOT EXISTS (SELECT 1 FROM SchemaVersion);
