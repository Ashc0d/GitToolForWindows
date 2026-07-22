[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("x64", "ARM64")]
    [string]$Platform = "x64",

    [ValidateSet("Standalone", "Msix-Unsigned")]
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

function Get-CurrentBranch {
    $branch = (git branch --show-current).Trim()
    Assert-LastCommandSucceeded "Branch detection"
    return $branch
}

function Get-MsixFlavor {
    param([Parameter(Mandatory)][string]$Branch)

    switch -Regex ($branch) {
        "^master$" { return "Production" }
        "^development$" { return "Development" }
        "^experimental/" { return "Experimental" }
        default {
            fail "Unsigned MSIX builds are supported only from master, development, or experimental/* branches. Current branch: '$branch'."
        }
    }
}

function Get-SafeMsixComponent {
    param([Parameter(Mandatory)][string]$Value)

    $component = $Value -replace "[^A-Za-z0-9.-]", "-"
    $component = $component.Trim(".", "-")
    if ([string]::IsNullOrWhiteSpace($component)) {
        fail "The current Windows username cannot be used in an MSIX identity."
    }

    return $component
}

function New-LocalPackageVersion {
    param(
        [Parameter(Mandatory)][string]$AppVersion,
        [Parameter(Mandatory)][string]$PackageIdentity,
        [Parameter(Mandatory)][string]$StatePath
    )

    $parsedAppVersion = [Version]$AppVersion
    $buildComponent = if ($parsedAppVersion.Build -ge 0) { $parsedAppVersion.Build } else { 0 }
    $versionPrefix = "$($parsedAppVersion.Major).$($parsedAppVersion.Minor).$buildComponent"
    $highestRevision = 0

    $installedPackages = @(Get-AppxPackage -Name $PackageIdentity -ErrorAction SilentlyContinue)
    foreach ($installedPackage in $installedPackages) {
        $installedVersion = [Version]$installedPackage.Version
        $installedPrefix = "$($installedVersion.Major).$($installedVersion.Minor).$($installedVersion.Build)"
        if ($installedPrefix -eq $versionPrefix) {
            $highestRevision = [Math]::Max($highestRevision, $installedVersion.Revision)
        }
    }

    if (Test-Path -LiteralPath $StatePath -PathType Leaf) {
        $stateVersion = [Version](Get-Content -LiteralPath $StatePath -Raw).Trim()
        $statePrefix = "$($stateVersion.Major).$($stateVersion.Minor).$($stateVersion.Build)"
        if ($statePrefix -eq $versionPrefix) {
            $highestRevision = [Math]::Max($highestRevision, $stateVersion.Revision)
        }
    }

    $nextRevision = $highestRevision + 1
    if ($nextRevision -gt [UInt16]::MaxValue) {
        fail "The local MSIX revision counter is exhausted for app version '$AppVersion'."
    }

    $packageVersion = "$versionPrefix.$nextRevision"
    Set-Content -LiteralPath $StatePath -Value $packageVersion -Encoding ascii
    return $packageVersion
}

function New-MsixBuildInputs {
    param(
        [Parameter(Mandatory)][string]$Flavor,
        [Parameter(Mandatory)][string]$Branch,
        [Parameter(Mandatory)][string]$Configuration,
        [Parameter(Mandatory)][string]$Platform,
        [Parameter(Mandatory)][string]$RepositoryRoot
    )

    $localUserName = [Environment]::UserName
    $safeUserName = Get-SafeMsixComponent $localUserName
    $flavorSuffix = if ($Flavor -eq "Production") { "" } else { ".$Flavor" }
    $packageIdentity = "GitTool.$safeUserName$flavorSuffix"
    # Windows requires this unsigned-namespace marker for Add-AppxPackage
    # -AllowUnsigned. The personal part of the publisher remains local-only.
    $packagePublisher = "CN=$safeUserName, OID.2.25.311729368913984317654407730594956997722=1"
    $publisherDisplayName = $localUserName
    $templateName = if ($Flavor -eq "Production") { "Package.appxmanifest" } else { "Package.$Flavor.appxmanifest" }
    $templatePath = Join-Path $RepositoryRoot "src\GitTool.App\$templateName"
    $intermediateDirectory = Join-Path $RepositoryRoot "src\GitTool.App\obj\MsixUnsigned\$Configuration\$Platform\$Flavor"
    $generatedManifestPath = Join-Path $intermediateDirectory "Package.appxmanifest"
    $buildMetadataPath = Join-Path $intermediateDirectory "BuildInfo.json"
    $versionStatePath = Join-Path $intermediateDirectory "PackageVersion.txt"

    if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
        fail "MSIX manifest template '$templatePath' was not found."
    }

    New-Item -ItemType Directory -Path $intermediateDirectory -Force | Out-Null
    $project = [xml](Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\GitTool.App\GitTool.App.csproj"))
    $appVersion = [string]$project.Project.PropertyGroup.Version
    $packageVersion = New-LocalPackageVersion `
        -AppVersion $appVersion `
        -PackageIdentity $packageIdentity `
        -StatePath $versionStatePath
    $template = Get-Content -LiteralPath $templatePath -Raw
    $manifest = $template.Replace("__PACKAGE_IDENTITY__", [Security.SecurityElement]::Escape($packageIdentity))
    $manifest = $manifest.Replace("__PACKAGE_PUBLISHER__", [Security.SecurityElement]::Escape($packagePublisher))
    $manifest = $manifest.Replace("__PUBLISHER_DISPLAY_NAME__", [Security.SecurityElement]::Escape($publisherDisplayName))
    $manifest = $manifest.Replace("__PACKAGE_VERSION__", $packageVersion)
    if ($manifest -match "__[A-Z_]+__") {
        fail "MSIX manifest template '$templatePath' contains an unresolved token."
    }

    Set-Content -LiteralPath $generatedManifestPath -Value $manifest -Encoding utf8

    $commit = (git rev-parse --short HEAD).Trim()
    Assert-LastCommandSucceeded "Source revision detection"
    $buildMetadata = [ordered]@{
        product = "GitTool"
        appVersion = $appVersion
        packageVersion = $packageVersion
        buildTimestampUtc = [DateTimeOffset]::UtcNow.ToString("O")
        builtBy = $localUserName
        buildMachine = [Environment]::MachineName
        branch = $Branch
        commit = $commit
        configuration = $Configuration
        platform = $Platform
        packageFlavor = $Flavor
        packageIdentity = $packageIdentity
        packagePublisher = $packagePublisher
    }
    $buildMetadata | ConvertTo-Json | Set-Content -LiteralPath $buildMetadataPath -Encoding utf8

    return [PSCustomObject]@{
        ManifestPath = $generatedManifestPath
        BuildMetadataPath = $buildMetadataPath
        PackageIdentity = $packageIdentity
        LocalUserName = $localUserName
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

    if ($Target -eq "Msix-Unsigned") {
        $branch = Get-CurrentBranch
        $msixFlavor = Get-MsixFlavor $branch
        $msixInputs = New-MsixBuildInputs `
            -Flavor $msixFlavor `
            -Branch $branch `
            -Configuration $Configuration `
            -Platform $Platform `
            -RepositoryRoot $repositoryRoot
        info "Building the unsigned $msixFlavor MSIX for local user '$($msixInputs.LocalUserName)'."
        dotnet build .\src\GitTool.App\GitTool.App.csproj `
            --configuration $Configuration `
            --no-restore `
            -p:Platform=$Platform `
            -p:PackageFlavor=$msixFlavor `
            -p:PackageManifest="$($msixInputs.ManifestPath)" `
            -p:MsixBuildMetadataFile="$($msixInputs.BuildMetadataPath)" `
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
        info "BuildInfo.json inside the MSIX records the local build details."
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
