[CmdletBinding()]
param(
    [string]$Sdk = '',
    [string]$CourseRoot = '',
    [switch]$IncludeUpdate
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (!$Sdk) {
    $localSdk = Join-Path $PSScriptRoot '..\..\work\dotnet\dotnet.exe'
    $Sdk = if (Test-Path -LiteralPath $localSdk) { (Resolve-Path -LiteralPath $localSdk).Path } else { 'dotnet' }
}
if (!$CourseRoot) { $CourseRoot = $PSScriptRoot }
$CourseRoot = [IO.Path]::GetFullPath($CourseRoot)
$names = @('TypeEasy.Core.Tests', 'TypeEasy.Analytics.Tests', 'TypeEasy.Heatmap.Tests', 'TypeEasy.FingerGuide.Tests', 'TypeEasy.ReviewMetadata.Tests', 'TypeEasy.Integration.Tests', 'TypeEasy.Catalog.Tests')
if ($IncludeUpdate) { $names += 'ShelterType.Update.Tests' }
$config = Join-Path $PSScriptRoot 'NuGet.Config'
foreach ($name in $names) {
    $folder = Join-Path (Join-Path $PSScriptRoot 'tests') $name
    $project = Join-Path $folder ($name + '.csproj')
    & $Sdk restore $project --configfile $config
    if ($LASTEXITCODE) { throw "Restore failed: $name" }
    & $Sdk build $project --configuration Release --no-restore
    if ($LASTEXITCODE) { throw "Build failed: $name" }
    $assembly = Join-Path $folder ("bin\Release\net8.0\$name.dll")

    # Failure probes run before any test profile or UI can be constructed.
    $previousErrorPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5 represents redirected native stderr as non-terminating errors.
        $ErrorActionPreference = 'Continue'
        $probe = & $Sdk $assembly --qa-verify-failure 2>&1
        $probeExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorPreference }
    if ($probeExitCode -ne 1 -or ($probe -join "`n") -notmatch '\(caught\)') { throw "Failure boundary was not verified: $name" }
    Write-Host "Failure boundary verified: $name"
    $testArguments = @()
    if ($name -in @('TypeEasy.Integration.Tests', 'TypeEasy.Catalog.Tests')) { $testArguments = @($CourseRoot) }
    & $Sdk $assembly @testArguments
    if ($LASTEXITCODE) { throw "Tests failed: $name" }
}
Write-Host "PASS: $($names.Count) test suites."
