namespace IsoGenerator.Core;

internal sealed class PipeSupportCrossingException(string repair,string lowerPipe,string support)
    :SheetCapacityException($"Пересечение труб {repair} и {lowerPipe} попадает на опору {support}; требуется изменить расстояния на схеме.",repair,conflict:LayoutConflict.ComponentCollision)
{
    public string LowerPipe{get;}=lowerPipe;
}

internal sealed class PipeValveOverlapException(string pipe,string valve)
    :SheetCapacityException($"Труба {pipe} накладывается на арматуру {valve}.",pipe,conflict:LayoutConflict.ComponentCollision)
{
    public string Valve{get;}=valve;
}

public static class PipeCrossingBreaks
{
    public const double DefaultGapLength = 6.0;

    readonly record struct PipeSegment(
        Stroke OriginalStroke,
        SheetPoint P1, SheetPoint P2,
        ModelPoint M1, ModelPoint M2,
        double Length2D);

    public static IReadOnlyList<Stroke> Apply(
        IReadOnlyList<Stroke> strokes,
        IReadOnlyList<Part> parts,
        int quarterTurns = 0,
        double gapLength = DefaultGapLength,
        IReadOnlyList<SymbolGlyph>? symbols = null, IReadOnlyList<Connection>? connections = null)
    {
        if (strokes.Count == 0 || gapLength <= 0.01) return strokes;

        var partMap = new Dictionary<string, Part>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parts) partMap.TryAdd(p.Handle, p);

        var pipeSegments = new List<PipeSegment>();
        var otherStrokes = new List<Stroke>();

        int turns = ((quarterTurns % 4) + 4) % 4;

        foreach (var s in strokes)
        {
            if (s.Role == "pipe" && s.Points.Count == 2 && partMap.TryGetValue(s.SourceId, out var part))
            {
                var ends = part.Ports.Where(p => p.Name.StartsWith("S", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (ends.Length == 2)
                {
                    var p1 = s.Points[0];
                    var p2 = s.Points[1];
                    double dx = p2.X - p1.X;
                    double dy = p2.Y - p1.Y;
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len > 0.1)
                    {
                        var m1 = ends[0].Position;
                        var m2 = ends[1].Position;
                        var vProj = IsometricProjection.Project(new ModelPoint(m2.X - m1.X, m2.Y - m1.Y, m2.Z - m1.Z), quarterTurns: turns);
                        if (vProj.X * dx + vProj.Y * dy < 0)
                        {
                            (m1, m2) = (m2, m1);
                        }
                        pipeSegments.Add(new PipeSegment(s, p1, p2, m1, m2, len));
                        continue;
                    }
                }
            }
            otherStrokes.Add(s);
        }

        if (pipeSegments.Count == 0 || (pipeSegments.Count < 2 && (symbols is null || symbols.Count == 0) && !otherStrokes.Any(s => s.Role == "continuation"))) return strokes;

        var repairs=Enumerable.Range(0,pipeSegments.Count).Select(_=>new List<(double Start,double End,string Pipe)>()).ToArray();
        var breakIntervals = new List<(double Start, double End)>[pipeSegments.Count];
        for (int i = 0; i < pipeSegments.Count; i++)
            breakIntervals[i] = new List<(double Start, double End)>();
        double ObserverProximity(ModelPoint pt)
        {
            double x = pt.X, y = pt.Y, z = pt.Z;
            for (int k = 0; k < turns; k++) (x, y) = (-y, x);
            return z - (x + y);
        }

        for (int i = 0; i < pipeSegments.Count; i++)
        {
            var segA = pipeSegments[i];
            for (int j = i + 1; j < pipeSegments.Count; j++)
            {
                var segB = pipeSegments[j];

                if (string.Equals(segA.OriginalStroke.SourceId, segB.OriginalStroke.SourceId, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Shared 3D ports (physical connection in model) do not cross as visual skew lines
                if (segA.M1.DistanceTo(segB.M1) < 1.0 || segA.M1.DistanceTo(segB.M2) < 1.0 ||
                    segA.M2.DistanceTo(segB.M1) < 1.0 || segA.M2.DistanceTo(segB.M2) < 1.0)
                    continue;

                double dax = segA.P2.X - segA.P1.X;
                double day = segA.P2.Y - segA.P1.Y;
                double dbx = segB.P2.X - segB.P1.X;
                double dby = segB.P2.Y - segB.P1.Y;

                double den = dax * dby - day * dbx;
                if (Math.Abs(den) < 1e-6) continue;

                double wx = segB.P1.X - segA.P1.X;
                double wy = segB.P1.Y - segA.P1.Y;

                double ta = (wx * dby - wy * dbx) / den;
                double tb = (wx * day - wy * dax) / den;

                // Must cross strictly in the interior of both segments
                if (ta <= 1e-4 || ta >= 1.0 - 1e-4 || tb <= 1e-4 || tb >= 1.0 - 1e-4)
                    continue;

                var pt3Da = new ModelPoint(
                    segA.M1.X + ta * (segA.M2.X - segA.M1.X),
                    segA.M1.Y + ta * (segA.M2.Y - segA.M1.Y),
                    segA.M1.Z + ta * (segA.M2.Z - segA.M1.Z));

                var pt3Db = new ModelPoint(
                    segB.M1.X + tb * (segB.M2.X - segB.M1.X),
                    segB.M1.Y + tb * (segB.M2.Y - segB.M1.Y),
                    segB.M1.Z + tb * (segB.M2.Z - segB.M1.Z));

                if (pt3Da.DistanceTo(pt3Db) < 1.0) continue;

                // Rule: "труба которая выше целая, нижняя в разрыв"
                int lowerIndex;
                double lowerT;
                double lowerLen;

                double dz = pt3Da.Z - pt3Db.Z;
                if (Math.Abs(dz) > 0.5)
                {
                    if (dz < 0)
                    {
                        lowerIndex = i;
                        lowerT = ta;
                        lowerLen = segA.Length2D;
                    }
                    else
                    {
                        lowerIndex = j;
                        lowerT = tb;
                        lowerLen = segB.Length2D;
                    }
                }
                else
                {
                    double proxA = ObserverProximity(pt3Da);
                    double proxB = ObserverProximity(pt3Db);
                    if (proxA < proxB)
                    {
                        lowerIndex = i;
                        lowerT = ta;
                        lowerLen = segA.Length2D;
                    }
                    else
                    {
                        lowerIndex = j;
                        lowerT = tb;
                        lowerLen = segB.Length2D;
                    }
                }

                double deltaT = (gapLength * 0.5) / lowerLen;
                double t0 = Math.Max(0.0, lowerT - deltaT);
                double t1 = Math.Min(1.0, lowerT + deltaT);
                breakIntervals[lowerIndex].Add((t0, t1));
                repairs[lowerIndex].Add((t0,t1,pipeSegments[lowerIndex==i?j:i].OriginalStroke.SourceId));
            }
        }

        // Rule: lower pipe crossing behind an upper symbol (valve, fitting, flange) gets a break.
        // Supports are attached to pipes and never break pipes ("разрывы на опорах не делать").
        if (symbols is not null && symbols.Count > 0)
        {
            foreach (var g in symbols.Where(sym => sym.Variant != "missing"))
            {
                if (!partMap.TryGetValue(g.SourceId, out var symPart) || symPart.Ports.Count == 0) continue;
                if (BomCategories.Classify(symPart) == BomCategory.Supports ||
                    symPart.CatalogClass.IndexOf("Support", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    g.Definition.IndexOf("SUPPORT", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                double symZ = symPart.Ports.Average(p => p.Position.Z);
                var symPt3D = new ModelPoint(
                    symPart.Ports.Average(p => p.Position.X),
                    symPart.Ports.Average(p => p.Position.Y),
                    symZ);
                var corners = SymbolFootprint.Corners(g);
                if(connections is not null&&!g.Context&&BomCategories.Classify(symPart)==BomCategory.Valves)
                {
                    var adjacent=Neighbors(symPart.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach(string joint in adjacent.Where(handle=>partMap.TryGetValue(handle,out var part)&&part.IsConnector).ToArray())
                        adjacent.UnionWith(Neighbors(joint));
                    foreach(var pipe in pipeSegments)
                    {
                        string handle=pipe.OriginalStroke.SourceId;
                        if(handle==symPart.Handle||adjacent.Contains(handle)||
                           symPart.Ports.Any(p=>p.Position.DistanceTo(pipe.M1)<1||p.Position.DistanceTo(pipe.M2)<1))continue;
                        var axis=new DimensionRouting.Segment(pipe.P1,pipe.P2,true);
                        if(corners.Zip(corners.Skip(1).Append(corners[0]),(a,b)=>new DimensionRouting.Segment(a,b,false))
                            .Any(edge=>DimensionRouting.CheckCross(axis,edge)||
                                DimensionRouting.DistanceSquared(edge.A,axis)<16||
                                DimensionRouting.DistanceSquared(axis.A,edge)<16||
                                DimensionRouting.DistanceSquared(axis.B,edge)<16))
                            throw new PipeValveOverlapException(handle,symPart.Handle);
                    }

                    IEnumerable<string> Neighbors(string handle)=>connections.Where(c=>c.FirstHandle==handle||c.SecondHandle==handle)
                        .Select(c=>c.FirstHandle==handle?c.SecondHandle:c.FirstHandle);
                }
                double symRadius = corners.Length > 0 ? corners.Max(c => Math.Sqrt((c.X - g.Origin.X) * (c.X - g.Origin.X) + (c.Y - g.Origin.Y) * (c.Y - g.Origin.Y))) : 5.0;

                for (int i = 0; i < pipeSegments.Count; i++)
                {
                    var seg = pipeSegments[i];
                    if (string.Equals(seg.OriginalStroke.SourceId, g.SourceId, StringComparison.OrdinalIgnoreCase)) continue;

                    double dax = seg.P2.X - seg.P1.X;
                    double day = seg.P2.Y - seg.P1.Y;
                    double len2 = seg.Length2D * seg.Length2D;
                    double t = ((g.Origin.X - seg.P1.X) * dax + (g.Origin.Y - seg.P1.Y) * day) / len2;

                    if (t <= 1e-4 || t >= 1.0 - 1e-4) continue;

                    double px = seg.P1.X + t * dax;
                    double py = seg.P1.Y + t * day;
                    double dist = Math.Sqrt((g.Origin.X - px) * (g.Origin.X - px) + (g.Origin.Y - py) * (g.Origin.Y - py));

                    if (dist > Math.Max(symRadius, 4.0)) continue;

                    var pt3Da = new ModelPoint(
                        seg.M1.X + t * (seg.M2.X - seg.M1.X),
                        seg.M1.Y + t * (seg.M2.Y - seg.M1.Y),
                        seg.M1.Z + t * (seg.M2.Z - seg.M1.Z));

                    double dz = pt3Da.Z - symZ;
                    bool isLower = Math.Abs(dz) > 0.5 ? dz < 0 : ObserverProximity(pt3Da) < ObserverProximity(symPt3D);

                    if (isLower)
                    {
                        double breakWidth = Math.Max(gapLength * 0.5, symRadius * 0.8);
                        double deltaT = breakWidth / seg.Length2D;
                        double t0 = Math.Max(0.0, t - deltaT);
                        double t1 = Math.Min(1.0, t + deltaT);
                        breakIntervals[i].Add((t0, t1));
                    }
                }
            }
        }

        // Rule: lower pipe crossing behind an upper continuation mark gets a break
        foreach (var cs in strokes.Where(s => s.Role == "continuation" && s.Points.Count >= 2))
        {
            if (!partMap.TryGetValue(cs.SourceId, out var contPart) || contPart.Ports.Count == 0) continue;
            double contZ = contPart.Ports.Average(p => p.Position.Z);
            var contPt3D = new ModelPoint(
                contPart.Ports.Average(p => p.Position.X),
                contPart.Ports.Average(p => p.Position.Y),
                contZ);
            double cx = cs.Points.Average(p => p.X), cy = cs.Points.Average(p => p.Y);

            for (int i = 0; i < pipeSegments.Count; i++)
            {
                var seg = pipeSegments[i];
                if (string.Equals(seg.OriginalStroke.SourceId, cs.SourceId, StringComparison.OrdinalIgnoreCase)) continue;

                double dax = seg.P2.X - seg.P1.X;
                double day = seg.P2.Y - seg.P1.Y;
                double len2 = seg.Length2D * seg.Length2D;
                double t = ((cx - seg.P1.X) * dax + (cy - seg.P1.Y) * day) / len2;

                if (t <= 1e-4 || t >= 1.0 - 1e-4) continue;

                double px = seg.P1.X + t * dax;
                double py = seg.P1.Y + t * day;
                double dist = Math.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));

                if (dist > 3.5) continue;

                var pt3Da = new ModelPoint(
                    seg.M1.X + t * (seg.M2.X - seg.M1.X),
                    seg.M1.Y + t * (seg.M2.Y - seg.M1.Y),
                    seg.M1.Z + t * (seg.M2.Z - seg.M1.Z));

                double dz = pt3Da.Z - contZ;
                bool isLower = Math.Abs(dz) > 0.5 ? dz < 0 : ObserverProximity(pt3Da) < ObserverProximity(contPt3D);

                if (isLower)
                {
                    double breakWidth = gapLength * 0.5 + 1.5;
                    double deltaT = breakWidth / seg.Length2D;
                    double t0 = Math.Max(0.0, t - deltaT);
                    double t1 = Math.Min(1.0, t + deltaT);
                    breakIntervals[i].Add((t0, t1));
                }
            }
        }

        var result = new List<Stroke>(otherStrokes);

        for (int i = 0; i < pipeSegments.Count; i++)
        {
            var seg = pipeSegments[i];
            var intervals = breakIntervals[i];
            if(intervals.Count>0&&symbols is not null)
            {
                double ux=(seg.P2.X-seg.P1.X)/seg.Length2D,uy=(seg.P2.Y-seg.P1.Y)/seg.Length2D;
                foreach(var support in symbols.Where(g=>partMap.TryGetValue(g.SourceId,out var part)&&BomCategories.Classify(part)==BomCategory.Supports))
                {
                    var part=partMap[support.SourceId];
                    double mx=seg.M2.X-seg.M1.X,my=seg.M2.Y-seg.M1.Y,mz=seg.M2.Z-seg.M1.Z,l2=mx*mx+my*my+mz*mz;
                    bool attached=part.Ports.Any(port=>
                    {
                        double t=((port.Position.X-seg.M1.X)*mx+(port.Position.Y-seg.M1.Y)*my+(port.Position.Z-seg.M1.Z)*mz)/Math.Max(1e-9,l2);
                        return t>=0&&t<=1&&port.Position.DistanceTo(new(seg.M1.X+mx*t,seg.M1.Y+my*t,seg.M1.Z+mz*t))<=1;
                    });
                    if(!attached)continue;
                    var footprint=SymbolFootprint.Corners(support);
                    double low=footprint.Min(p=>(p.X-seg.P1.X)*ux+(p.Y-seg.P1.Y)*uy)-1.5;
                    double high=footprint.Max(p=>(p.X-seg.P1.X)*ux+(p.Y-seg.P1.Y)*uy)+1.5;
                    if(intervals.Any(gap=>gap.Start*seg.Length2D<high&&gap.End*seg.Length2D>low))
                                            {
                        string repair=repairs[i].FirstOrDefault(gap=>gap.Start*seg.Length2D<high&&gap.End*seg.Length2D>low).Pipe??seg.OriginalStroke.SourceId;
                        throw new PipeSupportCrossingException(repair,seg.OriginalStroke.SourceId,support.SourceId);
                    }
                }
            }

            if (intervals.Count == 0)
            {
                result.Add(seg.OriginalStroke);
                continue;
            }

            intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
            var merged = new List<(double Start, double End)>();
            var current = intervals[0];
            for (int k = 1; k < intervals.Count; k++)
            {
                if (intervals[k].Start <= current.End + 1e-6)
                {
                    current = (current.Start, Math.Max(current.End, intervals[k].End));
                }
                else
                {
                    merged.Add(current);
                    current = intervals[k];
                }
            }
            merged.Add(current);

            double cursor = 0.0;
            double dax = seg.P2.X - seg.P1.X;
            double day = seg.P2.Y - seg.P1.Y;

            foreach (var brk in merged)
            {
                if (brk.Start > cursor + 1e-5)
                {
                    double subLen = (brk.Start - cursor) * seg.Length2D;
                    if (subLen >= 0.5)
                    {
                        var startPt = new SheetPoint(seg.P1.X + cursor * dax, seg.P1.Y + cursor * day);
                        var endPt = new SheetPoint(seg.P1.X + brk.Start * dax, seg.P1.Y + brk.Start * day);
                        result.Add(new Stroke(seg.OriginalStroke.SourceId, "pipe", new[] { startPt, endPt }));
                    }
                }
                cursor = Math.Max(cursor, brk.End);
            }

            if (cursor < 1.0 - 1e-5)
            {
                double subLen = (1.0 - cursor) * seg.Length2D;
                if (subLen >= 0.5)
                {
                    var startPt = new SheetPoint(seg.P1.X + cursor * dax, seg.P1.Y + cursor * day);
                    var endPt = new SheetPoint(seg.P1.X + 1.0 * dax, seg.P1.Y + 1.0 * day);
                    result.Add(new Stroke(seg.OriginalStroke.SourceId, "pipe", new[] { startPt, endPt }));
                }
            }
        }

        return result;
    }
}
