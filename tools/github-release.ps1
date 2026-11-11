param([string]$Owner='IvanGelenko',[string]$Repository='specgen-net',[switch]$CheckOnly,[string]$Tag,[string]$Commit,[string]$Installer,[string]$NotesFile)
$ErrorActionPreference='Stop'
if($Owner -notmatch '^[A-Za-z0-9_.-]+$' -or $Repository -notmatch '^[A-Za-z0-9_.-]+$'){throw 'Invalid repository'}
# Reuse the user's Git credential manager. Never print or persist the credential.
$env:GIT_TERMINAL_PROMPT='0';$env:GCM_INTERACTIVE='Never'
$lines="protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
if($LASTEXITCODE -ne 0){throw 'GitHub credential unavailable'}
$credentials=@{};foreach($line in $lines){$separator=$line.IndexOf('=');if($separator -gt 0){$credentials[$line.Substring(0,$separator)]=$line.Substring($separator+1)}}
$lines=$null
if(!$credentials['password']){throw 'GitHub credential unavailable'}
$headers=@{Authorization=('Bearer '+$credentials['password']);Accept='application/vnd.github+json';'User-Agent'='SpecGen-Release';'X-GitHub-Api-Version'='2022-11-28';'Cache-Control'='no-cache'}
$api="https://api.github.com/repos/$Owner/$Repository"
function Request([string]$Method,[string]$Url,$Body=$null){
 if($null -eq $Body){return Invoke-RestMethod -Uri $Url -Method $Method -Headers $headers -TimeoutSec 60}
 $json=$Body|ConvertTo-Json -Depth 12 -Compress
 Invoke-RestMethod -Uri $Url -Method $Method -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec 60
}
$repo=Request GET $api
if(!$repo.permissions.push){throw 'GitHub credential cannot publish to this repository'}
if($CheckOnly){
 $latest=Request GET "$api/releases/latest"
 Write-Output "Repository write access confirmed; latest release: $($latest.tag_name)"
 try{$runners=Request GET "$api/actions/runners";Write-Output ('Runner states: '+(($runners.runners|ForEach-Object {$_.status}) -join ', '))}catch{Write-Output 'Runner status unavailable'}
 exit 0
}
if($Tag -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+$' -or $Commit -notmatch '^[a-f0-9]{40}$'){throw 'Invalid release tag or commit'}
if(!(Test-Path -LiteralPath $Installer) -or !(Test-Path -LiteralPath $NotesFile)){throw 'Installer or release notes missing'}
$localHash=(Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash.ToLowerInvariant();$localSize=(Get-Item -LiteralPath $Installer).Length
$release=$null
try{$release=Request GET "$api/releases/tags/$Tag"}catch{if([int]$_.Exception.Response.StatusCode -ne 404){throw}}
if($release -and !$release.draft){throw 'This release is already published; refusing to replace it'}
$notes=Get-Content -LiteralPath $NotesFile -Raw -Encoding utf8
if(!$release){$release=Request POST "$api/releases" @{tag_name=$Tag;target_commitish=$Commit;name="SpecGen $($Tag.TrimStart('v'))";body=$notes;draft=$true;prerelease=$false}}
$asset=@($release.assets|Where-Object name -eq 'PipingSpecPlugin-Setup.exe')|Select-Object -First 1
if($asset -and ($asset.size -ne $localSize -or $asset.digest -ne ('sha256:'+$localHash))){
 # Only replace our installer asset on an unpublished draft.
 Request DELETE "$api/releases/assets/$($asset.id)"|Out-Null;$asset=$null
}
if(!$asset){$asset=Invoke-RestMethod -Uri "https://uploads.github.com/repos/$Owner/$Repository/releases/$($release.id)/assets?name=PipingSpecPlugin-Setup.exe" -Method Post -Headers $headers -ContentType 'application/octet-stream' -InFile $Installer -TimeoutSec 300}
if($asset.size -ne $localSize -or $asset.digest -ne ('sha256:'+$localHash)){throw 'Uploaded installer size or SHA-256 mismatch; release remains a draft'}
$published=Request PATCH "$api/releases/$($release.id)" @{draft=$false;prerelease=$false;make_latest='true';body=$notes}
Write-Output "Published: $($published.html_url)"
Write-Output "Installer SHA256: $localHash"
Write-Output "Download: $($asset.browser_download_url)"
$credentials.Clear();$headers.Clear()
