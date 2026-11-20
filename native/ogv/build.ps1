<#
  Builds i26ogv.dll (Windows x64): the movie player's Ogg Theora + Vorbis decoder (native/ogv/i26ogv.c) with the reference
  libraries compiled in, from their official Xiph.Org source releases, pinned below with the publishers' SHA-256
  (https://downloads.xiph.org/releases/{ogg,vorbis,theora}/SHA256SUMS). Needs the MSVC C compiler (Visual Studio or its
  Build Tools, "Desktop development with C++"); nothing is installed.

  usage: powershell -ExecutionPolicy Bypass -File native\ogv\build.ps1 [-Work DIR] [-Out DIR]
    -Work  where the sources are downloaded, unpacked and built (default native\ogv\third_party, git-ignored)
    -Out   where the DLL goes (default game\native\win-x64, git-ignored; MoviePlayer looks there, next to the game's
           executable and in I76_OGV_LIB)
#>
param([string]$Work = (Join-Path $PSScriptRoot 'third_party'), [string]$Out = (Join-Path $PSScriptRoot '..\..\game\native\win-x64'))
$ErrorActionPreference = 'Stop'

$libs = @(
    @{ Name = 'libogg-1.3.6';    Url = 'https://downloads.xiph.org/releases/ogg/libogg-1.3.6.tar.gz';          Sha256 = '83e6704730683d004d20e21b8f7f55dcb3383cdf84c0daedf30bde175f774638' },
    @{ Name = 'libvorbis-1.3.7'; Url = 'https://downloads.xiph.org/releases/vorbis/libvorbis-1.3.7.tar.gz';    Sha256 = '0e982409a9c3fc82ee06e08205b1355e5c6aa4c36bca58146ef399621b0ce5ab' },
    @{ Name = 'libtheora-1.2.0'; Url = 'https://downloads.xiph.org/releases/theora/libtheora-1.2.0.tar.gz';    Sha256 = '279327339903b544c28a92aeada7d0dcfd0397b59c2f368cc698ac56f515906e' }
)

New-Item -ItemType Directory -Force $Work, $Out | Out-Null
foreach ($l in $libs) {
    $tgz = Join-Path $Work ($l.Name + '.tar.gz')
    if (-not (Test-Path $tgz)) { Write-Host "fetch $($l.Url)"; Invoke-WebRequest -UseBasicParsing -Uri $l.Url -OutFile $tgz }
    $sha = (Get-FileHash -Algorithm SHA256 $tgz).Hash.ToLowerInvariant()
    if ($sha -ne $l.Sha256) { throw "$($l.Name): SHA-256 $sha, expected $($l.Sha256) (delete $tgz to fetch it again)" }
    if (-not (Test-Path (Join-Path $Work $l.Name))) { tar -xzf $tgz -C $Work; if ($LASTEXITCODE) { throw "unpacking $tgz failed" } }
}

$ogg = Join-Path $Work 'libogg-1.3.6'; $vorbis = Join-Path $Work 'libvorbis-1.3.7'; $theora = Join-Path $Work 'libtheora-1.2.0'
$src = @()
$src += 'bitwise.c', 'framing.c' | ForEach-Object { Join-Path $ogg "src\$_" }
# libvorbis_la_SOURCES of lib/Makefile.am (the decoder's own pull in the encoder's analysis through block.c)
$src += 'mdct.c', 'smallft.c', 'block.c', 'envelope.c', 'window.c', 'lsp.c', 'lpc.c', 'analysis.c', 'synthesis.c', 'psy.c', 'info.c',
        'floor1.c', 'floor0.c', 'res0.c', 'mapping0.c', 'registry.c', 'codebook.c', 'sharedbook.c', 'lookup.c', 'bitrate.c' | ForEach-Object { Join-Path $vorbis "lib\$_" }
# decoder_sources of lib/Makefile.am without the old API's wrappers and the hand-written assembly (plain C on x64)
$src += 'bitpack.c', 'decinfo.c', 'decode.c', 'dequant.c', 'fragment.c', 'huffdec.c', 'idct.c', 'info.c', 'internal.c', 'quant.c', 'state.c' | ForEach-Object { Join-Path $theora "lib\$_" }
$src += Join-Path $PSScriptRoot 'i26ogv.c'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found: install Visual Studio or its Build Tools with the C++ workload' }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'no Visual Studio with the MSVC x64 tools found' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'

$obj = Join-Path $Work 'obj'; New-Item -ItemType Directory -Force $obj | Out-Null
$dll = Join-Path $Work 'i26ogv.dll'
# each library's own sources by their own include paths (two files named info.c: objects by library)
$cmd = Join-Path $Work 'build.cmd'
$flags = '/nologo /c /O2 /MT /W3 /DNDEBUG /D_CRT_SECURE_NO_WARNINGS /wd4244 /wd4267 /wd4305 /wd4996'
$inc = "/I`"$ogg\include`" /I`"$vorbis\include`" /I`"$vorbis\lib`" /I`"$theora\include`""
$lines = @("@echo off", "call `"$vcvars`" >nul || exit /b 1", "cd /d `"$obj`"")
$objs = @()
foreach ($s in $src) {
    $lib = if ($s.StartsWith($ogg)) { 'ogg' } elseif ($s.StartsWith($vorbis)) { 'vorbis' } elseif ($s.StartsWith($theora)) { 'theora' } else { 'i26' }
    $o = "${lib}_" + [IO.Path]::GetFileNameWithoutExtension($s) + '.obj'
    $objs += $o
    $lines += "cl $flags $inc `"$s`" /Fo`"$o`" || exit /b 1"
}
$lines += "link /nologo /DLL /OUT:`"$dll`" $($objs -join ' ') || exit /b 1"
$lines | Set-Content -Encoding ascii $cmd
cmd /c "`"$cmd`""
if ($LASTEXITCODE) { throw "build failed ($LASTEXITCODE)" }
Copy-Item $dll (Join-Path $Out 'i26ogv.dll') -Force
$built = Get-Item (Join-Path $Out 'i26ogv.dll')
Write-Host ("built {0} ({1:N0} bytes, SHA-256 {2})" -f $built.FullName, $built.Length, (Get-FileHash -Algorithm SHA256 $built).Hash.ToLowerInvariant())
