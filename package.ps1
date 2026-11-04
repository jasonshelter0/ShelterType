[CmdletBinding()]
param(
    [string]$Sdk = '',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\TypeEasy.Windows\TypeEasy.Windows.csproj') -Raw
$versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
if (!$versionNode) { throw 'The Windows project must declare Version.' }
$version = $versionNode.InnerText
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'A stable Major.Minor.Patch version is required for this release package.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot "artifacts\releases\$version" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$archive = Join-Path $OutputDirectory 'ShelterType-win-x64.zip'
$checksum = $archive + '.sha256'
if ((Test-Path -LiteralPath $archive) -or (Test-Path -LiteralPath $checksum)) {
    throw 'Release files already exist. Use a new OutputDirectory; released assets must not be silently replaced.'
}

# Never package an app folder that has been used for practice.
$staging = Join-Path $PSScriptRoot ('artifacts\publish\' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'build.ps1') -Sdk $Sdk -OutputDirectory $staging

function Copy-ReviewedAssets([string]$Name) {
    $source = Join-Path $PSScriptRoot $Name
    if (!(Test-Path -LiteralPath $source -PathType Container)) { throw "Missing course assets: $Name" }
    $items = @(Get-Item -LiteralPath $source) + @(Get-ChildItem -LiteralPath $source -Recurse -Force)
    foreach ($item in $items) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked course asset is not allowed: $($item.FullName)" }
        if ($item.Name -in @('UserData', 'TestData', '.git', '.env', 'hosts.yml', 'preferences.json', 'session.lock')) { throw "Unexpected private asset: $($item.FullName)" }
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $staging $Name) -Recurse
}
Copy-ReviewedAssets 'Data'
Copy-ReviewedAssets 'Teaching'
foreach ($name in @('README.md', 'ACKNOWLEDGEMENTS.md', 'THIRD-PARTY-NOTICES.md', '使用与测试.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $staging $name)
}
# Runtime licenses are retained exactly as emitted by the .NET SDK.
foreach ($item in Get-ChildItem -LiteralPath $staging -Recurse -Force) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked publish output is not allowed: $($item.FullName)" }
    if ($item.Name -in @('UserData', 'TestData', '.git', '.env', 'hosts.yml', 'session.lock')) { throw "Private file in release: $($item.FullName)" }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($staging, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
$digest = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($checksum, $digest + '  ShelterType-win-x64.zip' + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "Version: $version"
Write-Host "Package: $archive"
Write-Host "SHA256:  $digest"
Write-Host "Built app for testing: $staging"
