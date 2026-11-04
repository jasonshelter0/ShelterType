param([Parameter(Mandatory=$true)][string]$JobPath)
# This installer is embedded in the installed application, never downloaded as executable script.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$job = $null
$archive = $null
$packageStream = $null
$profileLock = $null
$updateLock = $null
$applied = [System.Collections.Generic.List[object]]::new()
$committed = $false
$rollbackComplete = $true
$logDirectory = [System.IO.Path]::GetFullPath((Split-Path -Parent $JobPath))
$failure = $null
$parentExited = $false
$validInstall = $false

function Assert-NoLinks([string]$Path) {
    $walk = [System.IO.Path]::GetFullPath($Path)
    while ($walk) {
        if ([System.IO.File]::Exists($walk) -or [System.IO.Directory]::Exists($walk)) {
            if (([System.IO.File]::GetAttributes($walk) -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Linked files or folders cannot be updated: $walk"
            }
        }
        $parent = [System.IO.Path]::GetDirectoryName($walk.TrimEnd('\'))
        if ($parent -eq $walk) { break }
        $walk = $parent
    }
}

function Assert-PackagePath([string]$Name) {
    if (!$Name -or $Name.Length -gt 220 -or $Name.Contains('\') -or $Name.Contains(':') -or $Name.StartsWith('/')) { throw 'Unsafe update package path.' }
    $parts = $Name.Split('/')
    if ($parts[0] -in @('UserData','TestData','.git','.sheltertype-update.lock')) { throw 'The update package contains protected personal data.' }
    foreach ($part in $parts) {
        if (!$part -or $part -in @('.','..') -or $part.EndsWith(' ') -or $part.EndsWith('.') -or
            $part -match '[\x00-\x1f<>"|?*]' -or $part -match '^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)') { throw 'Unsafe update package path.' }
    }
}

function Get-ChildPath([string]$Root, [string]$Relative) {
    $path = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($Root, $Relative.Replace('/', '\')))
    if (!$path.StartsWith($Root.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Update path escaped its root.' }
    return $path
}

function Confirm-ParentExited([int]$TimeoutMilliseconds) {
    if ([int]$job.ParentPid -le 0) { return $true }
    $parentProcess = Get-Process -Id ([int]$job.ParentPid) -ErrorAction SilentlyContinue
    # A reused process ID is not the instance which requested this update.
    if (!$parentProcess -or $parentProcess.StartTime.ToUniversalTime().Ticks -ne [long]$job.ParentStartUtcTicks) { return $true }
    return $parentProcess.WaitForExit($TimeoutMilliseconds)
}

function Remove-VerifiedTree([string]$Directory, [string]$ExpectedParent, [string]$NamePattern) {
    $resolved = [System.IO.Path]::GetFullPath($Directory).TrimEnd('\')
    if ([System.IO.Path]::GetDirectoryName($resolved) -ne $ExpectedParent.TrimEnd('\') -or
        [System.IO.Path]::GetFileName($resolved) -notmatch $NamePattern) { throw 'Refusing to remove an unrecognized update cache directory.' }
    Assert-NoLinks $resolved
    # Inspect the entire tree first, without following links. Delete each verified file/directory
    # individually, so a recursive filesystem operation can never cross a reparse point.
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $directories = [System.Collections.Generic.List[string]]::new()
    $deletions = [System.Collections.Generic.List[string]]::new()
    $pending.Push($resolved)
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        $directories.Add($current)
        foreach ($entry in [System.IO.Directory]::EnumerateFileSystemEntries($current)) {
            $attributes = [System.IO.File]::GetAttributes($entry)
            if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked cache entries cannot be removed automatically.' }
            if (($attributes -band [System.IO.FileAttributes]::Directory) -ne 0) { $pending.Push($entry) } else { $deletions.Add($entry) }
        }
    }
    foreach ($entry in $deletions) { [System.IO.File]::Delete($entry) }
    foreach ($entry in ($directories | Sort-Object Length -Descending)) { [System.IO.Directory]::Delete($entry,$false) }
}

try {
    Assert-NoLinks $logDirectory
    $job = Get-Content -LiteralPath $JobPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $install = [System.IO.Path]::GetFullPath([string]$job.InstallDirectory).TrimEnd('\')
    Assert-NoLinks $install
    if ($logDirectory.Equals($install,[System.StringComparison]::OrdinalIgnoreCase) -or $logDirectory.StartsWith($install+'\',[System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Update staging must be outside the application directory.'
    }
    if (![System.IO.File]::Exists((Join-Path $install 'ShelterType.exe'))) { throw 'ShelterType.exe was not found in the application folder.' }
    $validInstall = $true
    $package = [System.IO.Path]::GetFullPath([string]$job.PackagePath)
    if ($package -ne (Join-Path $logDirectory 'ShelterType-win-x64.zip')) { throw 'Unexpected update package location.' }
    Assert-NoLinks $package
    if ([string]$job.Sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid package checksum.' }
    if ((Get-Item -LiteralPath $package).Length -gt 268435456) { throw 'Update package is too large.' }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $packageStream = [System.IO.File]::Open($package, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $actualHash = [System.BitConverter]::ToString($sha.ComputeHash($packageStream)).Replace('-','') } finally { $sha.Dispose() }
    if ($actualHash -ne [string]$job.Sha256) { throw 'Update checksum verification failed.' }
    $packageStream.Position = 0
    $archive = [System.IO.Compression.ZipArchive]::new($packageStream,[System.IO.Compression.ZipArchiveMode]::Read,$true)
    if ($archive.Entries.Count -gt 20000) { throw 'Too many update package entries.' }
    $names = @{}
    $files = [System.Collections.Generic.List[object]]::new()
    [long]$expanded = 0
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.TrimEnd('/')
        Assert-PackagePath $name
        if ($names.ContainsKey($name)) { throw 'Duplicate update package entry.' }
        $isDirectory = $entry.FullName.EndsWith('/')
        $names.Add($name, $isDirectory)
        $unixType = ($entry.ExternalAttributes -shr 16) -band 0xf000
        if (($entry.ExternalAttributes -band 0x400) -ne 0 -or $unixType -notin @(0,0x8000,0x4000)) { throw 'Links or special files are not permitted in update packages.' }
        if ($entry.Length -lt 0 -or $entry.Length -gt (1073741824 - $expanded)) { throw 'Expanded update package is too large.' }
        $expanded += $entry.Length
        if (!$isDirectory) { $files.Add([pscustomobject]@{Name=$name;Entry=$entry;Existed=$false;Target=$null;Backup=$null}) }
    }
    foreach ($required in @('ShelterType.exe','ShelterType.dll','ShelterType.runtimeconfig.json')) {
        if (!$names.ContainsKey($required) -or $names[$required]) { throw "Update package is missing $required." }
    }
    foreach ($file in $files) {
        $parts = $file.Name.Split('/')
        for ($i=1; $i -lt $parts.Length; $i++) {
            $ancestor = ($parts[0..($i-1)] -join '/')
            if ($names.ContainsKey($ancestor) -and !$names[$ancestor]) { throw 'Update package path is both file and folder.' }
        }
    }
    $stage = Join-Path $logDirectory ('stage-' + [guid]::NewGuid().ToString('N'))
    $backup = Join-Path $logDirectory ('backup-' + [guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($stage) | Out-Null
    [System.IO.Directory]::CreateDirectory($backup) | Out-Null
    # Fully extract and check all target paths before changing any application file.
    foreach ($file in $files) {
        $staged = Get-ChildPath $stage $file.Name
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($staged)) | Out-Null
        $source = $file.Entry.Open()
        $destination = [System.IO.File]::Open($staged,[System.IO.FileMode]::CreateNew,[System.IO.FileAccess]::Write,[System.IO.FileShare]::None)
        try {
            $buffer = New-Object byte[] 81920
            [long]$written = 0
            while (($read = $source.Read($buffer,0,$buffer.Length)) -gt 0) {
                $written += $read
                if ($written -gt $file.Entry.Length) { throw 'Decompressed entry exceeds its declared size.' }
                $destination.Write($buffer,0,$read)
            }
            if ($written -ne $file.Entry.Length) { throw 'Decompressed entry length does not match the package.' }
        } finally { $destination.Dispose(); $source.Dispose() }
        $file.Target = Get-ChildPath $install $file.Name
        $file.Backup = Get-ChildPath $backup $file.Name
        Assert-NoLinks $file.Target
        if ([System.IO.Directory]::Exists($file.Target)) { throw "A folder conflicts with an update file: $($file.Name)" }
        $ancestor = [System.IO.Path]::GetDirectoryName($file.Target)
        while ($ancestor.Length -gt $install.Length) {
            if ([System.IO.File]::Exists($ancestor)) { throw 'An existing file conflicts with an update folder.' }
            $ancestor = [System.IO.Path]::GetDirectoryName($ancestor)
        }
    }
    $archive.Dispose(); $archive = $null
    $packageStream.Dispose(); $packageStream = $null
    if (!(Confirm-ParentExited 120000)) { throw 'ShelterType did not close in time. The update was not installed.' }
    $parentExited = $true
    Assert-NoLinks (Join-Path $install '.sheltertype-update.lock')
    $updateLock = [System.IO.File]::Open((Join-Path $install '.sheltertype-update.lock'),[System.IO.FileMode]::OpenOrCreate,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::None)
    $profileDirectory = Join-Path $install 'UserData'
    Assert-NoLinks $profileDirectory
    [System.IO.Directory]::CreateDirectory($profileDirectory) | Out-Null
    Assert-NoLinks (Join-Path $profileDirectory 'session.lock')
    $profileLock = [System.IO.File]::Open((Join-Path $profileDirectory 'session.lock'),[System.IO.FileMode]::OpenOrCreate,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::None)
    # Back up everything before beginning the commit. Keep backups for recovery after unexpected power loss.
    foreach ($file in $files) {
        Assert-NoLinks $file.Target
        $file.Existed = [System.IO.File]::Exists($file.Target)
        if ($file.Existed) {
            [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($file.Backup)) | Out-Null
            [System.IO.File]::Copy($file.Target, $file.Backup, $false)
        }
    }
    foreach ($file in $files) {
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($file.Target)) | Out-Null
        # Moving an existing file out first avoids partial overwrites if copying the replacement fails.
        if ($file.Existed) { [System.IO.File]::Delete($file.Target) }
        $applied.Add($file)
        [System.IO.File]::Copy((Get-ChildPath $stage $file.Name), $file.Target, $false)
    }
    $committed = $true
}
catch {
    $failure = $_.Exception.ToString()
    for ($i=$applied.Count-1; $i -ge 0; $i--) {
        $file = $applied[$i]
        try {
            if ([System.IO.File]::Exists($file.Target)) { [System.IO.File]::Delete($file.Target) }
            if ($file.Existed) { [System.IO.File]::Copy($file.Backup,$file.Target,$false) }
        }
        catch { $rollbackComplete = $false; $failure += "`r`nRollback: " + $_.Exception.ToString() }
    }
}
finally {
    if ($archive) { $archive.Dispose() }
    if ($packageStream) { $packageStream.Dispose() }
    if ($profileLock) { $profileLock.Dispose() }
    if ($updateLock) { $updateLock.Dispose() }
}

if (!$parentExited -and $job -and $validInstall) {
    # Validation/extraction can fail after the UI has already closed. Restart its previous
    # version only after independently confirming that the requesting instance has exited.
    try { $parentExited = Confirm-ParentExited 5000 } catch { }
}
if ($committed) {
    # Keep this update's backup and result. Successful older updates are redundant; failed
    # update directories are deliberately retained for diagnosis/manual recovery.
    try {
        Remove-VerifiedTree $stage $logDirectory '^stage-[0-9a-f]{32}$'
        Assert-NoLinks $package
        if ($package -eq (Join-Path $logDirectory 'ShelterType-win-x64.zip')) { [System.IO.File]::Delete($package) }
        $cacheRoot = [System.IO.Path]::GetDirectoryName($logDirectory)
        foreach ($oldDirectory in [System.IO.Directory]::EnumerateDirectories($cacheRoot)) {
            if ($oldDirectory -eq $logDirectory -or [System.IO.Path]::GetFileName($oldDirectory) -notmatch '^update-[0-9a-f]{32}$') { continue }
            Assert-NoLinks $oldDirectory
            $oldResultPath = Join-Path $oldDirectory 'result.json'
            $oldJobPath = Join-Path $oldDirectory 'install-job.json'
            Assert-NoLinks $oldResultPath; Assert-NoLinks $oldJobPath
            if (![System.IO.File]::Exists($oldResultPath) -or ![System.IO.File]::Exists($oldJobPath)) { continue }
            $oldResult = Get-Content -LiteralPath $oldResultPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $oldJob = Get-Content -LiteralPath $oldJobPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($oldResult.Success -eq $true -and [System.IO.Path]::GetFullPath([string]$oldJob.InstallDirectory).TrimEnd('\') -eq $install) {
                Remove-VerifiedTree $oldDirectory $cacheRoot '^update-[0-9a-f]{32}$'
            }
        }
    } catch { try { $_.Exception.ToString() | Set-Content -LiteralPath (Join-Path $logDirectory 'cleanup-error.txt') -Encoding UTF8 } catch { } }
}
try {
    [pscustomobject]@{Success=$committed;RollbackComplete=$rollbackComplete;ParentExited=$parentExited;Error=$failure;FinishedUtc=[DateTime]::UtcNow.ToString('O')} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logDirectory 'result.json') -Encoding UTF8
} catch { }
if ($job -and $job.Restart) {
    if (!$committed) {
        try {
            Add-Type -AssemblyName System.Windows.Forms
            $message = [string]$job.FailureIntro + ' '
            if ($rollbackComplete) { $message += [string]$job.KeptMessage } else { $message += [string]$job.RecoveryMessage }
            $message += "`r`n`r`n" + [string]$job.LogLabel + "`r`n" + $logDirectory
            [System.Windows.Forms.MessageBox]::Show($message, 'ShelterType', 'OK', 'Warning') | Out-Null
        } catch { }
    }
    if ($parentExited -and $rollbackComplete) {
        # The updater stays hidden; the interactive application must reopen visibly.
        try { Start-Process -FilePath (Join-Path $install 'ShelterType.exe') -WorkingDirectory $install -WindowStyle Normal }
        catch {
            try { $_.Exception.ToString() | Set-Content -LiteralPath (Join-Path $logDirectory 'restart-error.txt') -Encoding UTF8 } catch { }
        }
    }
}
if ($committed) { exit 0 } else { exit 1 }
