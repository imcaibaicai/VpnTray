# 编译 VpnTray（.NET Framework 4.8 自带编译器，无需安装 SDK）
# 用法： powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root 'src'
$outDir = Join-Path $root 'out'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw "找不到 csc.exe" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$sources = Get-ChildItem -Path (Join-Path $srcDir '*.cs') | Sort-Object Name
if ($sources.Count -eq 0) { throw "src 下没有 .cs" }

$manifest = Join-Path $srcDir 'app.manifest'
$exe = Join-Path $outDir 'VpnTray.exe'
$rsp = Join-Path $outDir 'build.rsp'

$lines = @()
$lines += '/nologo'
$lines += '/target:winexe'
$lines += '/platform:anycpu'
$lines += '/optimize+'
$lines += '/langversion:5'
$lines += '/codepage:65001'
$lines += '/warn:2'
if (Test-Path $manifest) { $lines += '/win32manifest:"' + $manifest + '"' }
foreach ($ref in @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Security.dll')) {
    $lines += '/r:' + $ref
}
$lines += '/out:"' + $exe + '"'
foreach ($s in $sources) { $lines += '"' + $s.FullName + '"' }

Set-Content -Path $rsp -Value $lines -Encoding UTF8

Write-Host "编译中..." -ForegroundColor Cyan
& $csc ('@' + $rsp)
$code = $LASTEXITCODE

if ($code -eq 0) {
    # 内核随程序一起放
    $pipeline = Join-Path $root 'bin\pipeline.exe'
    if (Test-Path $pipeline) { Copy-Item $pipeline (Join-Path $outDir 'pipeline.exe') -Force }
    $info = Get-Item $exe
    Write-Host ("编译成功: {0}  ({1:N0} 字节)" -f $info.FullName, $info.Length) -ForegroundColor Green
} else {
    Write-Host "编译失败，退出码 $code" -ForegroundColor Red
}
exit $code
