[CmdletBinding()]
param(
    [switch]$KeepLegacyData
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

function Remove-ValidatedDirectory {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$RequiredParent,
        [Parameter(Mandatory)][string]$RequiredLeaf
    )

    $resolvedParent = [IO.Path]::GetFullPath($RequiredParent).TrimEnd('\')
    $resolvedPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $actualParent = [IO.Path]::GetDirectoryName($resolvedPath).TrimEnd('\')
    $actualLeaf = [IO.Path]::GetFileName($resolvedPath)

    if (-not $actualParent.Equals($resolvedParent, [StringComparison]::OrdinalIgnoreCase) -or
        -not $actualLeaf.Equals($RequiredLeaf, [StringComparison]::OrdinalIgnoreCase)) {
        fail "Refusing to remove unexpected directory '$resolvedPath'."
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        info "Removing app data '$resolvedPath'."
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

try {
    if (-not (Test-IsAdministrator)) {
        fail "The unsigned development package is installed for all users; run this script from an elevated PowerShell session."
    }

    $runningProcesses = Get-Process -Name "GitTool.App" -ErrorAction SilentlyContinue
    if ($null -ne $runningProcesses) {
        warn "GitTool is running and will be stopped before package removal."
        $runningProcesses | Stop-Process -Force
    }

    $packages = @(Get-AppxPackage -AllUsers -Name "Ashish.GitTool.Experimental")
    $packageFamilies = @($packages | Select-Object -ExpandProperty PackageFamilyName -Unique)

    if ($packages.Count -eq 0) {
        warn "The experimental GitTool package is not installed."
    }
    else {
        foreach ($package in $packages) {
            info "Removing package '$($package.PackageFullName)' for all users."
            Remove-AppxPackage -Package $package.PackageFullName -AllUsers
        }
        ok "The experimental GitTool package registration was removed."
    }

    Start-Sleep -Milliseconds 500
    $packagesRoot = Join-Path $env:LOCALAPPDATA "Packages"
    foreach ($packageFamily in $packageFamilies) {
        if (-not $packageFamily.StartsWith("Ashish.GitTool.Experimental_", [StringComparison]::OrdinalIgnoreCase)) {
            fail "Refusing to clean unexpected package family '$packageFamily'."
        }

        Remove-ValidatedDirectory `
            -Path (Join-Path $packagesRoot $packageFamily) `
            -RequiredParent $packagesRoot `
            -RequiredLeaf $packageFamily
    }

    if ($KeepLegacyData) {
        warn "Legacy unpackaged settings and logs were kept."
    }
    else {
        Remove-ValidatedDirectory `
            -Path (Join-Path $env:LOCALAPPDATA "GitTool") `
            -RequiredParent $env:LOCALAPPDATA `
            -RequiredLeaf "GitTool"
    }

    ok "GitTool Experimental and its package-managed settings and logs are removed."
}
catch {
    fail $_.Exception.Message
}
