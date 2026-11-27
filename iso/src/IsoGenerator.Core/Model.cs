using System.Text.Json.Serialization;

namespace IsoGenerator.Core;

// Model coordinates remain in millimeters. Drawing layout must never change them.
public readonly record struct ModelPoint(double X, double Y, double Z)
{
    public double DistanceTo(ModelPoint b) => Math.Sqrt(Math.Pow(X-b.X,2)+Math.Pow(Y-b.Y,2)+Math.Pow(Z-b.Z,2));
}
public readonly record struct SheetPoint(double X, double Y);
public sealed record Port(string Name, ModelPoint Position, ModelPoint Direction, string EndCondition = "");
public enum ComponentKind
{
    Other, Pipe, Elbow, Tee, Reducer, Valve, Flange, BlindFlange, Cap, Support, Joint, TapOffset
}

public sealed record Part(string SourceDrawing, string Handle, string Kind, string Line,
    IReadOnlyList<Port> Ports, IReadOnlyDictionary<string,string> Properties, ModelPoint? OperatorDirection = null)
{
    [JsonIgnore] public string CatalogClass=>Properties.GetValueOrDefault("PnPClassName","");

    [JsonIgnore] public bool IsConnector=>Kind=="AcPpDb3dConnector";

    [JsonIgnore] public ComponentKind Component=>CatalogClass switch
    {
        "Pipe"=>ComponentKind.Pipe,
        "Elbow" or "PipeBend"=>ComponentKind.Elbow,
        "Tee"=>ComponentKind.Tee,
        "Reducer"=>ComponentKind.Reducer,
        "Valve"=>ComponentKind.Valve,
        "Flange"=>ComponentKind.Flange,
        "BlindFlange"=>ComponentKind.BlindFlange,
        "Cap"=>ComponentKind.Cap,
        "Support"=>ComponentKind.Support,
        "Joint"=>ComponentKind.Joint,
        "" when IsConnector&&!Properties.ContainsKey("PnPClassName")=>ComponentKind.Joint,
        _=>ComponentKind.Other
    };

    [JsonIgnore] public string SymbolDefinition=>Properties.GetValueOrDefault("ContentIsoSymbolDefinition","");
}
public sealed record ModelIsoMarker(string Handle,string HostHandle,string HostPort,ModelPoint Position,int InformationType,ModelPoint? Direction=null);
public sealed record BoundaryTapAxis(string Handle,string Port,ModelPoint Axis);
// Connections come from Plant topology; proximity alone is not a connection.
public sealed record Connection(string FirstHandle, string FirstPort, string SecondHandle, string SecondPort);
public sealed record EquipmentConnection(string OwnerHandle,string OwnerPort,string EquipmentHandle,string EquipmentPort,string Tag,string Nozzle,double? EquipmentElevation=null);
public sealed record PipelineSnapshot(int SchemaVersion, string SourceDrawing,
    IReadOnlyList<Part> Parts, IReadOnlyList<Connection> Connections, IReadOnlyList<string> Warnings,IReadOnlyList<Part>? Accessories=null,IReadOnlyList<ModelIsoMarker>? IsoMarkers=null,IReadOnlyList<Connection>? RequiredCuts=null,IReadOnlyList<BoundaryTapAxis>? BoundaryTapAxes=null,IReadOnlyList<ExternalLineContext>? ExternalLineContexts=null,IReadOnlyList<EquipmentConnection>? EquipmentConnections=null);

public static class IsometricProjection
{
    // +X up-right, +Y up-left, +Z up. Rotation is about model Z before projection.
    public static SheetPoint Project(ModelPoint point, ModelPoint origin = default, int quarterTurns = 0)
    {
        double x = point.X-origin.X, y = point.Y-origin.Y, z = point.Z-origin.Z;
        int turns = ((quarterTurns % 4) + 4) % 4;
        for (int i=0;i<turns;i++) (x,y)=(-y,x);
        return new(Math.Sqrt(3)/2*(x-y), .5*(x+y)+z);
    }
}
public sealed record DimensionValue(string SourceId, double Millimeters, bool ReferenceOnly = false)
{
    public string Label => Math.Round(Millimeters, 0, MidpointRounding.AwayFromZero)
        .ToString(System.Globalization.CultureInfo.InvariantCulture) + (ReferenceOnly ? "*" : "");
}
