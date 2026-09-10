[CmdletBinding()]
param(
    [ValidatePattern('^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$')]
    [string]$Version,

    [ValidateSet('Paid', 'Free')]
    [string]$Edition = 'Paid',

    [string]$IdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [string]$ExpectedFamilyName
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    $propsPath = Join-Path $repoRoot 'Directory.Build.props'
    if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
        throw "Directory.Build.props was not found at $propsPath"
    }

    try {
        [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    }
    catch {
        throw "Directory.Build.props could not be parsed: $($_.Exception.Message)"
    }

    $versionNode = $props.SelectSingleNode(
        "/Project/PropertyGroup/WinQuickSwitchReleaseVersion")
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
        throw 'Directory.Build.props has no WinQuickSwitchReleaseVersion value'
    }

    $Version = $versionNode.InnerText.Trim()
}

if ($Version -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$') {
    throw "Version must contain four numeric components, got '$Version'."
}

$versionParts = @($Version.Split('.') | ForEach-Object { [uint32]$_ })

if (@($versionParts | Where-Object { $_ -gt 65535 }).Count -gt 0) {
    throw 'Every MSIX version component must be between 0 and 65535.'
}

if ($versionParts[3] -ne 0) {
    throw ('Microsoft Store requires the fourth version component to be 0. ' +
        'Increment the third component for a new Store package instead.')
}

$artifactRoot = Join-Path $repoRoot ("artifacts\store-msix\" + $Edition.ToLowerInvariant() + "\v" + $Version)
$stageDirectory = Join-Path $artifactRoot 'staging'
$uploadDirectory = Join-Path $artifactRoot 'upload'
$projectPath = Join-Path $repoRoot 'src\WinQuickSwitch\WinQuickSwitch.csproj'
$manifestTemplate = Join-Path $repoRoot 'store\AppxManifest.xml'
$assetSource = Join-Path $repoRoot 'store\Assets'
$dotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
$assetGenerator = Join-Path $PSScriptRoot 'Generate-StoreAssets.ps1'

$paidIdentityName = 'Dreamle.WinQuickSwitch'
$paidPublisher = 'CN=E047B488-2EDF-444A-8C22-4FF1BD29B2B8'
$paidFamilyName = 'Dreamle.WinQuickSwitch_sth8w7gs4yt8p'
$freeIdentityName = 'Dreamle.WinQuickSwitch-Free'
$freePublisher = 'CN=E047B488-2EDF-444A-8C22-4FF1BD29B2B8'
$freeFamilyName = 'Dreamle.WinQuickSwitch-Free_sth8w7gs4yt8p'

if ($Edition -eq 'Paid') {
    if ([string]::IsNullOrWhiteSpace($IdentityName)) {
        $IdentityName = $paidIdentityName
    }

    if ([string]::IsNullOrWhiteSpace($Publisher)) {
        $Publisher = $paidPublisher
    }

    if ([string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
        $PublisherDisplayName = 'Dreamle'
    }

    if ([string]::IsNullOrWhiteSpace($ExpectedFamilyName)) {
        $ExpectedFamilyName = $paidFamilyName
    }

    if ($IdentityName -ne $paidIdentityName -or $Publisher -ne $paidPublisher) {
        throw 'The existing Paid package identity and publisher must not change.'
    }

    $displayName = 'WinQuickSwitch'
    $description = 'Quick Windows display, audio, device, favorite, and profile controls.'
    $artifactStem = 'WinQuickSwitch'
}
else {
    if ([string]::IsNullOrWhiteSpace($IdentityName)) {
        $IdentityName = $freeIdentityName
    }

    if ([string]::IsNullOrWhiteSpace($Publisher)) {
        $Publisher = $freePublisher
    }

    if ([string]::IsNullOrWhiteSpace($ExpectedFamilyName)) {
        $ExpectedFamilyName = $freeFamilyName
    }

    if ($IdentityName -ne $freeIdentityName -or $Publisher -ne $freePublisher) {
        throw 'The Free package identity and publisher must match the reserved Partner Center product.'
    }

    if ([string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
        $PublisherDisplayName = 'Dreamle'
    }

    $displayName = 'WinQuickSwitch - Free'
    $description = 'Quick Windows display, audio, device, favorite, and keybind controls.'
    $artifactStem = 'WinQuickSwitch-Free'
}

function Reset-ArtifactDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $safeRoot = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)

    if (-not $resolvedPath.StartsWith(
        $safeRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a directory outside $safeRoot"
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $resolvedPath -Force | Out-Null
}

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "The repository-local .NET SDK was not found at $dotnet"
}

$makeAppx = Get-ChildItem `
    -Path 'C:\Program Files (x86)\Windows Kits\10\bin' `
    -Recurse `
    -Filter 'makeappx.exe' `
    -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq 'x64' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $makeAppx) {
    throw 'MakeAppx.exe was not found in the installed Windows SDK.'
}

& $assetGenerator

Reset-ArtifactDirectory -Path $stageDirectory
Reset-ArtifactDirectory -Path $uploadDirectory

$publishArguments = @(
    'publish',
    $projectPath,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--output', $stageDirectory,
    '-p:PublishSingleFile=false',
    '-p:PublishTrimmed=false',
    '-p:PublishReadyToRun=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-p:WinQuickSwitchE2E=false',
    "-p:WinQuickSwitchReleaseVersion=$Version",
    "-p:WinQuickSwitchEdition=$Edition"
)

& $dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "Store publish failed with exit code $LASTEXITCODE."
}

[xml]$manifest = Get-Content -LiteralPath $manifestTemplate -Raw
$manifestIdentity = $manifest.Package.Identity
$manifestIdentity.Name = $IdentityName
$manifestIdentity.Publisher = $Publisher

if ($manifestIdentity.Name -ne $IdentityName) {
    throw "Package identity must match the selected edition: $IdentityName"
}

if ($manifestIdentity.Publisher -ne $Publisher) {
    throw "Package publisher must match the selected edition: $Publisher"
}

$manifest.Package.Identity.Version = $Version
$manifest.Package.Properties.DisplayName = $displayName
$manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
$manifest.Package.Properties.Description = $description
$visualElements = $manifest.SelectSingleNode(
    "/*[local-name()='Package']/*[local-name()='Applications']/*[local-name()='Application']/*[local-name()='VisualElements']")

if (-not $visualElements) {
    throw 'The Store manifest is missing uap:VisualElements.'
}

$visualElements.SetAttribute('DisplayName', $displayName)
$visualElements.SetAttribute('Description', $description)
$manifestPath = Join-Path $stageDirectory 'AppxManifest.xml'
$xmlSettings = [System.Xml.XmlWriterSettings]::new()
$xmlSettings.Indent = $true
$xmlSettings.Encoding = [System.Text.UTF8Encoding]::new($false)

$writer = [System.Xml.XmlWriter]::Create($manifestPath, $xmlSettings)
try {
    $manifest.Save($writer)
}
finally {
    $writer.Dispose()
}

Copy-Item `
    -LiteralPath $assetSource `
    -Destination (Join-Path $stageDirectory 'Assets') `
    -Recurse `
    -Force

$packageName = "${artifactStem}_${Version}_x64.msix"
$packagePath = Join-Path $artifactRoot $packageName

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

& $makeAppx pack /d $stageDirectory /p $packagePath /o
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed with exit code $LASTEXITCODE."
}

$uploadPackagePath = Join-Path $uploadDirectory $packageName
Copy-Item -LiteralPath $packagePath -Destination $uploadPackagePath

$uploadBaseName = "${artifactStem}_${Version}_x64"
$zipPath = Join-Path $artifactRoot "${uploadBaseName}.zip"
$uploadPath = Join-Path $artifactRoot "${uploadBaseName}.msixupload"

foreach ($oldUpload in @($zipPath, $uploadPath)) {
    if (Test-Path -LiteralPath $oldUpload) {
        Remove-Item -LiteralPath $oldUpload -Force
    }
}

Compress-Archive `
    -LiteralPath $uploadPackagePath `
    -DestinationPath $zipPath `
    -CompressionLevel Optimal
Move-Item -LiteralPath $zipPath -Destination $uploadPath

$packageHash = Get-FileHash -LiteralPath $packagePath -Algorithm SHA256
$uploadHash = Get-FileHash -LiteralPath $uploadPath -Algorithm SHA256

Write-Host ''
Write-Host 'Store package created successfully.'
Write-Host "Identity: $($manifestIdentity.Name)"
Write-Host "Publisher: $($manifestIdentity.Publisher)"
if ([string]::IsNullOrWhiteSpace($ExpectedFamilyName)) {
    Write-Host 'Expected family name: not supplied (obtain it from Partner Center after reservation)'
}
else {
    Write-Host "Expected family name: $ExpectedFamilyName"
}
Write-Host "Edition: $Edition"
Write-Host "Version: $($manifestIdentity.Version)"
Write-Host "Package: $packagePath"
Write-Host "Package SHA-256: $($packageHash.Hash)"
Write-Host "Upload: $uploadPath"
Write-Host "Upload SHA-256: $($uploadHash.Hash)"
Write-Host 'Signing: unsigned; Microsoft Store signs the accepted submission.'
