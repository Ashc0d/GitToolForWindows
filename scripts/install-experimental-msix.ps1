[CmdletBinding()]
param(
    [string]$PackagePath
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

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

try {
    if (-not (Test-IsAdministrator)) {
        fail "Unsigned executable MSIX packages require an elevated PowerShell session."
    }

    $repositoryRoot = Split-Path -Parent $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($PackagePath)) {
        $package = Get-ChildItem `
            -Path (Join-Path $repositoryRoot "src\GitTool.App\AppPackages") `
            -Recurse `
            -File `
            -Filter *.msix |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1

        if ($null -eq $package) {
            fail "No MSIX was found. Build one with scripts\build.ps1 -Configuration Release -Platform x64 -Target Msix from an experimental/* branch."
        }

        $resolvedPackagePath = $package.FullName
    }
    else {
        $resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
    }

    info "Installing unsigned development package '$resolvedPackagePath'."
    Add-AppxPackage `
        -Path $resolvedPackagePath `
        -AllowUnsigned `
        -ForceApplicationShutdown `
        -ForceUpdateFromAnyVersion

    $installedPackage = Get-AppxPackage -AllUsers -Name "Ashish.GitTool.Experimental" |
        Sort-Object Version -Descending |
        Select-Object -First 1

    if ($null -eq $installedPackage) {
        fail "Windows did not report the experimental GitTool package after installation."
    }

    ok "Installed $($installedPackage.Name) $($installedPackage.Version)."
    info "Launch 'GitTool Experimental' from the Start menu and run it without elevation."
}
catch {
    fail $_.Exception.Message
}
