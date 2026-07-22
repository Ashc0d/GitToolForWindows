[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("x64", "ARM64")]
    [string]$Platform = "x64",

    [ValidateSet("Standalone", "Msix")]
    [string]$Target = "Standalone",

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

function Get-MsixFlavor {
    $branch = (git branch --show-current).Trim()
    Assert-LastCommandSucceeded "Branch detection"

    switch -Regex ($branch) {
        "^master$" { return "Production" }
        "^development$" { return "Development" }
        "^experimental/" { return "Experimental" }
        default {
            fail "Unsigned MSIX builds are supported only from master, development, or experimental/* branches. Current branch: '$branch'."
        }
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
        dotnet run --project .\tests\GitTool.App.Tests\GitTool.App.Tests.csproj `
            --configuration Release `
            --no-restore `
            -p:Platform=x64
        Assert-LastCommandSucceeded "Notification tests"
        ok "Core and notification tests passed."
    }

    if ($Target -eq "Msix") {
        $msixFlavor = Get-MsixFlavor
        info "Building the unsigned $msixFlavor MSIX for $Platform."
        dotnet build .\src\GitTool.App\GitTool.App.csproj `
            --configuration $Configuration `
            --no-restore `
            -p:Platform=$Platform `
            -p:PackageFlavor=$msixFlavor `
            -p:GenerateAppxPackageOnBuild=true `
            -p:AppxPackageSigningEnabled=false `
            -p:AppxBundle=Never `
            -p:UapAppxPackageBuildMode=SideloadOnly `
            -p:NuGetAudit=false
        Assert-LastCommandSucceeded "MSIX package build"
        $package = Get-ChildItem `
            -Path .\src\GitTool.App\AppPackages `
            -Recurse `
            -File `
            -Filter *.msix |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1

        if ($null -eq $package) {
            fail "The MSIX build completed but no package was found."
        }

        ok "Unsigned $msixFlavor package created at '$($package.FullName)'."
        info "Install it manually from an elevated PowerShell session with Add-AppxPackage -AllowUnsigned."
    }
    else {
        $runtimeIdentifier = if ($Platform -eq "x64") { "win-x64" } else { "win-arm64" }
        $publishDirectory = Join-Path $repositoryRoot "artifacts\standalone\$Configuration\$Platform"

        info "Publishing the self-contained standalone app for $Platform."
        dotnet publish .\src\GitTool.App\GitTool.App.csproj `
            --configuration $Configuration `
            --runtime $runtimeIdentifier `
            --self-contained true `
            --no-restore `
            -p:Platform=$Platform `
            -p:WindowsAppSDKSelfContained=true `
            -p:PublishSingleFile=false `
            -p:PublishDir="$publishDirectory" `
            -p:NuGetAudit=false
        Assert-LastCommandSucceeded "Standalone publish"

        $executable = Join-Path $publishDirectory "GitTool.App.exe"
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
            fail "Standalone publish completed but '$executable' was not produced."
        }

        ok "Standalone app created at '$publishDirectory'."
    }
}
catch {
    fail $_.Exception.Message
}
finally {
    Pop-Location
}
