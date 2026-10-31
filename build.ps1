param(
    [ValidateSet('all', 'win-x64', 'win-arm64')]
    [string] $Runtime = 'all'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
$dotnet = Join-Path $root 'tools\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $installed = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $installed) { throw 'Install the .NET 8 SDK to build this project.' }
    $dotnet = $installed.Source
}

$libraries = @{
    'win-arm64' = @{
        Architecture = 'aarch64'
        ArchiveHash = '015F133399B3525021B6D937CF54ACE2761D4F70224D5E62F5DF24191B28F398'
        LibraryHash = 'F90CEE7E3DBB0A12FBFA6B95AF1FB2BB22DBFEBF8422EFC0324918EE75661850'
    }
    'win-x64' = @{
        Architecture = 'x86_64'
        ArchiveHash = 'EFC719EFF3DC5954B4A5EFC800632808BCEC01CC9034940DD14E5EB7FE9EA633'
        LibraryHash = '04E4D27DC385DC18DDD93FBE69C75E635B864F3C6EC53A9122DF54C2F7842F4A'
    }
}
$runtimes = if ($Runtime -eq 'all') { @('win-x64', 'win-arm64') } else { @($Runtime) }
$notices = @(
    'README.txt', 'THIRD-PARTY-NOTICES.txt', 'LICENSE.txt', 'MPV-GPL.txt',
    'MPV-LGPL.txt', 'MPV-COPYRIGHT.txt', 'DOTNET-LICENSE.txt', 'DOTNET-THIRD-PARTY-NOTICES.txt'
)

foreach ($rid in $runtimes) {
    $spec = $libraries[$rid]
    $native = Join-Path $root "native\$($spec.Architecture)"
    $dll = Join-Path $native 'libmpv-2.dll'
    if (-not (Test-Path $dll)) {
        New-Item -ItemType Directory -Force $native, (Join-Path $root 'tools') | Out-Null
        $archive = Join-Path $root "tools\$($spec.Architecture).7z"
        $url = "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261021/mpv-dev-$($spec.Architecture)-20261021-git-e471496c56.7z"
        if (-not (Test-Path $archive)) {
            Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $archive
        }
        if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $spec.ArchiveHash) {
            throw "Checksum mismatch for $archive. Remove this named archive and retry."
        }
        & tar.exe -xf $archive -C $native
        if ($LASTEXITCODE -ne 0) { throw "Unable to extract $archive using Windows tar.exe." }
    }
    if ((Get-FileHash $dll -Algorithm SHA256).Hash -ne $spec.LibraryHash) {
        throw "Checksum mismatch for $dll. The pinned playback library is required."
    }
    $output = Join-Path $root "artifacts\$rid"
    & $dotnet publish (Join-Path $root 'src\Win4DaveMediaPlayer.csproj') -c Release -r $rid `
        --self-contained true -p:PublishSingleFile=true -warnaserror -o $output
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid." }
    foreach ($notice in $notices) {
        Copy-Item (Join-Path $root $notice) $output -Force
    }
    $zip = Join-Path $root "artifacts\Win4DaveMediaPlayer-$rid.zip"
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -Force
    Write-Host "Created $zip"
}

$source = Join-Path $root 'artifacts\source'
foreach ($directory in @('src', 'tests')) {
    $destination = Join-Path $source $directory
    New-Item -ItemType Directory -Force $destination | Out-Null
    Get-ChildItem (Join-Path $root $directory) -File |
        Copy-Item -Destination $destination -Force
}
foreach ($file in $notices + @('build.ps1', 'test.ps1', '.gitignore')) {
    Copy-Item (Join-Path $root $file) $source -Force
}
Compress-Archive -Path (Join-Path $source '*') `
    -DestinationPath (Join-Path $root 'artifacts\Win4DaveMediaPlayer-source.zip') -Force
