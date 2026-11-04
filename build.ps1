[CmdletBinding()]
param(
    [string]$Sdk = '',
    [string]$OutputDirectory = '',
    [ValidateSet('win-x64')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (!$Sdk) {
    $localSdk = Join-Path $PSScriptRoot '..\..\work\dotnet\dotnet.exe'
    $Sdk = if (Test-Path -LiteralPath $localSdk) { (Resolve-Path -LiteralPath $localSdk).Path } else { 'dotnet' }
}
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot ('artifacts\publish\' + [Guid]::NewGuid().ToString('N'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $OutputDirectory) -and (Get-ChildItem -LiteralPath $OutputDirectory -Force | Select-Object -First 1)) {
    throw 'OutputDirectory must be empty. Choose a new directory; the script never deletes an existing app.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$project = Join-Path $PSScriptRoot 'src\TypeEasy.Windows\TypeEasy.Windows.csproj'
$config = Join-Path $PSScriptRoot 'NuGet.Config'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

# Supply Microsoft runtime packs without changing the user's global NuGet configuration.
& $Sdk restore $project --runtime $Runtime --configfile $config --source https://api.nuget.org/v3/index.json -p:SelfContained=true
if ($LASTEXITCODE) { throw 'Restore failed.' }
& $Sdk publish $project --configuration Release --runtime $Runtime --self-contained true --no-restore --output $OutputDirectory -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE) { throw 'Publish failed.' }
foreach ($name in @('ShelterType.exe', 'ShelterType.dll', 'ShelterType.runtimeconfig.json', 'coreclr.dll', 'PresentationFramework.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $OutputDirectory $name) -PathType Leaf)) { throw "Missing publish output: $name" }
}
# The runtime packs' package-root license files are not automatically included by dotnet publish.
$assets = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\TypeEasy.Windows\obj\project.assets.json') -Raw | ConvertFrom-Json
$packageRoot = $assets.project.restore.packagesPath
$dependencies = @($assets.project.frameworks.PSObject.Properties | ForEach-Object { $_.Value.downloadDependencies })
foreach ($packId in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
    $dependency = $dependencies | Where-Object { $_.name -eq $packId } | Select-Object -First 1
    if (!$dependency -or $dependency.version -notmatch '^\[([^,]+),') { throw "Cannot resolve runtime license version: $packId" }
    $packRoot = Join-Path (Join-Path $packageRoot $packId.ToLowerInvariant()) $Matches[1]
    $licenseFiles = @(Get-ChildItem -LiteralPath $packRoot -File | Where-Object { $_.Name -match '^(LICENSE(?:\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' })
    if (!($licenseFiles | Where-Object { $_.Name -match '^LICENSE' })) { throw "Missing runtime license: $packId" }
    $licenseDestination = Join-Path (Join-Path $OutputDirectory 'licenses') $packId
    New-Item -ItemType Directory -Path $licenseDestination -Force | Out-Null
    foreach ($licenseFile in $licenseFiles) { Copy-Item -LiteralPath $licenseFile.FullName -Destination (Join-Path $licenseDestination $licenseFile.Name) }
}
Write-Host "Published self-contained $Runtime app: $OutputDirectory"
