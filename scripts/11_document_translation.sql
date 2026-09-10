/* ============================================================
   MuktoAin — Add DOCUMENT_TRANSLATION table (2026-09-10)

   Backs the on-demand AI-translated language toggle on document
   preview. One cached row per (DocumentId, Language) — see
   docs/superpowers/specs/2026-09-10-document-styling-language-toggle-design.md.
   Never the authoritative document; PDF export and ContentDraft/
   ContentFinal are untouched by this table.

   IDEMPOTENT: safe to re-run. Execute in SSMS against the MuktoAin
   database.
   ============================================================ */
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'DOCUMENT_TRANSLATION' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[DOCUMENT_TRANSLATION]
    (
        DocumentTranslationId INT IDENTITY(1,1) NOT NULL,
        DocumentId            INT               NOT NULL,
        Language              VARCHAR(2)        NOT NULL,
        TranslatedContent     NVARCHAR(MAX)     NOT NULL,
        CreatedAt             DATETIME2         NOT NULL DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_DOCUMENT_TRANSLATION PRIMARY KEY (DocumentTranslationId),
        CONSTRAINT FK_DOCUMENT_TRANSLATION_GeneratedDocument FOREIGN KEY (DocumentId)
            REFERENCES [dbo].[GENERATED_DOCUMENT] (DocumentId)
    );

    CREATE UNIQUE INDEX UX_DOCUMENT_TRANSLATION_DocumentId_Language
        ON [dbo].[DOCUMENT_TRANSLATION] (DocumentId, Language);
END
GO
