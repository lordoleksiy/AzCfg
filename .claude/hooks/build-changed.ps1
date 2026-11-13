# PostToolUse hook: builds the .csproj nearest to the edited .cs/.csproj file; prints errors only.
$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$file = $payload.tool_input.file_path
if (-not $file -or $file -notmatch '\.(cs|csproj)$' -or -not (Test-Path $file)) { exit 0 }

$dir = Split-Path -Parent (Resolve-Path $file)
$proj = $null
while ($dir) {
    $proj = Get-ChildItem -Path $dir -Filter *.csproj -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($proj) { break }
    $dir = Split-Path -Parent $dir
}
if (-not $proj) { exit 0 }

$out = dotnet build $proj.FullName --nologo -v q -clp:ErrorsOnly 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    $lines = ($out -split "`r?`n" | Where-Object { $_ -match 'error ' } | Select-Object -Unique | Select-Object -First 15) -join "`n"
    [Console]::Error.WriteLine("dotnet build failed for $($proj.Name):`n$lines")
    exit 2
}
exit 0
