param(
    [string] $Ffmpeg,
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = $(if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') {
        'win-arm64'
    } else { 'win-x64' }),
    [switch] $Ui
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
$dotnet = Join-Path $root 'tools\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $installed = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $installed) { throw 'Install the .NET 8 SDK to run tests.' }
    $dotnet = $installed.Source
}
if (-not $Ffmpeg) {
    $Ffmpeg = Join-Path $root 'tools\ffmpeg\ffmpeg.exe'
    if (-not (Test-Path $Ffmpeg)) {
        $installed = Get-Command ffmpeg -ErrorAction SilentlyContinue
        if (-not $installed) { throw 'Provide -Ffmpeg with the path to FFmpeg (fixture generation only).' }
        $Ffmpeg = $installed.Source
    }
}
if (-not (Test-Path $Ffmpeg)) { throw "FFmpeg was not found: $Ffmpeg" }
$architecture = if ($Runtime -eq 'win-arm64') { 'aarch64' } else { 'x86_64' }
if (-not (Test-Path (Join-Path $root "native\$architecture\libmpv-2.dll"))) {
    throw "Run .\build.ps1 -Runtime $Runtime first to obtain the pinned playback library."
}
$fixtures = Join-Path $root 'artifacts\fixtures'
New-Item -ItemType Directory -Force $fixtures | Out-Null
$normal = Join-Path $fixtures 'frame test.mp4'
$renamed = Join-Path $fixtures 'renamed.mp4'
$variable = Join-Path $fixtures 'variable.mp4'
& $Ffmpeg -hide_banner -loglevel error -y `
    -f lavfi -i 'testsrc2=size=320x180:rate=25:duration=4' `
    -f lavfi -i 'sine=frequency=440:duration=4' `
    -c:v libx264 -g 50 -bf 3 -pix_fmt yuv420p -c:a aac -shortest -movflags +faststart $normal
if ($LASTEXITCODE -ne 0) { throw 'MP4 fixture generation failed.' }
& $Ffmpeg -hide_banner -loglevel error -y -i $normal -c copy -f matroska $renamed
if ($LASTEXITCODE -ne 0) { throw 'Invalid-container fixture generation failed.' }
& $Ffmpeg -hide_banner -loglevel error -y `
    -f lavfi -i 'testsrc2=size=320x180:rate=25:duration=4' `
    -vf 'select=not(eq(mod(n\,3)\,2))' -fps_mode vfr -c:v libx264 -g 50 -bf 3 -pix_fmt yuv420p $variable
if ($LASTEXITCODE -ne 0) { throw 'Variable-frame-rate fixture generation failed.' }
$testProject = Join-Path $root 'tests\Win4DaveMediaPlayer.Tests.csproj'
& $dotnet build $testProject -c Release -r $Runtime -p:SelfContained=true -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
$arguments = @($normal, $renamed, $variable)
if ($Ui) { $arguments += '--ui' }
& (Join-Path $root "tests\bin\Release\net8.0-windows\$Runtime\Win4DaveMediaPlayer.Tests.exe") @arguments
if ($LASTEXITCODE -ne 0) { throw 'Playback tests failed.' }
& $dotnet format whitespace $testProject --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Formatting verification failed.' }
