[CmdletBinding()]
param(
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()
$checkCount = 0
$temporaryRoots = [System.Collections.Generic.List[string]]::new()
$temporaryParent = [System.IO.Path]::GetFullPath(
    [System.IO.Path]::GetTempPath()).TrimEnd('\')

function Add-Check {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [bool]$Passed,

        [Parameter(Mandatory)]
        [string]$FailureMessage
    )

    $script:checkCount++
    if (-not $Passed) {
        [void]$script:failures.Add(("{0}: {1}" -f $Name, $FailureMessage))
    }
}

function Find-ZipEntry {
    param(
        [Parameter(Mandatory)]
        [System.IO.Compression.ZipArchive]$Archive,

        [Parameter(Mandatory)]
        [string]$Name
    )

    $entry = $Archive.GetEntry($Name)
    if ($null -ne $entry) {
        return $entry
    }

    foreach ($candidate in $Archive.Entries) {
        if ($candidate.FullName.Trim('/').Equals(
                $Name,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            return $candidate
        }
    }

    return $null
}

function Read-ZipEntryText {
    param(
        [Parameter(Mandatory)]
        [System.IO.Compression.ZipArchiveEntry]$Entry
    )

    $stream = $Entry.Open()
    try {
        $reader = [System.IO.StreamReader]::new(
            $stream,
            [System.Text.Encoding]::UTF8,
            $true)
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Read-ZipEntryBytes {
    param(
        [Parameter(Mandatory)]
        [System.IO.Compression.ZipArchiveEntry]$Entry
    )

    $stream = $Entry.Open()
    $buffer = [System.IO.MemoryStream]::new()
    try {
        $stream.CopyTo($buffer)
        return $buffer.ToArray()
    }
    finally {
        $buffer.Dispose()
        $stream.Dispose()
    }
}

function Copy-ZipEntryToTemporaryFile {
    param(
        [Parameter(Mandatory)]
        [System.IO.Compression.ZipArchiveEntry]$Entry,

        [Parameter(Mandatory)]
        [string]$LeafName
    )

    $temporaryRoot = Join-Path `
        ([System.IO.Path]::GetTempPath()) `
        ("WinQuickSwitch-release-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    [void]$script:temporaryRoots.Add($temporaryRoot)

    $temporaryFile = Join-Path $temporaryRoot $LeafName
    $archiveStream = $Entry.Open()
    $fileStream = [System.IO.File]::Open(
        $temporaryFile,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $archiveStream.CopyTo($fileStream)
    }
    finally {
        $fileStream.Dispose()
        $archiveStream.Dispose()
    }

    return $temporaryFile
}

function Get-ZipEntrySha256 {
    param(
        [Parameter(Mandatory)]
        [System.IO.Compression.ZipArchiveEntry]$Entry
    )

    $hashAlgorithm = [System.Security.Cryptography.SHA256]::Create()
    $stream = $Entry.Open()
    try {
        return ([System.BitConverter]::ToString(
            $hashAlgorithm.ComputeHash($stream))).Replace('-', '')
    }
    finally {
        $stream.Dispose()
        $hashAlgorithm.Dispose()
    }
}

function Get-NodeValue {
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlNode]$Document,

        [Parameter(Mandatory)]
        [string]$XPath
    )

    $node = $Document.SelectSingleNode($XPath)
    if ($null -eq $node) {
        return $null
    }

    $value = if ($node -is [System.Xml.XmlAttribute]) {
        $node.Value
    }
    else {
        $node.InnerText
    }
    if ($null -eq $value) {
        return $null
    }

    return ([string]$value).Trim()
}

function Test-ContainsMetadataMarker {
    param(
        [Parameter(Mandatory)]
        [byte[]]$Bytes,

        [Parameter(Mandatory)]
        [string]$Marker
    )

    return [System.Text.Encoding]::UTF8.GetString($Bytes).Contains($Marker)
}

function Get-FileVersion {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    try {
        $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path).FileVersion
        if ($null -eq $fileVersion) {
            return $null
        }

        return $fileVersion.Trim()
    }
    catch {
        return $null
    }
}

function Validate-StoreEdition {
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Edition
    )

    $editionRoot = Join-Path `
        (Join-Path (Join-Path $repositoryRoot 'artifacts\store-msix') $Edition.DirectoryName) `
        ("v" + $Version)
    $msixPath = Join-Path $editionRoot ($Edition.ArtifactStem + "_" + $Version + '_x64.msix')
    $uploadPath = Join-Path $editionRoot ($Edition.ArtifactStem + "_" + $Version + '_x64.msixupload')

    $msixExists = Test-Path -LiteralPath $msixPath -PathType Leaf
    Add-Check `
        "$($Edition.Name) MSIX exists" `
        $msixExists `
        "Missing $msixPath"

    $uploadExists = Test-Path -LiteralPath $uploadPath -PathType Leaf
    Add-Check `
        "$($Edition.Name) MSIX upload exists" `
        $uploadExists `
        "Missing $uploadPath"

    $standaloneHash = $null
    if ($msixExists) {
        $standaloneHash = (Get-FileHash -LiteralPath $msixPath -Algorithm SHA256).Hash
        $archive = $null
        try {
            $archive = [System.IO.Compression.ZipFile]::OpenRead($msixPath)

            $manifestEntry = Find-ZipEntry $archive 'AppxManifest.xml'
            Add-Check `
                "$($Edition.Name) MSIX manifest exists" `
                ($null -ne $manifestEntry) `
                "$msixPath does not contain AppxManifest.xml"

            if ($null -ne $manifestEntry) {
                $manifest = [xml](Read-ZipEntryText $manifestEntry)
                $identityName = Get-NodeValue `
                    $manifest `
                    "/*[local-name()='Package']/*[local-name()='Identity']/@Name"
                $publisher = Get-NodeValue `
                    $manifest `
                    "/*[local-name()='Package']/*[local-name()='Identity']/@Publisher"
                $manifestVersion = Get-NodeValue `
                    $manifest `
                    "/*[local-name()='Package']/*[local-name()='Identity']/@Version"
                $displayName = Get-NodeValue `
                    $manifest `
                    "/*[local-name()='Package']/*[local-name()='Properties']/*[local-name()='DisplayName']"
                $publisherDisplayName = Get-NodeValue `
                    $manifest `
                    "/*[local-name()='Package']/*[local-name()='Properties']/*[local-name()='PublisherDisplayName']"
                $visualDisplayName = Get-NodeValue `
                    $manifest `
                    "/*[local-name()='Package']/*[local-name()='Applications']/*[local-name()='Application']/*[local-name()='VisualElements']/@DisplayName"

                Add-Check `
                    "$($Edition.Name) manifest identity" `
                    ($identityName -eq $Edition.IdentityName) `
                    "expected '$($Edition.IdentityName)', got '$identityName'"
                Add-Check `
                    "$($Edition.Name) manifest publisher" `
                    ($publisher -eq $Edition.Publisher) `
                    "expected '$($Edition.Publisher)', got '$publisher'"
                Add-Check `
                    "$($Edition.Name) manifest version" `
                    ($manifestVersion -eq $Version) `
                    "expected '$Version', got '$manifestVersion'"
                Add-Check `
                    "$($Edition.Name) manifest display name" `
                    ($displayName -eq $Edition.DisplayName) `
                    "expected '$($Edition.DisplayName)', got '$displayName'"
                Add-Check `
                    "$($Edition.Name) manifest publisher display name" `
                    ($publisherDisplayName -eq $Edition.PublisherDisplayName) `
                    "expected '$($Edition.PublisherDisplayName)', got '$publisherDisplayName'"
                Add-Check `
                    "$($Edition.Name) visual display name" `
                    ($visualDisplayName -eq $Edition.DisplayName) `
                    "expected '$($Edition.DisplayName)', got '$visualDisplayName'"
            }

            $packagedExe = Find-ZipEntry $archive 'WinQuickSwitch.exe'
            Add-Check `
                "$($Edition.Name) packaged executable exists" `
                ($null -ne $packagedExe) `
                "$msixPath does not contain WinQuickSwitch.exe"
            if ($null -ne $packagedExe) {
                $temporaryExe = Copy-ZipEntryToTemporaryFile `
                    $packagedExe `
                    "$($Edition.DirectoryName)-WinQuickSwitch.exe"
                $packagedVersion = Get-FileVersion $temporaryExe
                Add-Check `
                    "$($Edition.Name) packaged executable FileVersion" `
                    ($packagedVersion -eq $Version) `
                    "expected '$Version', got '$packagedVersion'"
            }

            $productionDll = Find-ZipEntry $archive 'WinQuickSwitch.dll'
            Add-Check `
                "$($Edition.Name) production DLL exists" `
                ($null -ne $productionDll) `
                "$msixPath does not contain WinQuickSwitch.dll"
            if ($null -ne $productionDll) {
                $dllBytes = Read-ZipEntryBytes $productionDll
                $hasE2eMarker = Test-ContainsMetadataMarker `
                    $dllBytes `
                    'SandboxE2eServices'
                Add-Check `
                    "$($Edition.Name) production DLL excludes SandboxE2eServices" `
                    (-not $hasE2eMarker) `
                    "$msixPath contains the SandboxE2eServices metadata marker"
            }
        }
        catch {
            Add-Check `
                "$($Edition.Name) MSIX readable" `
                $false `
                $_.Exception.Message
        }
        finally {
            if ($null -ne $archive) {
                $archive.Dispose()
            }
        }
    }

    if ($uploadExists) {
        $uploadArchive = $null
        try {
            $uploadArchive = [System.IO.Compression.ZipFile]::OpenRead($uploadPath)
            $innerPackages = @($uploadArchive.Entries | Where-Object {
                    -not $_.FullName.EndsWith('/') -and
                    $_.FullName.EndsWith('.msix', [System.StringComparison]::OrdinalIgnoreCase)
                })
            Add-Check `
                "$($Edition.Name) MSIX upload has one inner package" `
                ($innerPackages.Count -eq 1) `
                "expected 1 .msix entry, got $($innerPackages.Count)"

            if ($innerPackages.Count -eq 1 -and $null -ne $standaloneHash) {
                $innerHash = Get-ZipEntrySha256 $innerPackages[0]
                Add-Check `
                    "$($Edition.Name) MSIX upload package hash" `
                    ($innerHash -eq $standaloneHash) `
                    "expected standalone $standaloneHash, got inner $innerHash"
            }
        }
        catch {
            Add-Check `
                "$($Edition.Name) MSIX upload readable" `
                $false `
                $_.Exception.Message
        }
        finally {
            if ($null -ne $uploadArchive) {
                $uploadArchive.Dispose()
            }
        }
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $propsPath = Join-Path $repositoryRoot 'Directory.Build.props'
        if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
            Add-Check 'Release version source' $false "Missing $propsPath"
        }
        else {
            [xml]$props = Get-Content -LiteralPath $propsPath -Raw
            $versionNode = $props.SelectSingleNode(
                "/Project/PropertyGroup/WinQuickSwitchReleaseVersion")
            if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
                Add-Check 'Release version source' $false `
                    'Directory.Build.props has no WinQuickSwitchReleaseVersion value'
            }
            else {
                $Version = $versionNode.InnerText.Trim()
            }
        }
    }

    $versionIsValid = $Version -match '^\d{1,5}\.\d{1,5}\.\d{1,5}\.0$'
    Add-Check `
        'Release version format' `
        $versionIsValid `
        "Expected four numeric components with revision 0, got '$Version'"

    if ($versionIsValid) {
        foreach ($standalone in @(
                [pscustomobject]@{
                    Name = 'Free Lite'
                    Path = Join-Path $repositoryRoot ("artifacts\win-x64-free-lite-$Version\WinQuickSwitch.exe")
                }
                [pscustomobject]@{
                    Name = 'Free Portable'
                    Path = Join-Path $repositoryRoot ("artifacts\win-x64-free-portable-$Version\WinQuickSwitch.exe")
                }
                [pscustomobject]@{
                    Name = 'Paid Lite'
                    Path = Join-Path $repositoryRoot ("artifacts\win-x64-paid-lite-$Version\WinQuickSwitch.exe")
                }
                [pscustomobject]@{
                    Name = 'Paid Portable'
                    Path = Join-Path $repositoryRoot ("artifacts\win-x64-paid-portable-$Version\WinQuickSwitch.exe")
                })) {
            $exists = Test-Path -LiteralPath $standalone.Path -PathType Leaf
            Add-Check `
                "$($standalone.Name) executable exists" `
                $exists `
                "Missing $($standalone.Path)"
            if ($exists) {
                $fileVersion = Get-FileVersion $standalone.Path
                Add-Check `
                    "$($standalone.Name) executable FileVersion" `
                    ($fileVersion -eq $Version) `
                    "expected '$Version', got '$fileVersion'"
            }
        }

        Validate-StoreEdition ([pscustomobject]@{
                Name = 'Paid'
                DirectoryName = 'paid'
                ArtifactStem = 'WinQuickSwitch'
                IdentityName = 'Dreamle.WinQuickSwitch'
                Publisher = 'CN=E047B488-2EDF-444A-8C22-4FF1BD29B2B8'
                DisplayName = 'WinQuickSwitch'
                PublisherDisplayName = 'Dreamle'
            })
        Validate-StoreEdition ([pscustomobject]@{
                Name = 'Free'
                DirectoryName = 'free'
                ArtifactStem = 'WinQuickSwitch-Free'
                IdentityName = 'Dreamle.WinQuickSwitch-Free'
                Publisher = 'CN=E047B488-2EDF-444A-8C22-4FF1BD29B2B8'
                DisplayName = 'WinQuickSwitch - Free'
                PublisherDisplayName = 'Dreamle'
            })
    }
}
catch {
    Add-Check 'Release artifact validator execution' $false $_.Exception.Message
}
finally {
    foreach ($temporaryRoot in @($temporaryRoots)) {
        $resolvedRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
        $resolvedParent = [System.IO.Path]::GetDirectoryName($resolvedRoot)
        $rootName = [System.IO.Path]::GetFileName($resolvedRoot)
        $safeTemporaryRoot =
            $null -ne $resolvedParent -and
            $resolvedParent.TrimEnd('\').Equals(
                $temporaryParent,
                [System.StringComparison]::OrdinalIgnoreCase) -and
            $rootName -match '^WinQuickSwitch-release-[0-9a-f]{32}$'
        if ($safeTemporaryRoot -and (Test-Path -LiteralPath $resolvedRoot)) {
            Remove-Item -LiteralPath $resolvedRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$failureCount = $failures.Count
$passedCount = $checkCount - $failureCount
Write-Host ("Release artifact validation: $passedCount checks passed, $failureCount failed (version $Version).")
if ($failureCount -gt 0) {
    foreach ($failure in $failures) {
        Write-Host "FAIL $failure"
    }
    exit 1
}

exit 0
