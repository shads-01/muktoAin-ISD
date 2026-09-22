-- MuktoAin — Database Initialization
-- Run first, in SSMS, against your local SQL Server instance.

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'MuktoAin')
BEGIN
    CREATE DATABASE MuktoAin;
END
GO

-- Enable Read Committed Snapshot Isolation (RCSI) so read queries never block writes and writers never block reads
ALTER DATABASE MuktoAin SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
ALTER DATABASE MuktoAin SET ALLOW_SNAPSHOT_ISOLATION ON;
ALTER DATABASE MuktoAin SET AUTO_CLOSE OFF;
GO

USE MuktoAin;
GO
