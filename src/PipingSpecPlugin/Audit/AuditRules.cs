using System;
using System.Collections.Generic;
using System.Linq;

namespace PipingSpecPlugin.Audit
{
    public sealed class WallThicknessRule : IAuditRule
    {
        public string Id => "wall";
        public string Title => "Толщины стенок";
        public string Description => "Трубы, отводы и изгибы трубы одной линии с одинаковым номинальным диаметром — одинаковая толщина стенки; замечание — на каждую пару соединённых деталей, где стенка меняется. У тройников и переходов стенка конца не тоньше соединённой с ним трубы. Допуск 0,01 мм.";
        public IEnumerable<AuditIssue> Evaluate(AuditSnapshot snapshot, AuditOptions options)
        {
            // A part whose properties were not read has its own read issue; here
            // it would only add «no line ID» (its line is unknown, not missing),
            // «no wall» and «no diameter».
            var parts = snapshot.Parts.Where(p => p.IsPipeOrElbow && !p.LineUnknown && options.Includes(p)).ToList();
            foreach (var p in parts)
            {
                if (string.IsNullOrWhiteSpace(p.LineKey))
                    yield return AuditRules.Issue(Id, Title, "Не проверено", "Не задан идентификатор линии.", new[] { p });
                // WallMm is WallRaw in the part's own catalogue LengthUnit,
                // converted to mm. A readable number with an unknown unit is
                // named as such, not as an unreadable wall.
                if (!p.WallMm.HasValue)
                    yield return AuditRules.Issue(Id, Title, "Не проверено",
                        AuditRules.ParsePositive(p.WallRaw).HasValue && !AuditRules.MillimetresPerUnit(p.LengthUnit).HasValue
                            ? $"Неизвестная единица длины детали «{(p.LengthUnit ?? "").Trim()}» — толщину стенки «{p.WallRaw}» нельзя перевести в мм. {p.Description}"
                            : $"Не удалось определить толщину стенки: «{p.WallRaw}». {p.Description}",
                        new[] { p });
                if (!AuditRules.ParsePositive(p.Diameter).HasValue)
                    yield return AuditRules.Issue(Id, Title, "Не проверено", $"Не удалось определить номинальный диаметр: «{p.Diameter}».", new[] { p });
            }
            var all = new Dictionary<string, AuditPart>(StringComparer.Ordinal);
            foreach (var p in snapshot.Parts) all.TryAdd(p.Key, p);
            foreach (var line in parts.Where(p => !string.IsNullOrWhiteSpace(p.LineKey) && p.WallMm.HasValue && AuditRules.ParsePositive(p.Diameter).HasValue)
                .GroupBy(p => p.LineKey, StringComparer.OrdinalIgnoreCase))
            foreach (var group in line.GroupBy(p => AuditRules.ParsePositive(p.Diameter).Value))
            {
                var list = group.ToList();
                if (list.Max(p => p.WallMm.Value) - list.Min(p => p.WallMm.Value) <= options.WallToleranceMm + 1e-9) continue;
                foreach (var issue in WallChanges(list, group.Key, all, options.WallToleranceMm)) yield return issue;
            }
            foreach (var issue in FittingsAgainstPipes(snapshot, options)) yield return issue;
        }

        // One remark per place where the wall changes: a pair of parts of the
        // line and DN joined directly or through a connection object. A run of
        // another wall that touches no part of a different wall (a separate
        // piece of the line) is one remark against the line's main wall. A
        // single list of every part used to hide where the change was.
        IEnumerable<AuditIssue> WallChanges(List<AuditPart> list, double diameter, IReadOnlyDictionary<string, AuditPart> all, double tolerance)
        {
            bool Same(AuditPart a, AuditPart b) => Math.Abs(a.WallMm.Value - b.WallMm.Value) <= tolerance + 1e-9;
            string Name(AuditPart p) => $"{AuditRules.KindOf(p)} Ду {p.Diameter} [{p.Key}] — {p.WallMm.Value:0.###} мм";
            var members = list.ToDictionary(p => p.Key, StringComparer.Ordinal);
            var near = list.ToDictionary(p => p.Key, p => Joined(p, all).Where(q => q.Key != p.Key && members.ContainsKey(q.Key))
                .Select(q => members[q.Key]).GroupBy(q => q.Key).Select(g => g.First()).ToList(), StringComparer.Ordinal);
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in list)
                foreach (var q in near[p.Key])
                {
                    if (string.CompareOrdinal(p.Key, q.Key) >= 0 || Same(p, q)) continue;
                    var (thin, thick) = p.WallMm.Value <= q.WallMm.Value ? (p, q) : (q, p);
                    covered.Add(p.Key); covered.Add(q.Key);
                    var pair = AuditRules.Issue(Id, Title, "Несоответствие",
                        $"Смена толщины стенки при Ду {diameter:0.###}: {Name(thin)} ↔ {Name(thick)} (разница {thick.WallMm.Value - thin.WallMm.Value:0.###} мм).", new[] { thin, thick });
                    pair.Magnitude = thick.WallMm.Value - thin.WallMm.Value;
                    yield return pair;
                }
            // The line's main wall: the most parts; a tie — the thicker one,
            // so the thinner run is the one named.
            var main = list.GroupBy(p => Math.Round(p.WallMm.Value, 6)).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var start in list.Where(p => Math.Abs(p.WallMm.Value - main.Key) > tolerance + 1e-9))
            {
                if (!seen.Add(start.Key)) continue;
                // The run of this wall the part belongs to.
                var run = new List<AuditPart> { start };
                for (int i = 0; i < run.Count; i++)
                    foreach (var q in near[run[i].Key].Where(q => Same(q, start) && seen.Add(q.Key))) run.Add(q);
                if (run.Any(p => covered.Contains(p.Key))) continue;
                var odd = AuditRules.Issue(Id, Title, "Несоответствие",
                    $"Стенка {start.WallMm.Value:0.###} мм при Ду {diameter:0.###}: " + string.Join(", ", run.Select(p => $"{AuditRules.KindOf(p)} Ду {p.Diameter} [{p.Key}]")) +
                    $". Основная стенка линии — {main.Key:0.###} мм ({main.Count()} дет.); с трубами и отводами другой стенки участок напрямую не соединён (граничит с арматурой, фланцами, тройниками или стоит отдельно).", run);
                odd.Magnitude = Math.Abs(main.Key - start.WallMm.Value);
                yield return odd;
            }
        }

        // A tee or reducer end must not be thinner than the pipe welded to it
        // (directly or through a weld / connection object). The end is the one
        // with the pipe's nominal diameter: a reducer's ends differ.
        IEnumerable<AuditIssue> FittingsAgainstPipes(AuditSnapshot snapshot, AuditOptions options)
        {
            var byKey = new Dictionary<string, AuditPart>(StringComparer.Ordinal);
            foreach (var p in snapshot.Parts) byKey.TryAdd(p.Key, p);
            foreach (var fitting in snapshot.Parts.Where(p => p.IsTeeOrReducer && !p.LineUnknown && options.Includes(p)))
            {
                var name = AuditRules.NameOf(fitting);
                var pipes = Joined(fitting, byKey).Where(p => p.IsPipe).GroupBy(p => p.Key).Select(g => g.First()).ToList();
                if (pipes.Count == 0)
                {
                    if (!fitting.ConnectionsRead)
                        yield return AuditRules.Issue(Id, Title, "Не проверено", $"{name}: соединения не прочитаны — стенку нельзя сравнить с трубами.", new[] { fitting });
                    continue;
                }
                if (fitting.Ends == null || fitting.Ends.Count == 0)
                {
                    yield return AuditRules.Issue(Id, Title, "Не проверено", $"{name}: не удалось прочитать толщины стенок концов" +
                        (string.IsNullOrWhiteSpace(fitting.EndsError) ? "." : ": " + fitting.EndsError.Trim()), new[] { fitting });
                    continue;
                }
                foreach (var pipe in pipes)
                {
                    var pipeName = AuditRules.NameOf(pipe);
                    var diameter = AuditRules.ParsePositive(pipe.Diameter);
                    var ends = diameter.HasValue
                        ? fitting.Ends.Where(e => AuditRules.ParsePositive(e.Diameter) is double d && Math.Abs(d - diameter.Value) < 1e-6).ToList()
                        : new List<AuditEnd>();
                    if (!pipe.WallMm.HasValue || ends.Count == 0 || ends.Any(e => !e.WallMm.HasValue))
                    {
                        var why = !pipe.WallMm.HasValue ? $"толщина стенки трубы «{pipe.WallRaw}» неизвестна"
                            : ends.Count == 0 ? $"нет конца под Ду трубы «{pipe.Diameter}»"
                            : $"толщина стенки конца «{ends.First(e => !e.WallMm.HasValue).WallRaw}» неизвестна";
                        yield return AuditRules.Issue(Id, Title, "Не проверено", $"{name} ↔ {pipeName}: {why}.", new[] { fitting, pipe });
                        continue;
                    }
                    var wall = ends.Min(e => e.WallMm.Value);
                    if (wall < pipe.WallMm.Value - options.WallToleranceMm - 1e-9)
                    {
                        var thin = AuditRules.Issue(Id, Title, "Несоответствие",
                            $"{name}: стенка {wall:0.###} мм на конце Ду {diameter.Value:0.###} тоньше соединённой трубы — {pipe.WallMm.Value:0.###} мм ({pipeName}).", new[] { fitting, pipe });
                        thin.Magnitude = pipe.WallMm.Value - wall;
                        yield return thin;
                    }
                }
            }
        }

        // Parts joined to this one directly or through a connection object.
        static IEnumerable<AuditPart> Joined(AuditPart part, IReadOnlyDictionary<string, AuditPart> byKey)
        {
            foreach (var key in part.Neighbours)
            {
                if (!byKey.TryGetValue(key, out var next)) continue;
                if (!next.IsConnector) { yield return next; continue; }
                foreach (var far in next.Neighbours)
                    if (far != part.Key && byKey.TryGetValue(far, out var joined)) yield return joined;
            }
        }
    }

    public sealed class ConnectivityRule : IAuditRule
    {
        public string Id => "connections";
        public string Title => "Соединения элементов";
        public string Description => "Свободные порты и раздельные участки одной линии. Свободный конец может быть предусмотрен проектом.";
        public IEnumerable<AuditIssue> Evaluate(AuditSnapshot snapshot, AuditOptions options)
        {
            foreach (var p in snapshot.Parts.Where(options.Includes))
            {
                if (!p.ConnectionsRead)
                    yield return AuditRules.Issue(Id, Title, "Не проверено", "Не удалось полностью прочитать соединения и порты" +
                        (string.IsNullOrWhiteSpace(p.ConnectionsError) ? "." : ": " + p.ConnectionsError.Trim()), new[] { p });
                foreach (var port in p.Ports.Where(v => v.Connected == false))
                    yield return AuditRules.Issue(Id, Title, "Проверить", $"Свободный порт «{port.Name}». Возможен проектный конец линии. {p.Description}", new[] { p });
                if (p.IsPipeOrElbow && p.ConnectionsRead && p.Ports.Count == 0)
                    yield return AuditRules.Issue(Id, Title, "Не проверено", "У трубы/отвода не найдены физические порты.", new[] { p });
            }

            var byKey = new Dictionary<string, AuditPart>(StringComparer.Ordinal);
            foreach (var p in snapshot.Parts) byKey.TryAdd(p.Key, p);
            foreach (var line in snapshot.Parts.Where(p => options.Includes(p) && !string.IsNullOrWhiteSpace(p.LineKey) && !p.IsConnector)
                .GroupBy(p => p.LineKey, StringComparer.OrdinalIgnoreCase))
            {
                // Components are counted over the line's parts with physical ports.
                var members = line.Where(p => p.Ports.Count > 0).ToList();
                var memberKeys = members.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
                if (memberKeys.Count < 2) continue;
                bool OnLine(AuditPart p) => string.Equals(p.LineKey, line.Key, StringComparison.OrdinalIgnoreCase);
                // Certain paths run through the line's parts with ports, its pipes /
                // elbows whose ports could not be read (known edges to them are
                // physical) and connectors without a line ID or of this line.
                // Equipment must not join separate pipelines through a common vessel.
                var allowed = new HashSet<string>(memberKeys, StringComparer.Ordinal);
                allowed.UnionWith(line.Where(p => p.IsPipeOrElbow && !p.ConnectionsRead).Select(p => p.Key));
                allowed.UnionWith(snapshot.Parts.Where(p => p.IsConnector && (string.IsNullOrWhiteSpace(p.LineKey) || OnLine(p))).Select(p => p.Key));
                var unseen = new HashSet<string>(memberKeys, StringComparer.Ordinal);
                var groups = new List<List<string>>();
                while (unseen.Count > 0)
                {
                    var pending = new Stack<string>();
                    pending.Push(unseen.First());
                    var visited = new HashSet<string>(StringComparer.Ordinal);
                    while (pending.Count > 0)
                    {
                        var key = pending.Pop();
                        if (!allowed.Contains(key) || !visited.Add(key)) continue;
                        unseen.Remove(key);
                        foreach (var next in byKey[key].Neighbours) pending.Push(next);
                    }
                    groups.Add(visited.Where(memberKeys.Contains).ToList());
                }
                if (groups.Count < 2) continue;

                // Could the data hide a joint? Also through parts whose line is
                // unknown and this line's other parts with unread ports (a valve
                // between the pieces — or a support, which must not bridge them
                // for certain). An edge is missing from the data only between two
                // elements whose connections were not read, so those may all be
                // joined. An unread element touching one piece alone changes
                // nothing (it used to downgrade, or skip, the whole line).
                var nodes = new HashSet<string>(allowed, StringComparer.Ordinal);
                nodes.UnionWith(snapshot.Parts.Where(p => p.LineUnknown || (!p.ConnectionsRead && OnLine(p))).Select(p => p.Key));
                var root = nodes.ToDictionary(k => k, k => k, StringComparer.Ordinal);
                string Find(string k) { while (root[k] != k) k = root[k] = root[root[k]]; return k; }
                void Join(string a, string b) { a = Find(a); b = Find(b); if (a != b) root[a] = b; }
                string firstUnread = null;
                foreach (var key in nodes)
                {
                    foreach (var next in byKey[key].Neighbours) if (nodes.Contains(next)) Join(key, next);
                    if (byKey[key].ConnectionsRead) continue;
                    if (firstUnread == null) firstUnread = key; else Join(firstUnread, key);
                }
                var listing = $"{groups.Count} несвязанных участков: " + string.Join("; ", groups.Select(g => string.Join(", ", g)));
                var joined = groups.GroupBy(g => Find(g[0]), StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
                if (joined.Count == 0)
                {
                    var split = AuditRules.Issue(Id, Title, "Проверить", $"В текущем чертеже линия состоит из {listing}. " +
                        "Соединения через другие DWG здесь не проверяются.", members);
                    split.Magnitude = groups.Count;
                    yield return split;
                    continue;
                }
                var doubtful = nodes.Where(k => (!byKey[k].ConnectionsRead || byKey[k].LineUnknown) && joined.Contains(Find(k)))
                    .OrderBy(k => k, StringComparer.Ordinal).ToList();
                yield return AuditRules.Issue(Id, Title, "Не проверено", $"В текущем чертеже линия может состоять из {listing}. " +
                    $"Участки могут соединяться через элементы с непрочитанными соединениями или номером линии ({string.Join(", ", doubtful)}), поэтому разрыв не подтверждён.", members);
            }
        }
    }

    public sealed class CollisionRule : IAuditRule
    {
        public string Id => "collisions";
        public string Title => "Коллизии с другими элементами";
        public string Description => "Пересечения выбранных линий со всеми объектами, включая загруженные внешние ссылки и вложенные вставки. Контакт опоры с деталью, к которой она присоединена, исключается. Детали, соединённые через объект соединения (сварной шов, фланцевое соединение, врезку), пересечением не считаются. Пересечения только изоляции при одинаковом заполненном номере линии игнорируются. Проверяется полная геометрия, независимо от XCLIP. Пересечения не глубже заданного допуска не учитываются.";
        public IEnumerable<AuditIssue> Evaluate(AuditSnapshot snapshot, AuditOptions options)
        {
            if (options.SelectedLines == null) return snapshot.GeometryIssues;
            if (options.SelectedLines.Count == 0) return Array.Empty<AuditIssue>();
            var keys = snapshot.Parts.Where(options.Includes).Select(p => p.Key).ToHashSet();
            // Unsupported obstacle geometry is relevant even if the obstacle is
            // not on a selected line. Positive pairs must involve a selected part.
            return snapshot.GeometryIssues.Where(i => i.Severity == "Не проверено" || i.Keys.Any(keys.Contains));
        }
    }
}
