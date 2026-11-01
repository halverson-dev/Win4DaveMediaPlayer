$ErrorActionPreference = 'Stop'
$root = 'C:\tmp'
$htmlPath = Join-Path $root 'Win4DaveMediaPlayer.html'
$sourcePath = Join-Path $root 'Win4DaveMediaPlayer.cs'
$exePath = Join-Path $root 'Win4DaveMediaPlayer.exe'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

$html = [Convert]::ToBase64String([IO.File]::ReadAllBytes($htmlPath))

$source = @"
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Win4DaveMediaPlayer
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            string[] candidates = new string[]
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"),
                Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe")
            };

            string edge = null;
            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                {
                    edge = candidate;
                    break;
                }
            }

            if (edge == null)
            {
                edge = "msedge.exe";
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = edge,
                Arguments = "--app=data:text/html;base64,$html",
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
    }
}
"@

[IO.File]::WriteAllText($sourcePath, $source, [Text.Encoding]::UTF8)

& $compiler /nologo /target:winexe /platform:anycpu `
  /out:$exePath `
  /reference:System.dll `
  $sourcePath

if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }
Write-Output $exePath
