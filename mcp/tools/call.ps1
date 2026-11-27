param([Parameter(Mandatory)][string]$Tool, [string]$ArgsJson, [string]$ArgsFile)
[Console]::OutputEncoding = [Text.Encoding]::UTF8
if ($ArgsFile) { $ArgsJson = Get-Content -LiteralPath $ArgsFile -Raw -Encoding UTF8 -ErrorAction Stop }
if ([string]::IsNullOrWhiteSpace($ArgsJson)) { $ArgsJson = '{}' }
if (-not $ArgsJson.TrimStart().StartsWith('{')) { throw 'ArgsJson must be a JSON object.' }
$toolArgs = ConvertFrom-Json -InputObject $ArgsJson -ErrorAction Stop
if ($toolArgs -isnot [pscustomobject]) { throw 'ArgsJson must be a JSON object.' }
if (-not $env:LOCALAPPDATA) { throw 'LOCALAPPDATA is not set.' }
$tok = (Get-Content -LiteralPath "$env:LOCALAPPDATA\Plant3dMcp\token" -Raw -ErrorAction Stop).Trim()
$body = ConvertTo-Json -InputObject @{ tool = $Tool; args = $toolArgs } -Depth 100 -Compress
try {
    Invoke-RestMethod -Uri "http://127.0.0.1:18765/" -Method Post -Headers @{ Authorization = "Bearer $tok" } `
        -ContentType "application/json" -Body ([Text.Encoding]::UTF8.GetBytes($body)) | ConvertTo-Json -Depth 8
} catch { "HTTP error: " + $_.Exception.Message }
