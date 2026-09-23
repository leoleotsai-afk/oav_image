# Build ImageSearch.exe (WinForms, AnyCPU) using the built-in .NET Framework csc.exe.
# No .NET SDK, Visual Studio or NuGet packages required.

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root "src"
$binDir = Join-Path $root "bin"

if (-not (Test-Path $binDir)) {
    New-Item -ItemType Directory -Path $binDir | Out-Null
}

$fwDir = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$cscCandidates = @(
    (Join-Path $fwDir "csc.exe"),
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
    throw "csc.exe not found. .NET Framework 4.x does not appear to be installed."
}

$speechDll = "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\System.Speech\v4.0_4.0.0.0__31bf3856ad364e35\System.Speech.dll"
if (-not (Test-Path $speechDll)) {
    throw "System.Speech.dll not found in the GAC. Voice search requires it."
}

$outExe = Join-Path $binDir "ImageSearch.exe"
$manifest = Join-Path $srcDir "app.manifest"
$sources = @(
    (Join-Path $srcDir "Program.cs"),
    (Join-Path $srcDir "MainForm.cs"),
    (Join-Path $srcDir "ImageHasher.cs"),
    (Join-Path $srcDir "ImageIndex.cs"),
    (Join-Path $srcDir "VoiceSearch.cs"),
    (Join-Path $srcDir "CameraCapture.cs")
)

$references = "System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Web.Extensions.dll,$speechDll"

& $csc /nologo /target:winexe /platform:anycpu /out:"$outExe" /win32manifest:"$manifest" /reference:$references $sources

if ($LASTEXITCODE -ne 0) {
    throw "Build failed, see errors above."
}

Write-Output "Build succeeded: $outExe"
