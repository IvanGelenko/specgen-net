namespace IsoGenerator.Core;
public static class DimensionCongestion
{
    sealed record Row(PipeDimension Dimension,SheetPoint Start,SheetPoint End,SheetPoint Axis,SheetPoint Normal);
    static double Dot(SheetPoint a,SheetPoint b)=>a.X*b.X+a.Y*b.Y;
    public static string? PipeToExtend(PipelineSnapshot snapshot,SheetScene scene)=>
        FindRepair(snapshot,scene,includeProximity:true);

    internal static string? FindRepair(PipelineSnapshot snapshot,SheetScene scene,bool includeProximity)
    {
        var rows=scene.Dimensions.Select(d=>
        {
            double dx=d.End.X-d.Start.X,dy=d.End.Y-d.Start.Y,l=Math.Sqrt(dx*dx+dy*dy);var u=new SheetPoint(dx/l,dy/l);var n=new SheetPoint(-u.Y,u.X);var ex=d.ExtensionDirection!.Value;var p=d.DimensionLinePoint!.Value;
            SheetPoint Move(SheetPoint a){double t=Dot(new(p.X-a.X,p.Y-a.Y),n)/Dot(ex,n);return new(a.X+ex.X*t,a.Y+ex.Y*t);}
            return new Row(d,Move(d.Start),Move(d.End),u,n);
        }).ToArray();
        var parts=snapshot.Parts.ToDictionary(p=>p.Handle,StringComparer.OrdinalIgnoreCase);
        var adjacency=parts.Keys.ToDictionary(h=>h,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
        foreach(var c in snapshot.Connections)if(adjacency.ContainsKey(c.FirstHandle)&&adjacency.ContainsKey(c.SecondHandle)){adjacency[c.FirstHandle].Add(c.SecondHandle);adjacency[c.SecondHandle].Add(c.FirstHandle);}
        foreach(var row in rows)
        foreach(var glyph in (scene.Symbols??Array.Empty<SymbolGlyph>()).Where(g=>!g.Context&&parts.TryGetValue(g.SourceId,out var part)&&BomCategories.Classify(part) is BomCategory.Valves or BomCategory.Instruments))
        {
            var contours=glyph.LocalContours??new[]{(IReadOnlyList<SheetPoint>)SymbolFootprint.Corners(glyph).ToArray()};
            bool crossing=contours.Any(contour=>
            {
                var points=glyph.LocalContours is null?contour.ToArray():contour.Select(p=>new SheetPoint(glyph.Origin.X+glyph.XAxis.X*p.X+glyph.YAxis.X*p.Y,glyph.Origin.Y+glyph.XAxis.Y*p.X+glyph.YAxis.Y*p.Y)).ToArray();
                return points.Zip(points.Skip(1).Append(points[0]),(a,b)=>new DimensionRouting.Segment(a,b,false)).Any(edge=>DimensionRouting.CheckCross(new(row.Start,row.End,true),edge));
            });
            if(!crossing)continue;
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase){glyph.SourceId};var frontier=new[]{glyph.SourceId};
            for(int depth=0;depth<8&&frontier.Length>0;depth++)
            {
                foreach(string handle in frontier)
                {
                    if(parts[handle].Component!=ComponentKind.Pipe)continue;
                    var stroke=scene.Strokes.FirstOrDefault(st=>st.SourceId==handle&&st.Role=="pipe");if(stroke is null)continue;
                    double dx=stroke.Points[^1].X-stroke.Points[0].X,dy=stroke.Points[^1].Y-stroke.Points[0].Y,length=Math.Sqrt(dx*dx+dy*dy);
                    if(length>1e-6&&Math.Abs(dx*row.Normal.X+dy*row.Normal.Y)/length>.25)
                    {
                        if(Environment.GetEnvironmentVariable("ISO_PLANNER_TRACE")=="1")
                            Console.Error.WriteLine($"Размер {row.Dimension.Label} пересекает контур {glyph.SourceId}; кандидат {handle}.");
                        return handle;
                    }
                }
                frontier=frontier.SelectMany(h=>adjacency[h]).Where(seen.Add).OrderBy(h=>h,StringComparer.Ordinal).ToArray();
            }
        }
        if(!includeProximity)return null;
        var conflicts=new List<(Row A,Row B,double Gap)>();
        for(int i=0;i<rows.Length;i++)for(int j=i+1;j<rows.Length;j++)
        {
            var a=rows[i];var b=rows[j];if(a.Dimension.ChainId==b.Dimension.ChainId||Math.Abs(Dot(a.Axis,b.Axis))<.999)continue;
            double gap=Math.Abs(Dot(new(b.Start.X-a.Start.X,b.Start.Y-a.Start.Y),a.Normal));
            double overlap=Math.Min(Math.Max(Dot(a.Start,a.Axis),Dot(a.End,a.Axis)),Math.Max(Dot(b.Start,a.Axis),Dot(b.End,a.Axis)))-Math.Max(Math.Min(Dot(a.Start,a.Axis),Dot(a.End,a.Axis)),Math.Min(Dot(b.Start,a.Axis),Dot(b.End,a.Axis)));
            if(gap<7-1e-6&&overlap>3)conflicts.Add((a,b,gap));
        }
        foreach(var conflict in conflicts.OrderBy(c=>c.Gap))
        {
            var from=(conflict.A.Dimension.SourceIds??new[]{conflict.A.Dimension.SourceId}).Where(parts.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var to=(conflict.B.Dimension.SourceIds??new[]{conflict.B.Dimension.SourceId}).Where(parts.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(from.Overlaps(to))continue;
            var queue=new Queue<string>(from);var previous=from.ToDictionary(h=>h,_=>(string?)null,StringComparer.OrdinalIgnoreCase);string? reached=null;
            while(queue.Count>0){var h=queue.Dequeue();if(to.Contains(h)){reached=h;break;}foreach(var next in adjacency[h])if(previous.TryAdd(next,h))queue.Enqueue(next);}
            if(reached is null)continue;
            var path=new List<string>();for(string? h=reached;h is not null;h=previous[h])path.Add(h);
            var candidates=path.Where(h=>parts[h].Component==ComponentKind.Pipe).Select(h=>
            {
                var stroke=scene.Strokes.FirstOrDefault(s=>s.SourceId==h&&s.Role=="pipe");
                if(stroke is null)return (Handle:h,Score:0.0);
                var a=stroke.Points[0];var b=stroke.Points[^1];double dx=b.X-a.X,dy=b.Y-a.Y,l=Math.Sqrt(dx*dx+dy*dy);
                return (Handle:h,Score:Math.Abs(dx*conflict.A.Normal.X+dy*conflict.A.Normal.Y)/Math.Max(.001,l));
            }).Where(c=>c.Score>.25).OrderByDescending(c=>c.Score).ToArray();
            if(candidates.Length>0)return candidates[0].Handle;
        }

        // Check for 2D crowding between pipes and components of other runs (valves, fittings, continuation marks)
        var pipeStrokes = scene.Strokes.Where(s => s.Role == "pipe" && s.Points.Count >= 2).ToArray();
        var obstacles = new List<(string SourceId, SheetPoint Point)>();
        var contextOwners=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? ContextOwner(string source,SheetPoint point)
        {
            if(parts.ContainsKey(source))return source;
            var external=snapshot.ExternalLineContexts?.FirstOrDefault(c=>c.Parts.Any(p=>p.Handle==source)&&parts.ContainsKey(c.OwnerHandle));
            if(external is not null)return external.OwnerHandle;
            return scene.Annotations.Where(a=>a.Role=="continuation"&&a.Anchor.HasValue&&parts.ContainsKey(a.SourceId)).OrderBy(a=>Math.Pow(a.Anchor!.Value.X-point.X,2)+Math.Pow(a.Anchor.Value.Y-point.Y,2)).FirstOrDefault()?.SourceId;
        }
        foreach(var glyph in scene.Symbols??Array.Empty<SymbolGlyph>())
        {
            if(glyph.Variant=="missing")continue;
            string? owner=glyph.Context?ContextOwner(glyph.SourceId,glyph.Origin):glyph.SourceId;
            if(owner is null||!parts.TryGetValue(owner,out var part))continue;
            if(!glyph.Context&&BomCategories.Classify(part)==BomCategory.Supports)continue;
            obstacles.Add((owner,glyph.Origin));if(glyph.Context)contextOwners.Add(owner);
        }
        foreach(var context in scene.Strokes.Where(st=>st.Role.StartsWith("context-",StringComparison.Ordinal)&&st.Points.Count>1))
        foreach(var (a,b) in context.Points.Zip(context.Points.Skip(1)))
        {
            var middle=new SheetPoint((a.X+b.X)/2,(a.Y+b.Y)/2);string? owner=ContextOwner(context.SourceId,middle);
            if(owner is null)continue;contextOwners.Add(owner);
            foreach(var point in new[]{a,middle,b})obstacles.Add((owner,point));
        }
        foreach (var c in scene.Strokes.Where(s => s.Role == "continuation" && s.Points.Count >= 2))
            if (parts.ContainsKey(c.SourceId))
            {
                double cx = c.Points.Average(p => p.X), cy = c.Points.Average(p => p.Y);
                obstacles.Add((c.SourceId, new SheetPoint(cx, cy)));
            }

        var crowdingIncidents = new List<(string PipeHandle, string ObstacleHandle, double Dist, SheetPoint Normal)>();
        foreach (var ps in pipeStrokes)
        {
            var p1 = ps.Points[0]; var p2 = ps.Points[^1];
            double dx = p2.X - p1.X, dy = p2.Y - p1.Y, l2 = dx * dx + dy * dy;
            if (l2 < 1.0) continue;
            double l = Math.Sqrt(l2);
            var pipePart = parts[ps.SourceId];
            double pipeZ = pipePart.Ports.Count > 0 ? pipePart.Ports.Average(p => p.Position.Z) : 0;

            foreach (var (obsId, obsPt) in obstacles)
            {
                if (string.Equals(obsId, ps.SourceId, StringComparison.OrdinalIgnoreCase)) continue;
                if (adjacency.TryGetValue(ps.SourceId, out var direct) && direct.Contains(obsId)) continue;

                double t = ((obsPt.X - p1.X) * dx + (obsPt.Y - p1.Y) * dy) / l2;
                if (t <= 0.05 || t >= 0.95) continue;

                double px = p1.X + t * dx, py = p1.Y + t * dy;
                double dist = Math.Sqrt((obsPt.X - px) * (obsPt.X - px) + (obsPt.Y - py) * (obsPt.Y - py));
                if (dist >= 6.0) continue;

                var obsPart = parts[obsId];
                double obsZ = obsPart.Ports.Count > 0 ? obsPart.Ports.Average(p => p.Position.Z) : 0;
                if (!contextOwners.Contains(obsId)&&Math.Abs(pipeZ - obsZ) < 50.0) continue;

                var normal = dist > 0.001
                    ? new SheetPoint((obsPt.X - px) / dist, (obsPt.Y - py) / dist)
                    : new SheetPoint(-dy / l, dx / l);
                crowdingIncidents.Add((ps.SourceId, obsId, dist, normal));
            }
        }

        foreach (var incident in crowdingIncidents.OrderBy(ci => ci.Dist))
        {
            var queue = new Queue<string>();
            queue.Enqueue(incident.PipeHandle);
            var previous = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [incident.PipeHandle] = null };
            string? reached = null;
            while (queue.Count > 0)
            {
                var h = queue.Dequeue();
                if (string.Equals(h, incident.ObstacleHandle, StringComparison.OrdinalIgnoreCase)) { reached = h; break; }
                if (adjacency.TryGetValue(h, out var nbrs))
                {
                    foreach (var next in nbrs)
                        if (previous.TryAdd(next, h)) queue.Enqueue(next);
                }
            }
            if (reached is null) continue;
            var path = new List<string>();
            for (string? h = reached; h is not null; h = previous[h]) path.Add(h);

            var candidates = path
                .Where(h => !string.Equals(h, incident.PipeHandle, StringComparison.OrdinalIgnoreCase) &&
                            parts[h].Component==ComponentKind.Pipe)
                .Select(h =>
                {
                    var stroke = scene.Strokes.FirstOrDefault(s => s.SourceId == h && s.Role == "pipe");
                    if (stroke is null) return (Handle: h, Score: 0.0);
                    var a = stroke.Points[0]; var b = stroke.Points[^1];
                    double dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Sqrt(dx * dx + dy * dy);
                    if (l < 0.01) return (Handle: h, Score: 0.0);
                    double score = Math.Abs(dx * incident.Normal.X + dy * incident.Normal.Y) / l;
                    if (Math.Abs(dx) < 0.01 && Math.Abs(dy) > 0.1) score += 0.2; // bonus for vertical riser/drop
                    return (Handle: h, Score: score);
                })
                .Where(c => c.Score > 0.25)
                .OrderByDescending(c => c.Score)
                .ToArray();

            if (candidates.Length > 0) return candidates[0].Handle;
        }

        return null;
    }
}
