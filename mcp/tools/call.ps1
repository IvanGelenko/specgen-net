param([string]$Tool, [string]$ArgsJson, [string]$ArgsFile)
[Console]::OutputEncoding = [Text.Encoding]::UTF8
if ($ArgsFile) { $ArgsJson = Get-Content $ArgsFile -Raw -Encoding UTF8 }
$tok = (Get-Content "$env:LOCALAPPDATA\Plant3dMcp\token" -Raw).Trim()
$body = '{"tool":"' + $Tool + '","args":' + $ArgsJson + '}'
try {
    Invoke-RestMethod -Uri "http://127.0.0.1:18765/" -Method Post -Headers @{ Authorization = "Bearer $tok" } `
        -ContentType "application/json" -Body ([Text.Encoding]::UTF8.GetBytes($body)) | ConvertTo-Json -Depth 8
} catch { "HTTP error: " + $_.Exception.Message }
