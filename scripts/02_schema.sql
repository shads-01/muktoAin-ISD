-- MuktoAin — Schema (14 tables)
-- Run after 01_init_database.sql, in SSMS, with database context = MuktoAin.
--
-- Naming rule (see AGENTS.md §3.4 / Tultul_plan.md Step 1.6): table names are
-- UPPERCASE with underscores, exactly matching design.md §2, and referenced
-- as [dbo].[TABLE_NAME] everywhere -- in this script, in EF Core's
-- ToTable(...) configuration, and in every raw SQL query. USER and CASE are
-- T-SQL reserved words and would fail to parse unbracketed; every table name
-- is bracketed for consistency with that rule. Column, constraint, and index
-- names below are plain identifiers -- none of them collide with reserved
-- words, so they're left unbracketed.
--
-- All primary key constraints are explicitly named (PK_<TABLE>) rather than
-- left to SQL Server's auto-generated names, so later scripts (e.g.
-- 03_fulltext.sql's KEY INDEX clause) can reference them reliably.
--
-- Idempotent: safe to re-run. Tables are created in FK-dependency order
-- (tier 1 -> tier 5, matching MuktoAin.Domain/Entities' build order).

USE MuktoAin;
GO

-- Required for filtered indexes (IX_USER_NormalizedUserName,
-- IX_ACT_SECTION_CHUNK_VectorId_Null below) -- sqlcmd's default session
-- setting is OFF, which CREATE INDEX rejects for filtered/computed indexes.
SET QUOTED_IDENTIFIER ON;
GO

-- =====================================================================
-- Tier 1: no foreign keys
-- =====================================================================

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DISTRICT]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[DISTRICT]
    (
        DistrictId TINYINT       NOT NULL,
        Name       NVARCHAR(100) NOT NULL,
        CONSTRAINT PK_DISTRICT PRIMARY KEY (DistrictId),
        CONSTRAINT UQ_DISTRICT_Name UNIQUE (Name)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CASE_CATEGORY]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[CASE_CATEGORY]
    (
        CategoryId  INT IDENTITY(1,1) NOT NULL,
        Name        NVARCHAR(100)     NOT NULL,
        Description NVARCHAR(500)     NOT NULL,
        CONSTRAINT PK_CASE_CATEGORY PRIMARY KEY (CategoryId)
    );
END
GO

-- =====================================================================
-- Tier 2: [USER] and ACT
-- =====================================================================

-- [USER] carries both the domain-specific fields from design.md §2.1 AND the
-- standard ASP.NET Core Identity columns (IdentityUser<int> base properties),
-- since MuktoAin.Domain.Entities.User inherits IdentityUser<int> directly
-- (Option A -- see the decision-point comment in User.cs). The physical PK
-- column is named UserId (not Id) to stay consistent with every other
-- table's <Entity>Id convention; EF Core maps User.Id -> UserId via Fluent
-- configuration in Infrastructure, not via column renaming here.
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[USER]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[USER]
    (
        UserId              INT IDENTITY(1,1) NOT NULL,

        -- Domain fields (design.md §2.1)
        FullName            NVARCHAR(150)  NOT NULL,
        PhoneNumber         NVARCHAR(20)   NULL,
        Role                INT            NOT NULL,
        AccountStatus       INT            NOT NULL DEFAULT (0),
        PreferredLanguage   NVARCHAR(10)   NOT NULL DEFAULT ('bn'),
        CreatedByAdminId    INT            NULL,
        CreatedAt           DATETIME2      NOT NULL DEFAULT (SYSUTCDATETIME()),

        -- ASP.NET Core Identity base columns (IdentityUser<int>)
        UserName             NVARCHAR(256)        NULL,
        NormalizedUserName   NVARCHAR(256)        NULL,
        Email                NVARCHAR(256)        NOT NULL,
        NormalizedEmail      NVARCHAR(256)        NULL,
        EmailConfirmed       BIT                  NOT NULL DEFAULT (0),
        PasswordHash         NVARCHAR(MAX)        NULL,
        SecurityStamp        NVARCHAR(MAX)        NULL,
        ConcurrencyStamp     NVARCHAR(MAX)        NULL,
        PhoneNumberConfirmed BIT                  NOT NULL DEFAULT (0),
        TwoFactorEnabled     BIT                  NOT NULL DEFAULT (0),
        LockoutEnd           DATETIMEOFFSET       NULL,
        LockoutEnabled       BIT                  NOT NULL DEFAULT (1),
        AccessFailedCount    INT                  NOT NULL DEFAULT (0),

        CONSTRAINT PK_USER PRIMARY KEY (UserId),
        CONSTRAINT UQ_USER_Email UNIQUE (Email),
        CONSTRAINT FK_USER_CreatedByAdmin FOREIGN KEY (CreatedByAdminId)
            REFERENCES [dbo].[USER] (UserId)
    );

    CREATE INDEX IX_USER_NormalizedEmail ON [dbo].[USER] (NormalizedEmail);
END
GO

-- Filtered index: kept outside the table-creation block above so it can be
-- created independently on a re-run even if the table already exists.
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_USER_NormalizedUserName' AND object_id = OBJECT_ID(N'[dbo].[USER]'))
BEGIN
    CREATE UNIQUE INDEX IX_USER_NormalizedUserName ON [dbo].[USER] (NormalizedUserName) WHERE NormalizedUserName IS NOT NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ACT]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[ACT]
    (
        ActId           INT IDENTITY(1,1) NOT NULL,
        Title           NVARCHAR(500)     NOT NULL,
        ActNumber       NVARCHAR(50)      NOT NULL,
        Year            INT               NOT NULL,
        PublicationDate NVARCHAR(100)     NOT NULL,
        Language        NVARCHAR(20)      NOT NULL,
        IsRepealed      BIT               NOT NULL DEFAULT (0),
        TokenCount      INT               NOT NULL DEFAULT (0),
        SourceUrl       NVARCHAR(1000)    NOT NULL,
        ImportedAt      DATETIME2         NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_ACT PRIMARY KEY (ActId)
    );
END
GO

-- =====================================================================
-- Tier 3: LAWYER_PROFILE, [CASE], ACT_SECTION, ACT_FOOTNOTE
-- =====================================================================

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[LAWYER_PROFILE]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[LAWYER_PROFILE]
    (
        LawyerProfileId       INT IDENTITY(1,1) NOT NULL,
        UserId                INT               NOT NULL,
        BarRegistrationNumber NVARCHAR(100)     NOT NULL,
        VerificationStatus    INT               NOT NULL DEFAULT (0),
        VerifiedByAdminId     INT               NULL,
        Specialization        NVARCHAR(200)     NULL,
        VerifiedAt            DATETIME2         NULL,

        CONSTRAINT PK_LAWYER_PROFILE PRIMARY KEY (LawyerProfileId),
        CONSTRAINT UQ_LAWYER_PROFILE_UserId UNIQUE (UserId),
        CONSTRAINT UQ_LAWYER_PROFILE_BarRegistrationNumber UNIQUE (BarRegistrationNumber),
        CONSTRAINT FK_LAWYER_PROFILE_User FOREIGN KEY (UserId)
            REFERENCES [dbo].[USER] (UserId),
        CONSTRAINT FK_LAWYER_PROFILE_VerifiedByAdmin FOREIGN KEY (VerifiedByAdminId)
            REFERENCES [dbo].[USER] (UserId)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CASE]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[CASE]
    (
        CaseId                INT IDENTITY(1,1) NOT NULL,
        UserId                INT                NULL,
        CategoryId            INT                NOT NULL,
        DistrictId            TINYINT            NOT NULL,
        Title                 NVARCHAR(250)      NOT NULL,
        Description           NVARCHAR(MAX)      NOT NULL,
        Language              NVARCHAR(10)       NOT NULL,
        Status                INT                NOT NULL DEFAULT (0),
        IsAnonymous           BIT                NOT NULL DEFAULT (0),
        -- Guest tracking code for FR-8 (see data pipeline plan Step 1.6; wired by Step 2.1)
        AnonymousTrackingCode NVARCHAR(36)       NULL,
        CreatedAt             DATETIME2          NOT NULL DEFAULT (SYSUTCDATETIME()),
        UpdatedAt             DATETIME2          NOT NULL DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_CASE PRIMARY KEY (CaseId),
        CONSTRAINT FK_CASE_User FOREIGN KEY (UserId)
            REFERENCES [dbo].[USER] (UserId),
        CONSTRAINT FK_CASE_Category FOREIGN KEY (CategoryId)
            REFERENCES [dbo].[CASE_CATEGORY] (CategoryId),
        CONSTRAINT FK_CASE_District FOREIGN KEY (DistrictId)
            REFERENCES [dbo].[DISTRICT] (DistrictId)
    );

    CREATE INDEX IX_CASE_UserId ON [dbo].[CASE] (UserId);
    CREATE INDEX IX_CASE_CategoryId ON [dbo].[CASE] (CategoryId);
    CREATE INDEX IX_CASE_DistrictId ON [dbo].[CASE] (DistrictId);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ACT_SECTION]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[ACT_SECTION]
    (
        SectionId       INT IDENTITY(1,1) NOT NULL,
        ActId           INT               NOT NULL,
        OrdinalPosition INT               NOT NULL,
        SectionNumber   NVARCHAR(50)      NULL,
        SectionTitle    NVARCHAR(500)     NULL,
        SectionText     NVARCHAR(MAX)     NOT NULL,

        CONSTRAINT PK_ACT_SECTION PRIMARY KEY (SectionId),
        CONSTRAINT FK_ACT_SECTION_Act FOREIGN KEY (ActId)
            REFERENCES [dbo].[ACT] (ActId)
    );

    CREATE INDEX IX_ACT_SECTION_ActId ON [dbo].[ACT_SECTION] (ActId);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ACT_FOOTNOTE]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[ACT_FOOTNOTE]
    (
        FootnoteId    INT IDENTITY(1,1) NOT NULL,
        ActId         INT               NOT NULL,
        FootnoteOrder INT               NOT NULL,
        FootnoteText  NVARCHAR(MAX)     NOT NULL,

        CONSTRAINT PK_ACT_FOOTNOTE PRIMARY KEY (FootnoteId),
        CONSTRAINT FK_ACT_FOOTNOTE_Act FOREIGN KEY (ActId)
            REFERENCES [dbo].[ACT] (ActId)
    );

    CREATE INDEX IX_ACT_FOOTNOTE_ActId ON [dbo].[ACT_FOOTNOTE] (ActId);
END
GO

-- =====================================================================
-- Tier 4: ACT_SECTION_CHUNK, SCENARIO_MAPPING, GENERATED_DOCUMENT, CASE_ACT_REFERENCE
-- =====================================================================

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ACT_SECTION_CHUNK]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[ACT_SECTION_CHUNK]
    (
        ChunkId        INT IDENTITY(1,1) NOT NULL,
        SectionId      INT               NOT NULL,
        ChunkOrder     SMALLINT          NOT NULL,
        ChunkText      NVARCHAR(MAX)     NOT NULL,
        TokenCount     INT               NOT NULL DEFAULT (0),
        VectorId       NVARCHAR(64)      NULL,
        ContentHash    CHAR(64)          NULL,
        LastEmbeddedAt DATETIME2         NULL,

        CONSTRAINT PK_ACT_SECTION_CHUNK PRIMARY KEY (ChunkId),
        CONSTRAINT FK_ACT_SECTION_CHUNK_Section FOREIGN KEY (SectionId)
            REFERENCES [dbo].[ACT_SECTION] (SectionId)
    );

    CREATE INDEX IX_ACT_SECTION_CHUNK_SectionId ON [dbo].[ACT_SECTION_CHUNK] (SectionId);
END
GO

-- Filtered index: kept outside the table-creation block above so it can be
-- created independently on a re-run even if the table already exists.
-- Speeds up the embedding batch job scanning for unembedded chunks.
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ACT_SECTION_CHUNK_VectorId_Null' AND object_id = OBJECT_ID(N'[dbo].[ACT_SECTION_CHUNK]'))
BEGIN
    CREATE INDEX IX_ACT_SECTION_CHUNK_VectorId_Null ON [dbo].[ACT_SECTION_CHUNK] (VectorId) WHERE VectorId IS NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SCENARIO_MAPPING]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[SCENARIO_MAPPING]
    (
        MappingId       INT IDENTITY(1,1) NOT NULL,
        SectionId       INT               NOT NULL,
        ScenarioKeyword NVARCHAR(200)     NOT NULL,
        Notes           NVARCHAR(500)     NULL,

        CONSTRAINT PK_SCENARIO_MAPPING PRIMARY KEY (MappingId),
        CONSTRAINT FK_SCENARIO_MAPPING_Section FOREIGN KEY (SectionId)
            REFERENCES [dbo].[ACT_SECTION] (SectionId)
    );

    CREATE INDEX IX_SCENARIO_MAPPING_SectionId ON [dbo].[SCENARIO_MAPPING] (SectionId);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[GENERATED_DOCUMENT]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[GENERATED_DOCUMENT]
    (
        DocumentId              INT IDENTITY(1,1) NOT NULL,
        CaseId                  INT               NOT NULL,
        DocumentType            INT               NOT NULL,
        ContentDraft            NVARCHAR(MAX)     NOT NULL,
        ContentFinal            NVARCHAR(MAX)     NULL,
        Status                  INT               NOT NULL DEFAULT (0),
        PdfPath                 NVARCHAR(500)     NULL,
        -- Review-claim guard (see data pipeline plan Step 1.6; wired by Step 2.7)
        AssignedLawyerProfileId INT               NULL,
        CreatedAt               DATETIME2         NOT NULL DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_GENERATED_DOCUMENT PRIMARY KEY (DocumentId),
        CONSTRAINT FK_GENERATED_DOCUMENT_Case FOREIGN KEY (CaseId)
            REFERENCES [dbo].[CASE] (CaseId),
        CONSTRAINT FK_GENERATED_DOCUMENT_AssignedLawyerProfile FOREIGN KEY (AssignedLawyerProfileId)
            REFERENCES [dbo].[LAWYER_PROFILE] (LawyerProfileId)
    );

    CREATE INDEX IX_GENERATED_DOCUMENT_CaseId ON [dbo].[GENERATED_DOCUMENT] (CaseId);
    CREATE INDEX IX_GENERATED_DOCUMENT_AssignedLawyerProfileId ON [dbo].[GENERATED_DOCUMENT] (AssignedLawyerProfileId);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CASE_ACT_REFERENCE]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[CASE_ACT_REFERENCE]
    (
        CaseActReferenceId INT IDENTITY(1,1) NOT NULL,
        CaseId             INT               NOT NULL,
        SectionId          INT               NOT NULL,
        RelevanceScore     DECIMAL(5,4)      NOT NULL,
        RetrievalMethod    INT               NOT NULL,

        CONSTRAINT PK_CASE_ACT_REFERENCE PRIMARY KEY (CaseActReferenceId),
        CONSTRAINT FK_CASE_ACT_REFERENCE_Case FOREIGN KEY (CaseId)
            REFERENCES [dbo].[CASE] (CaseId),
        CONSTRAINT FK_CASE_ACT_REFERENCE_Section FOREIGN KEY (SectionId)
            REFERENCES [dbo].[ACT_SECTION] (SectionId)
    );

    CREATE INDEX IX_CASE_ACT_REFERENCE_CaseId ON [dbo].[CASE_ACT_REFERENCE] (CaseId);
    CREATE INDEX IX_CASE_ACT_REFERENCE_SectionId ON [dbo].[CASE_ACT_REFERENCE] (SectionId);
END
GO

-- =====================================================================
-- Tier 5: LAWYER_REVIEW, AI_LOG
-- =====================================================================

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[LAWYER_REVIEW]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[LAWYER_REVIEW]
    (
        ReviewId        INT IDENTITY(1,1) NOT NULL,
        DocumentId      INT               NOT NULL,
        LawyerProfileId INT               NOT NULL,
        Decision        INT               NOT NULL,
        Comments        NVARCHAR(MAX)     NOT NULL,
        ReviewedAt      DATETIME2         NOT NULL DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_LAWYER_REVIEW PRIMARY KEY (ReviewId),
        CONSTRAINT FK_LAWYER_REVIEW_Document FOREIGN KEY (DocumentId)
            REFERENCES [dbo].[GENERATED_DOCUMENT] (DocumentId),
        CONSTRAINT FK_LAWYER_REVIEW_LawyerProfile FOREIGN KEY (LawyerProfileId)
            REFERENCES [dbo].[LAWYER_PROFILE] (LawyerProfileId)
    );

    CREATE INDEX IX_LAWYER_REVIEW_DocumentId ON [dbo].[LAWYER_REVIEW] (DocumentId);
    CREATE INDEX IX_LAWYER_REVIEW_LawyerProfileId ON [dbo].[LAWYER_REVIEW] (LawyerProfileId);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AI_LOG]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[AI_LOG]
    (
        LogId        BIGINT IDENTITY(1,1) NOT NULL,
        CaseId       INT                  NULL,
        RequestType  INT                  NOT NULL,
        PromptText   NVARCHAR(MAX)        NOT NULL,
        ResponseText NVARCHAR(MAX)        NOT NULL,
        ModelUsed    NVARCHAR(100)        NOT NULL,
        TokensUsed   INT                  NOT NULL DEFAULT (0),
        -- Round-trip API call duration in milliseconds (required by FR-12)
        LatencyMs    INT                  NOT NULL DEFAULT (0),
        CreatedAt    DATETIME2            NOT NULL DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_AI_LOG PRIMARY KEY (LogId),
        CONSTRAINT FK_AI_LOG_Case FOREIGN KEY (CaseId)
            REFERENCES [dbo].[CASE] (CaseId)
    );

    CREATE INDEX IX_AI_LOG_CaseId ON [dbo].[AI_LOG] (CaseId);
END
GO
