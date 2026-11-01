# Launch the ZIP built by CI in an isolated runner directory. This checks startup
# and the loaded .NET runtime, not Explorer interaction or network transfers.
param(
    [Parameter(Mandatory = $true)][string]$Archive,
    [Parameter(Mandatory = $true)][string]$Destination,
    [int]$TimeoutSeconds = 60
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Destination) { throw 'Destination must not exist.' }
Expand-Archive -LiteralPath $Archive -DestinationPath $Destination
$root = (Resolve-Path -LiteralPath $Destination).Path
$exe = Join-Path $root 'Heimdall.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Heimdall.exe is missing.' }
$emptyDotnet = Join-Path $root 'empty-dotnet'
New-Item -ItemType Directory -Path $emptyDotnet | Out-Null
$info = [System.Diagnostics.ProcessStartInfo]::new()
$info.FileName = $exe
$info.WorkingDirectory = $root
$info.UseShellExecute = $false
$info.Environment['DOTNET_ROOT'] = $emptyDotnet
$info.Environment['DOTNET_ROOT_X64'] = $emptyDotnet
$info.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$process = [System.Diagnostics.Process]::Start($info)
if ($null -eq $process) { throw 'Starting Heimdall.exe returned no process.' }
try {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $foundWindow = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) { throw "Heimdall exited before its window appeared: $($process.ExitCode)." }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $foundWindow = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $foundWindow) { throw 'Heimdall did not show a main window before the timeout.' }
    $runtime = @($process.Modules | Where-Object { $_.ModuleName -ieq 'coreclr.dll' })
    $expected = Join-Path $root 'coreclr.dll'
    if ($runtime.Count -ne 1 -or -not [string]::Equals($runtime[0].FileName, $expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The application did not load coreclr.dll from the extracted ZIP.'
    }
    Write-Host "PASS: extracted Heimdall.exe opened a window and loaded its bundled .NET runtime."
}
finally {
    $process.Refresh()
    if (-not $process.HasExited) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
    }
    $process.Dispose()
}
