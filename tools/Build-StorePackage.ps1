[CmdletBinding()]
param(
    [ValidatePattern('^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$')]
    [string]$Version = '1.0.0.1'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot 'artifacts\store-msix'
$stageDirectory = Join-Path $artifactRoot 'staging'
$uploadDirectory = Join-Path $artifactRoot 'upload'
$projectPath = Join-Path $repoRoot 'src\WinQuickSwitch\WinQuickSwitch.csproj'
$manifestTemplate = Join-Path $repoRoot 'store\AppxManifest.xml'
$assetSource = Join-Path $repoRoot 'store\Assets'
$dotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
$assetGenerator = Join-Path $PSScriptRoot 'Generate-StoreAssets.ps1'

$expectedIdentityName = 'Dreamle.WinQuickSwitch'
$expectedPublisher = 'CN=E047B488-2EDF-444A-8C22-4FF1BD29B2B8'
$expectedFamilyName = 'Dreamle.WinQuickSwitch_sth8w7gs4yt8p'

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
    '--no-restore',
    '--output', $stageDirectory,
    '-p:PublishSingleFile=false',
    '-p:PublishTrimmed=false',
    '-p:PublishReadyToRun=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)

& $dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "Store publish failed with exit code $LASTEXITCODE."
}

[xml]$manifest = Get-Content -LiteralPath $manifestTemplate -Raw
$manifestIdentity = $manifest.Package.Identity

if ($manifestIdentity.Name -ne $expectedIdentityName) {
    throw "Package identity must match Partner Center: $expectedIdentityName"
}

if ($manifestIdentity.Publisher -ne $expectedPublisher) {
    throw "Package publisher must match Partner Center: $expectedPublisher"
}

$manifest.Package.Identity.Version = $Version
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

$packageName = "WinQuickSwitch_${Version}_x64.msix"
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

$uploadBaseName = "WinQuickSwitch_${Version}_x64"
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
Write-Host "Expected family name: $expectedFamilyName"
Write-Host "Version: $($manifestIdentity.Version)"
Write-Host "Package: $packagePath"
Write-Host "Package SHA-256: $($packageHash.Hash)"
Write-Host "Upload: $uploadPath"
Write-Host "Upload SHA-256: $($uploadHash.Hash)"
Write-Host 'Signing: unsigned; Microsoft Store signs the accepted submission.'
