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

function Get-MsixManifestInfo {
    param([Parameter(Mandatory)][string]$Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry("AppxManifest.xml")
        if ($null -eq $entry) {
            fail "'$Path' is not a valid MSIX package because AppxManifest.xml is missing."
        }

        $reader = [IO.StreamReader]::new($entry.Open())
        try {
            [xml]$manifest = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $namespace = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
        $namespace.AddNamespace("appx", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
        $identity = $manifest.SelectSingleNode("/appx:Package/appx:Identity", $namespace)
        $displayName = $manifest.SelectSingleNode("/appx:Package/appx:Properties/appx:DisplayName", $namespace)
        if ($null -eq $identity -or [string]::IsNullOrWhiteSpace($identity.Name)) {
            fail "'$Path' has no MSIX package identity."
        }

        return [PSCustomObject]@{
            Name = [string]$identity.Name
            DisplayName = [string]$displayName.InnerText
        }
    }
    finally {
        $archive.Dispose()
    }
}

try {
    if (-not (Test-IsAdministrator)) {
        fail "Unsigned MSIX packages require an elevated PowerShell session."
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
            fail "No MSIX was found. Build one with scripts\build.ps1 -Configuration Release -Platform x64 -Target Msix-Unsigned."
        }

        $resolvedPackagePath = $package.FullName
    }
    else {
        $resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
    }

    $manifestInfo = Get-MsixManifestInfo $resolvedPackagePath
    if (-not $manifestInfo.Name.StartsWith("GitTool.", [StringComparison]::OrdinalIgnoreCase)) {
        fail "Refusing to install unexpected package identity '$($manifestInfo.Name)'."
    }

    info "Installing unsigned '$($manifestInfo.DisplayName)' from '$resolvedPackagePath'."
    Add-AppxPackage `
        -Path $resolvedPackagePath `
        -AllowUnsigned `
        -ForceApplicationShutdown `
        -ForceUpdateFromAnyVersion

    $installedPackage = Get-AppxPackage -AllUsers -Name $manifestInfo.Name |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if ($null -eq $installedPackage) {
        fail "Windows did not report '$($manifestInfo.Name)' after installation."
    }

    ok "Installed $($manifestInfo.DisplayName) $($installedPackage.Version)."
    info "Launch '$($manifestInfo.DisplayName)' from the Start menu without elevation."
}
catch {
    fail $_.Exception.Message
}
