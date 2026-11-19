$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$fx  = Split-Path $csc
$src = "F:\AI Project\AttriDel\src"
$out = "F:\AI Project\AttriDel\dist\AttriDel.exe"
$rsp = "F:\AI Project\AttriDel\build\build.rsp"
$manifest = "F:\AI Project\AttriDel\build\AttriDel.manifest"
$icon = "F:\AI Project\AttriDel\build\AttriDel.ico"

$refNames = @(
  "System.dll",
  "System.Core.dll",
  "System.Windows.Forms.dll",
  "System.Drawing.dll",
  "System.Xml.dll",
  "System.IO.Compression.dll",
  "System.IO.Compression.FileSystem.dll"
)

$lines = @()
$lines += "/target:winexe"
$lines += "/optimize+"
$lines += "/langversion:5"
$lines += "/win32manifest:`"$manifest`""
$lines += "/win32icon:`"$icon`""
$lines += "/out:`"$out`""
foreach ($r in $refNames) {
  $lines += ("/reference:`"" + (Join-Path $fx $r) + "`"")
}
foreach ($f in (Get-ChildItem $src -Filter *.cs)) {
  $lines += ("`"" + $f.FullName + "`"")
}

[System.IO.File]::WriteAllLines($rsp, $lines, (New-Object System.Text.UTF8Encoding($false)))
& $csc ("@" + $rsp)
Write-Output ("ExitCode=" + $LASTEXITCODE)
if (Test-Path $out) {
  $f = Get-Item $out
  Write-Output ("EXE_BYTES=" + $f.Length)
}
