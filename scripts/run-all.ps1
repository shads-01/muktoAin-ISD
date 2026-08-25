<#
.SYNOPSIS
    Runs every scripts/*.sql file, in numeric filename order, against a local
    SQL Server instance via sqlcmd. All scripts are idempotent, so this is
    safe to re-run any time (first-time setup, or after pulling schema changes).

.PARAMETER ServerInstance
    SQL Server instance name. Defaults to ".\SQLEXPRESS".

.EXAMPLE
    .\scripts\run-all.ps1
    .\scripts\run-all.ps1 -ServerInstance ".\MSSQLSERVER"
#>
param(
    [string]$ServerInstance = ".\SQLEXPRESS"
)

$ErrorActionPreference = "Stop"
$scriptsDir = $PSScriptRoot

$sqlFiles = Get-ChildItem -Path $scriptsDir -Filter "*.sql" |
    Sort-Object { [int]($_.BaseName -split "_")[0] }

if ($sqlFiles.Count -eq 0) {
    Write-Error "No .sql files found in $scriptsDir"
    exit 1
}

foreach ($file in $sqlFiles) {
    Write-Host "==> Running $($file.Name) against $ServerInstance ..." -ForegroundColor Cyan
    & sqlcmd -S $ServerInstance -E -C -i $file.FullName
    if ($LASTEXITCODE -ne 0) {
        Write-Error "$($file.Name) failed with exit code $LASTEXITCODE -- stopping."
        exit $LASTEXITCODE
    }
}

Write-Host "All scripts completed successfully." -ForegroundColor Green
