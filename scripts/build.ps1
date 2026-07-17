[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("x64", "ARM64")]
    [string]$Platform = "x64",

    [switch]$Package,

    [switch]$SkipRestore,

    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function info {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Blue
}

function ok {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function warn {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function fail {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "[FAIL] $Message" -ForegroundColor Red
    exit 1
}

function Assert-LastCommandSucceeded {
    param([Parameter(Mandatory)][string]$Activity)
    if ($LASTEXITCODE -ne 0) {
        fail "$Activity failed with exit code $LASTEXITCODE."
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot

try {
    if ($SkipRestore) {
        warn "Package restore was skipped."
    }
    else {
        info "Restoring the solution with the .NET 10 SDK."
        dotnet restore .\GitTool.sln -p:NuGetAudit=false
        Assert-LastCommandSucceeded "Package restore"
    }

    if ($SkipTests) {
        warn "Core tests were skipped."
    }
    else {
        info "Running URL and local Git operation integration checks."
        dotnet run --project .\tests\GitTool.Core.Tests\GitTool.Core.Tests.csproj --configuration Release --no-restore
        Assert-LastCommandSucceeded "Core tests"
        ok "Core tests passed."
    }

    if ($Package) {
        info "Building the packaged WinUI 3 app for $Platform."
        dotnet build .\src\GitTool.App\GitTool.App.csproj `
            --configuration $Configuration `
            --no-restore `
            -p:Platform=$Platform `
            -p:GenerateAppxPackageOnBuild=true `
            -p:AppxPackageSigningEnabled=false `
            -p:NuGetAudit=false
        Assert-LastCommandSucceeded "MSIX package build"
        ok "Package created under src\GitTool.App\AppPackages."
    }
    else {
        info "Building the Visual Studio solution for $Platform."
        dotnet build .\GitTool.sln `
            --configuration $Configuration `
            --no-restore `
            -p:Platform=$Platform `
            -p:NuGetAudit=false
        Assert-LastCommandSucceeded "Solution build"
        ok "Solution build completed."
    }
}
catch {
    fail $_.Exception.Message
}
finally {
    Pop-Location
}
