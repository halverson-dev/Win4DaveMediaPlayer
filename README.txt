Win4DaveMediaPlayer
==================

A portable Windows MP4 video player for close, frame-by-frame review.

RUN
Extract the matching ZIP and double-click Win4DaveMediaPlayer.exe:
  win-x64: Intel/AMD 64-bit Windows 10 or 11
  win-arm64: ARM64 Windows 11

No installer, separate .NET runtime, codec pack, mpv, or FFmpeg installation
is needed to run the application. The executable includes its playback engine
and runtime, and extracts native libraries into the Windows temporary directory
on first launch. This needs available temporary disk space and write permission.
The executable is unsigned; Windows may show a SmartScreen warning.

PLAYBACK
Open MP4... or Ctrl+O opens a local MP4 file. A file can also be dropped onto
the window, or supplied as the first command-line argument. Videos open paused.

Play / Space: play or pause, with audio.
Previous frame / Left / comma: pause and show the preceding decoded frame.
Next frame / Right / period: pause and show the next decoded frame.
Home: return to the video start, or the selected interval start.
10% speed (0.1x): play at one tenth of normal speed; uncheck for normal speed.
Mute: silence audio. Audio pitch correction is enabled during slow playback.
The timeline seeks within the selected interval, or the full video.
Keyboard shortcuts do not interfere with typing times into the input boxes.

INTERVALS
Enter Start and End using seconds, mm:ss.mmm, or hh:mm:ss.mmm.
Examples: 12.5, 02:12.500, 00:02:12.500.
Set start here / Set end here copy the currently displayed timestamp.
Apply interval validates 0 <= start < end <= video duration, pauses, and seeks
to the start. Press Play to begin. Playback stops at the end and stays paused;
pressing Play again restarts the interval. Enable Repeat interval to loop it.
Clear interval pauses and restores access to the whole video.
Opening another video clears the interval. Only local .mp4 files are accepted;
files containing another container format or no playable video are rejected.

TIMING
Frame stepping uses decoded video timestamps, not a fixed frame-rate estimate,
including variable-frame-rate files. Interval limits are handled inside the
playback engine, not by a UI timer. Video frames are indivisible: non-frame-aligned
limits resolve to available frames. Reverse stepping may need to decode from an
earlier keyframe, so long-GOP, high-resolution files can step backward slowly.

BUILD FROM SOURCE
Install the .NET 8 SDK on Windows. Windows tar.exe is used to unpack the engine.
From this source directory, run:
  powershell -ExecutionPolicy Bypass -File .\build.ps1

This publishes compressed, self-contained single-file executables and ZIPs
for x64 and ARM64, plus an application source ZIP, under artifacts.
Use -Runtime win-x64 or -Runtime win-arm64 to build one architecture.
The build downloads a pinned mpv DLL from shinchiro's upstream release and
verifies SHA-256 checksums. Internet access is needed for the first build.
If tools\dotnet\dotnet.exe exists, the scripts use that local SDK.

TEST
FFmpeg is needed only to generate test videos. With the engine downloaded:
  powershell -ExecutionPolicy Bypass -File .\test.ps1 -Ffmpeg C:\path\ffmpeg.exe -Ui

Tests cover actual MP4 decoding, both frame directions, variable frame rates,
0.1x wall-clock playback, interval stopping/replay/looping and boundary seeking,
invalid files, time parsing, visible video rendering, and minimum-window layout.
-Ui opens a temporary test window and captures only its client area to
artifacts\ui.png; omit -Ui for headless playback tests.
Use -Runtime to test a particular architecture. x64 can run under Windows ARM64
emulation. Formatting is checked with dotnet format whitespace.

LICENSE
Application source: GPL-3.0-or-later; see LICENSE.txt.
Keep the included third-party licenses and notices with the application.
The supplied playback build includes GPL components. Before redistributing
binaries, provide the complete corresponding source for mpv and its bundled
dependencies as required by their licenses, not just this application source ZIP.
See THIRD-PARTY-NOTICES.txt for the exact upstream builds and source locations.
