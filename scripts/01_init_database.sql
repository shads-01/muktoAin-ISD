-- MuktoAin — Database Initialization
-- Run first, in SSMS, against your local SQL Server instance.

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'MuktoAin')
BEGIN
    CREATE DATABASE MuktoAin;
END
GO

USE MuktoAin;
GO
