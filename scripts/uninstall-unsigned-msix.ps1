[CmdletBinding()]
param(
    [string]$PackagePath,

    [string]$PackageName
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

function Get-MsixIdentityName {
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
        if ($null -eq $identity -or [string]::IsNullOrWhiteSpace($identity.Name)) {
            fail "'$Path' has no MSIX package identity."
        }

        return [string]$identity.Name
    }
    finally {
        $archive.Dispose()
    }
}

function Remove-ValidatedPackageDirectory {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$PackagesRoot,
        [Parameter(Mandatory)][string]$PackageFamily
    )

    $resolvedRoot = [IO.Path]::GetFullPath($PackagesRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $resolvedPath = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $actualParent = [IO.Path]::GetDirectoryName($resolvedPath).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $actualParent.Equals($resolvedRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($resolvedPath).Equals($PackageFamily, [StringComparison]::OrdinalIgnoreCase)) {
        fail "Refusing to remove unexpected package data directory '$resolvedPath'."
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        info "Removing package data '$resolvedPath'."
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

try {
    if (-not (Test-IsAdministrator)) {
        fail "Unsigned MSIX packages are installed for all users; run this script from an elevated PowerShell session."
    }

    if (-not [string]::IsNullOrWhiteSpace($PackagePath) -and -not [string]::IsNullOrWhiteSpace($PackageName)) {
        fail "Specify either PackagePath or PackageName, not both."
    }

    if ([string]::IsNullOrWhiteSpace($PackageName)) {
        if ([string]::IsNullOrWhiteSpace($PackagePath)) {
            $repositoryRoot = Split-Path -Parent $PSScriptRoot
            $package = Get-ChildItem `
                -Path (Join-Path $repositoryRoot "src\GitTool.App\AppPackages") `
                -Recurse `
                -File `
                -Filter *.msix |
                Sort-Object LastWriteTimeUtc -Descending |
                Select-Object -First 1
            if ($null -eq $package) {
                fail "No MSIX was found. Supply -PackageName or -PackagePath."
            }

            $PackagePath = $package.FullName
        }

        $PackageName = Get-MsixIdentityName (Resolve-Path -LiteralPath $PackagePath).Path
    }

    if (-not $PackageName.StartsWith("GitTool.", [StringComparison]::OrdinalIgnoreCase)) {
        fail "Refusing to remove unexpected package identity '$PackageName'."
    }

    $runningProcesses = Get-Process -Name "GitTool.App" -ErrorAction SilentlyContinue
    if ($null -ne $runningProcesses) {
        warn "GitTool is running and will be stopped before package removal."
        $runningProcesses | Stop-Process -Force
    }

    $packages = @(Get-AppxPackage -AllUsers -Name $PackageName)
    $packageFamilies = @($packages | Select-Object -ExpandProperty PackageFamilyName -Unique)
    if ($packages.Count -eq 0) {
        warn "The GitTool package '$PackageName' is not installed."
        exit 0
    }

    foreach ($package in $packages) {
        info "Removing package '$($package.PackageFullName)' for all users."
        Remove-AppxPackage -Package $package.PackageFullName -AllUsers
    }
    ok "Package registration was removed."

    Start-Sleep -Milliseconds 500
    $packagesRoot = Join-Path $env:LOCALAPPDATA "Packages"
    foreach ($packageFamily in $packageFamilies) {
        Remove-ValidatedPackageDirectory `
            -Path (Join-Path $packagesRoot $packageFamily) `
            -PackagesRoot $packagesRoot `
            -PackageFamily $packageFamily
    }

    ok "GitTool MSIX package and its package-managed data are removed."
    info "Unpackaged GitTool settings under %LOCALAPPDATA%\GitTool were not changed."
}
catch {
    fail $_.Exception.Message
}
