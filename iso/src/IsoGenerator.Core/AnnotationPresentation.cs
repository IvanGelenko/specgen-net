namespace IsoGenerator.Core;

public readonly record struct AnnotationPresentation(int Priority,bool Leader=false,bool RemoteLeader=false,bool MultilineLeader=false)
{
    public static AnnotationPresentation ForRole(string role)=>role switch
    {
        "bend-angle"=>new(-1),
        "slope-angle" or "slope-value" or "slope-projection"=>new(0),
        "valve-tag" or "instrument-tag"=>new(1,Leader:true,RemoteLeader:true),
        "external-line" or "equipment-connection"=>new(1,Leader:true,RemoteLeader:true,MultilineLeader:true),
        "support-type"=>new(1,Leader:true),
        "elevation"=>new(2,Leader:true,RemoteLeader:true),
        "position" or "position-group"=>new(3),
        "continuation"=>new(4,Leader:true,RemoteLeader:true),
        _=>new(4)
    };
}
