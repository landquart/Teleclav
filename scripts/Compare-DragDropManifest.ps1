param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$Manifest
)
$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path -LiteralPath $Root).Path
$expected = Get-Content -LiteralPath $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json
$files = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$dirs = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$prefix = $rootPath.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
Get-ChildItem -LiteralPath $rootPath -Force -Recurse | ForEach-Object {
    if (($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Links and reparse points are not supported.' }
    $relative = $_.FullName.Substring($prefix.Length).Replace('\', '/')
    if ($_.PSIsContainer) { $dirs[$relative] = $true }
    else {
        $files[$relative] = @{
            size = [long]$_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
}
if ($dirs.Count -ne @($expected.directories).Count -or $files.Count -ne @($expected.files).Count) { throw 'FAIL: file or directory count differs.' }
foreach ($directory in $expected.directories) {
    if (-not $dirs.ContainsKey([string]$directory)) { throw "FAIL: directory differs: $directory" }
}
foreach ($file in $expected.files) {
    $path = [string]$file.path
    if (-not $files.ContainsKey($path)) { throw "FAIL: missing file: $path" }
    if ($files[$path].size -ne [long]$file.size -or $files[$path].sha256 -cne [string]$file.sha256) { throw "FAIL: size or SHA-256 differs: $path" }
}
Write-Output "PASS: $($files.Count) files and $($dirs.Count) directories match by path, size and SHA-256."
