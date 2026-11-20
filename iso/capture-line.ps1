param([Parameter(Mandatory)][string]$SeedHandle,[string]$Output = "$PSScriptRoot\output\pipeline.json",[string]$MarkerMetadataFile)
$ErrorActionPreference='Stop'
$token=(Get-Content "$env:LOCALAPPDATA\Plant3dMcp\token" -Raw).Trim()
function Call([string]$Tool,$Arguments=@{}) {
    $body=@{tool=$Tool;args=$Arguments}|ConvertTo-Json -Depth 12 -Compress
    $r=Invoke-RestMethod http://127.0.0.1:18765/ -Method Post -Headers @{Authorization="Bearer $token"} -ContentType application/json -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 30
    if(!$r.ok){throw "$Tool : $($r.error)"}; return $r.result
}
function Point($p) { return @{X=[double]$p[0];Y=[double]$p[1];Z=[double]$p[2]} }
$null=Call ping
$document=(Call document_info).path
$project=Call plant_project_info
if($project.units -ne 'eMetric'){throw 'Only metric projects are supported in this prototype'}
function Guard { if((Call document_info).path -ne $document){throw 'Active document changed during capture; snapshot discarded'} }
Guard
$seed=Call get_part_properties @{handle=$SeedHandle}
$line=$seed.properties.LineNumberTag
if([string]::IsNullOrWhiteSpace($line)){throw 'Seed must have a nonempty LineNumberTag'}
Guard
function Save-Snapshot($snapshot) {
    $folder=Split-Path -Parent $Output
    if($folder){New-Item -ItemType Directory -Force $folder|Out-Null}
    $snapshot|ConvertTo-Json -Depth 32|Set-Content -LiteralPath $Output -Encoding utf8
    [pscustomobject]@{file=$Output;line=$line;parts=$snapshot.Parts.Count;connections=$snapshot.Connections.Count;markers=$snapshot.IsoMarkers.Count}|ConvertTo-Json
}

$native=$null
try {
    $native=Call capture_iso_snapshot @{line=$line;drawing=$document}
} catch {
    if($_.Exception.Message -notlike '*Unknown tool: capture_iso_snapshot*'){throw}
}
if($native){
    Save-Snapshot $native
    return
}

$all=Call list_plant_parts @{limit=1000;line=$line}
if($all.shown -ne $all.total){throw 'Drawing exceeds capture limit; no partial snapshot is allowed'}
$capturedLinks=@{}; $items=@{}; foreach($i in $all.items){$items[$i.handle]=$i}
$selected=@($all.items|Where-Object {$_.props.LineNumberTag -eq $line})
$queue=[Collections.Generic.Queue[string]]::new(); $seen=@{}; $parts=[Collections.Generic.List[object]]::new(); $connections=@{}; $accessories=[Collections.Generic.List[object]]::new(); $warnings=[Collections.Generic.List[string]]::new()
foreach($i in $selected){$queue.Enqueue($i.handle)}
while($queue.Count){
    $h=$queue.Dequeue(); if($seen.ContainsKey($h)){continue}; $seen[$h]=$true
    Guard
    $p=Call get_ports @{handle=$h}
    $props=@{}
    if($items.ContainsKey($h) -and $items[$h].cls -ne 'AcPpDb3dConnector'){
        Guard; $full=Call get_part_properties @{handle=$h}
        foreach($prop in $full.properties.PSObject.Properties){$props[$prop.Name]=[string]$prop.Value}
        if($props.LineNumberTag -ne $line){throw "Line assignment changed during capture: $h"}
    }
    Guard; $conditions=Call port_conditions @{handle=$h}
    $ports=@($p.ports|ForEach-Object {
        $name=$_.name;$condition=@($conditions.ports|Where-Object port -eq $name)
        $end=if($condition.Count -eq 1){[string]$condition[0].endCondition}else{''}
        if([string]::IsNullOrWhiteSpace($end)){$end=[string]$_.endType}
        @{Name=$name;Position=(Point $_.position);Direction=(Point $_.direction);EndCondition=$end}
    })
    $parts.Add(@{SourceDrawing=$document;Handle=$h;Kind=$p.cls;Line=$line;Ports=$ports;Properties=$props;OperatorDirection=$(if($p.operatorDirection){Point $p.operatorDirection}else{$null})})
    if($p.cls -eq 'AcPpDb3dConnector'){
        Guard;$connector=Call inspect_connector @{handle=$h}
        foreach($sub in $connector.subParts){
            if($sub.type -eq 'WeldSubPart'){continue}
            if(!$sub.partSizeProperties){throw "No component properties in connector $h"}
            $values=@{};foreach($prop in $sub.partSizeProperties.props.PSObject.Properties){$values[$prop.Name]=[string]$prop.Value}
            $values.PnPClassName=[string]$sub.partSizeProperties.type;$values.ConnectorHandle=$h
            if(!$values.DesignStd){$values.DesignStd=$values.CompatibleStandard}
            if($sub.type -eq 'BoltSetSubPart'){
                $values.Length=[string]$sub.boltLength;$values.IsoQuantity=if($values.NumberInSet){$values.NumberInSet}else{'1'};$values.IsoQuantityUnit='компл.'
            }else{$values.IsoQuantity='1';$values.IsoQuantityUnit='шт.'}
            $accessories.Add(@{SourceDrawing=$document;Handle="$h#$($sub.index)";Kind='ConnectorSubPart';Line=$line;Ports=@();Properties=$values})
        }
    }
    Guard; $links=@(Call get_connections @{handle=$h})
    $capturedLinks[$h]=@($links|ForEach-Object {(@("$($_.object1):$($_.port1)","$($_.object2):$($_.port2)")|Sort-Object)-join '|'}|Sort-Object -Unique)
    foreach($c in $links){
        if(!$c.object1 -or !$c.object2){$warnings.Add("$h has an unreadable connection");continue}
        $key=(@("$($c.object1):$($c.port1)","$($c.object2):$($c.port2)")|Sort-Object)-join '|'
        $connections[$key]=@{FirstHandle=$c.object1;FirstPort=[string]$c.port1;SecondHandle=$c.object2;SecondPort=[string]$c.port2}
        foreach($next in @($c.object1,$c.object2)){
            if(!$items.ContainsKey($next)){Guard;$neighbor=Call get_ports @{handle=$next};$items[$next]=@{cls=$neighbor.cls}}
            if($items.ContainsKey($next) -and $items[$next].cls -eq 'AcPpDb3dConnector' -and !$seen.ContainsKey($next)){$queue.Enqueue($next)}
        }
    }
}
Guard
# A second pass detects geometry/connection changes between individual MCP calls.
foreach($part in $parts) {
    Guard; $again=Call get_ports @{handle=$part.Handle}
    if(@($again.ports).Count -ne @($part.Ports).Count){throw "Ports changed during capture: $($part.Handle)"}
    foreach($old in $part.Ports){
        $current=@($again.ports|Where-Object name -eq $old.Name)
        if($current.Count -ne 1){throw "Port changed: $($part.Handle):$($old.Name)"}
        $new=Point $current[0].position
        foreach($axis in @('X','Y','Z')){if([Math]::Abs($new[$axis]-$old.Position[$axis]) -gt 0.001){throw "Geometry changed during capture: $($part.Handle). Retry without editing the drawing."}}
    }
    $againLinks=@(Call get_connections @{handle=$part.Handle})
    $expected=@($connections.Values|Where-Object {$_.FirstHandle -eq $part.Handle -or $_.SecondHandle -eq $part.Handle})
    $currentKeys=@($againLinks|ForEach-Object {(@("$($_.object1):$($_.port1)","$($_.object2):$($_.port2)")|Sort-Object)-join '|'}|Sort-Object -Unique)
    $oldKeys=@($capturedLinks[$part.Handle])
    if(($currentKeys -join ';') -ne ($oldKeys -join ';')){throw "Connections changed during capture: $($part.Handle)"}
}
Guard
$warnings.Add('Capture is read-only, sequential, and not an atomic transaction. Do not edit the drawing while capturing.')
$isoMarkers=[Collections.Generic.List[object]]::new()
$markerKinds=@{}; $markerDirections=@{}
if($MarkerMetadataFile){foreach($metadata in (Get-Content -LiteralPath $MarkerMetadataFile -Raw -Encoding utf8|ConvertFrom-Json)){
    $info=@($metadata.extension.PnP3dInfo|Where-Object code -eq 90)
    if($metadata.properties.Direction){$markerDirections[$metadata.handle]=Point $metadata.properties.Direction}
    if($info.Count){$markerKinds[$metadata.handle]=[int]$info[0].value}
}}
$markerObjects=@{items=@(foreach($cls in @('AcPpDb3dIsoSphereSymbol','AcPpDb3dIsoArrowSymbol','AcPpDb3dIsoStartPointSymbol')){(Call list_entities @{class=$cls;limit=1000}).items})}
foreach($marker in $markerObjects.items){
    $markerLinks=@(Call get_connections @{handle=$marker.handle})
    foreach($link in $markerLinks){
        $hostHandle=if($link.object1 -eq $marker.handle){$link.object2}else{$link.object1}
        $hostPort=if($link.object1 -eq $marker.handle){$link.port2}else{$link.port1}
        if(!$seen.ContainsKey($hostHandle)){continue}
        $markerPorts=Call get_ports @{handle=$marker.handle}
        if(!$markerPorts.ports.Count){continue}
        $kind=if($markerKinds.ContainsKey($marker.handle)){$markerKinds[$marker.handle]}else{-1}
        $isoMarkers.Add(@{Handle=$marker.handle;HostHandle=$hostHandle;HostPort=$hostPort;Position=(Point $markerPorts.ports[0].position);InformationType=$kind;Direction=$markerDirections[$marker.handle]})
    }
}
Guard
$snapshot=@{SchemaVersion=1;SourceDrawing=$document;Parts=@($parts.ToArray());Connections=@($connections.Values);Warnings=@($warnings.ToArray());Accessories=@($accessories.ToArray());IsoMarkers=@($isoMarkers.ToArray())}
if($isoMarkers.Where({$_.InformationType -lt 0}).Count){
    $warnings.Add('The running MCP version did not expose native marker metadata. This snapshot cannot certify manual breaks or flow direction.')
}
Save-Snapshot $snapshot



