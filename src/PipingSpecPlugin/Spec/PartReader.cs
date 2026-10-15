using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.AcPp3dObjectsUtils;
using Autodesk.ProcessPower.DataLinks;
using Autodesk.ProcessPower.PnP3dObjects;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// PartReader output: the row records plus a set of PnPIDs that are
    /// owned by Equipment in the same drawing (nozzles, manways, internal
    /// supports, etc.). The owned set is derived from
    /// DataLinksManager.GetRelatedRowIds on Equipment rows.
    /// </summary>
    public class PartReadResult
    {
        public List<PartRecord> Records = new();
        public HashSet<int> EquipmentOwnedPnPIds = new();
        // PnPID -> neighbouring PnPIDs through P3dPartConnection. Built
        // by calling DataLinksManager.GetRelatedRowIds on every row;
        // includes connectors (Buttweld, Universal) as standalone nodes
        // so valve-companion discovery can walk Valve → Connector → Flange.
        public Dictionary<int, List<int>> Topology = new();
        // Diagnostic counts so WRITESPEC can show whether topology /
        // equipment recognition found anything. Updated by BuildTopology.
        public int EquipmentCount;
        public int EquipmentNeighbourCount;
        // Rows we saw in the row-id set but could NOT turn into a record:
        // GetAllProperties threw or returned null, or BuildRecord bailed.
        // Surfaced in last-run.log so "a part went missing" can be traced
        // to the read stage instead of vanishing silently.
        public int SkippedRows;
        // One entry per skipped row (drawing-local rowId + reason). These no
        // longer stop WRITESPEC (the old "strict mode"): the command turns
        // them into rows of the «Позиции, требующие проверки» section.
        public List<(int RowId, string Reason)> SkippedRowDetails = new();
        // True if the WorkingDatabase swap for a side-DB read failed — the
        // read may then hit RowNotInThisDatabase on some rows.
        public bool WorkingDbSwapFailed;
        // Times an equipment-ownership mark was refused because the row is
        // a procurement category (pipe/fittings/valves/flanges). Diagnostic.
        public int OwnershipGuardKept;
        // True if a PnP database was Disconnect()-ed during the freshness
        // flush and Connect() then failed (even after a retry). Reads after
        // that point are unreliable for the WHOLE drawing (not one part), so
        // ReadOneDrawing still stops generation on it, the same as a failed
        // WorkingDatabase swap.
        public bool PnpReconnectFailed;
    }

    /// <summary>
    /// Pulls PartRecord instances out of a Plant 3D drawing's DataLinks
    /// blob. Iterating SelectAcPpRowIds gives us rowIds for every
    /// EngineeringItem in that drawing; GetAllProperties(rowId, true)
    /// returns the inherited property bag we then map onto PartRecord
    /// by name. Skipping a row when properties throw (corrupted link,
    /// stale row) is intentional — one broken part should not kill
    /// the whole spec.
    /// </summary>
    public static class PartReader
    {
        public static PartReadResult Read(Database db, DataLinksManager projectDlm = null,
                                          System.Action<string> trace = null)
        {
            void Trace(string msg) { try { trace?.Invoke(msg); } catch { } }

            var result = new PartReadResult();
            DataLinksManager dlm;
            try { dlm = DataLinksManager.GetManager(db); }
            catch (System.Exception ex)
            {
                Trace($"PartReader: GetManager не удался — {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            if (dlm == null)
            {
                Trace("PartReader: DataLinksManager отсутствует для чертежа.");
                throw new System.InvalidOperationException("DataLinksManager отсутствует для чертежа модели.");
            }

            var lineGroupCache = new Dictionary<(bool project, int id), string>();

            // GetRelatedRowIds on a side-database resolves its data through
            // the native AcPp layer which keys off HostApplicationServices.
            // WorkingDatabase — if we leave the working DB pointing at the
            // active document we get RowNotInThisDatabase exceptions on the
            // side rows. Swap it for the duration of the read.
            var prevWorking = HostApplicationServices.WorkingDatabase;
            var swapped = false;
            bool prevCaching = true;
            bool prevProjCaching = true;

            try
            {
                if (db != prevWorking)
                {
                    try { HostApplicationServices.WorkingDatabase = db; swapped = true; }
                    catch (System.Exception ex)
                    {
                        // Not always allowed — fall back to per-row try/catch.
                        // Flag it: a failed swap is a common root cause of
                        // rows silently dropping with RowNotInThisDatabase.
                        result.WorkingDbSwapFailed = true;
                        Trace($"PartReader: swap WorkingDatabase не удался — {ex.GetType().Name}: {ex.Message}");
                    }
                }

                // Every step below used to be `try { … } catch { }`. The
                // calls themselves are kept (they are the empirical freshness
                // sequence — see the comment further down), but failures are
                // now written to the run log so a stale read can be traced.
                void Step(string what, System.Action action)
                {
                    try { action(); }
                    catch (System.Exception ex)
                    { Trace($"PartReader: {what} не удался — {ex.GetType().Name}: {ex.Message}"); }
                }

                Step("dlm.EnableCaching (чтение)", () => prevCaching = dlm.EnableCaching);
                Step("dlm.EnableCaching=false", () => dlm.EnableCaching = false);

                // Flush pending in-memory changes to the DataLinks store
                // *before* querying. The active document holds new / edited
                // parts in its Database until the DLM is told to re-sync.
                // AuditDrawing(db) rescans the drawing's PnP blob and is
                // the canonical "give me live state" trigger; the other
                // three calls are belt-and-braces (different Plant 3D
                // versions surface the freshness flag through different
                // APIs).
                Step("dlm.AcceptChanges", () => dlm.AcceptChanges());
                Step("dlm.AuditDrawing", () => dlm.AuditDrawing(db));
                Step("dlm.ReloadLinkMap", () => dlm.ReloadLinkMap());
                Step("dlm.AuditLinks", () => dlm.AuditLinks());
                Step("dlm.RefreshLinkMap", () =>
                {
                    int dwgId = dlm.GetDrawingId(db);
                    if (dwgId != -1) dlm.RefreshLinkMap(dwgId);
                });
                Step("dlm PnP-кэш свойств", () =>
                {
                    if (!PnpDbFreshness.Flush(dlm.GetPnPDatabase(), "PartReader[dwg PnPDatabase]", Trace))
                        result.PnpReconnectFailed = true;
                });

                if (projectDlm != null)
                {
                    // projectDlm is the LIVE project store Plant 3D keeps open
                    // for the whole session. AcceptChanges here commits any
                    // pending DLM changes of the session — a side effect of a
                    // read command, kept because it is part of the freshness
                    // sequence that makes edits since the last save visible
                    // (removing it blindly risks reading stale tags). The
                    // Disconnect/Connect pair is the dangerous part and is
                    // guarded in PnpDbFreshness.Flush.
                    Step("projectDlm.EnableCaching (чтение)", () => prevProjCaching = projectDlm.EnableCaching);
                    Step("projectDlm.EnableCaching=false", () => projectDlm.EnableCaching = false);
                    Step("projectDlm.AcceptChanges", () => projectDlm.AcceptChanges());
                    Step("projectDlm.AuditDrawing", () => projectDlm.AuditDrawing(db));
                    Step("projectDlm.ReloadLinkMap", () => projectDlm.ReloadLinkMap());
                    Step("projectDlm.AuditLinks", () => projectDlm.AuditLinks());
                    Step("projectDlm.RefreshLinkMap", () =>
                    {
                        int projDwgId = projectDlm.GetDrawingId(db);
                        if (projDwgId != -1) projectDlm.RefreshLinkMap(projDwgId);
                    });
                    Step("projectDlm PnP-кэш свойств", () =>
                    {
                        if (!PnpDbFreshness.Flush(projectDlm.GetPnPDatabase(), "PartReader[project PnPDatabase]", Trace))
                            result.PnpReconnectFailed = true;
                    });
                }

                // Suppress the "restore EnableCaching after read" path for
                // projectDlm — Plant 3D keeps that DataLinksManager alive
                // for the whole AutoCAD session and any cached row map
                // outlives our finally block. Keep it off permanently
                // (PartReader is fast enough that this is harmless).
                prevProjCaching = false;

                // SelectAcPpRowIds is the documented read-only query. The
                // earlier attempt with BeginSaveDwg/EndSaveDwg tried to
                // grab the "live save" snapshot but that pair mutates DLM
                // state and broke quiet writes from Plant 3D. The
                // AcceptChanges + AuditDrawing + ReloadLinkMap sequence
                // above already forces the row map to rebuild against the
                // live blob, so plain SelectAcPpRowIds returns fresh data.
                Autodesk.ProcessPower.DataObjects.PnPRowIdArray rowIds;
                try { rowIds = dlm.SelectAcPpRowIds(db); }
                catch (System.Exception ex)
                {
                    Trace($"PartReader: SelectAcPpRowIds не удался — {ex.GetType().Name}: {ex.Message}");
                    throw;
                }

                var rowIdSet = new HashSet<int>();
                if (rowIds != null)
                {
                    foreach (int id in rowIds)
                        rowIdSet.Add(id);
                }

                // Supplement: scan model space for unsaved/new Plant 3D components in memory
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                        if (!msId.IsNull)
                        {
                            var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);
                            foreach (ObjectId entId in ms)
                            {
                                var className = entId.ObjectClass?.Name;
                                if (className != null && className.StartsWith("AcPp", System.StringComparison.OrdinalIgnoreCase))
                                {
                                    int rowId = 0;
                                    try { rowId = dlm.FindAcPpRowId(entId); } catch { }
                                    if (rowId != 0)
                                    {
                                        rowIdSet.Add(rowId);
                                    }
                                }
                            }
                        }
                        tr.Commit();
                    }
                    catch { }
                }

                // Units of Position X/Y/Z for ValveCompanionFinder's
                // proximity radius (see PartRecord.DrawingMmPerUnit).
                double? drawingMmPerUnit = null;
                try { drawingMmPerUnit = MillimetresPerUnit(db.Insunits); }
                catch (System.Exception ex)
                { Trace($"PartReader: INSUNITS не прочитан — {ex.GetType().Name}: {ex.Message}"); }

                foreach (int rowId in rowIdSet)
                {
                    PartRecord rec = null;
                    try
                    {
                        var props = dlm.GetAllProperties(rowId, true);
                        if (props == null)
                        {
                            result.SkippedRows++;
                            result.SkippedRowDetails.Add((rowId, "свойства не прочитаны (GetAllProperties вернул null)"));
                            Trace($"PartReader: rowId {rowId} пропущен — GetAllProperties вернул null.");
                            continue;
                        }
                        rec = BuildRecord(rowId, props, dlm, projectDlm, lineGroupCache, Trace);
                    }
                    catch (System.Exception ex)
                    {
                        // One broken part must not kill the whole spec — but
                        // record it so "a part went missing" is traceable.
                        result.SkippedRows++;
                        result.SkippedRowDetails.Add((rowId, $"{ex.GetType().Name}: {ex.Message}"));
                        Trace($"PartReader: rowId {rowId} пропущен — {ex.GetType().Name}: {ex.Message}");
                    }
                    if (rec == null) continue;
                    rec.DrawingMmPerUnit = drawingMmPerUnit;
                    result.Records.Add(rec);
                }

                // One ownership gate for every path below. Category lookup
                // prefers the record we just read (normalized, so an empty
                // PartCategory on a "Pipe"-class row still counts); rows not
                // in the record set (rare — a neighbour whose properties
                // failed) fall back to a direct DLM read.
                var categoryByRow = new Dictionary<int, string>();
                foreach (var rec in result.Records)
                    categoryByRow[rec.PnPID] = CategoryClassifier.Normalize(rec);
                var guardLogged = new HashSet<int>();
                bool MarkOwned(int id, string via)
                {
                    var isProcurement = categoryByRow.TryGetValue(id, out var cat)
                        ? CategoryClassifier.IsProcurementCategory(cat)
                        : IsProcurementCategory(dlm, id);
                    if (isProcurement)
                    {
                        result.OwnershipGuardKept++;
                        if (guardLogged.Add(id))
                            Trace($"{via}: rowId {id} НЕ помечен owned — закупочная категория (труба/деталь/арматура/фланец), оставляем в спецификации.");
                        return false;
                    }
                    return result.EquipmentOwnedPnPIds.Add(id);
                }

                // Equipment-owned exclusion (manways, internal supports
                // attached to vessels). Plant 3D doesn't always populate
                // a separate "Equipment" PartCategory — vessels often go
                // through with empty/null category. The companion-flange
                // exclusion handled below catches the case the user cares
                // about most (nozzle-style flanges attached to valves or
                // equipment), so this path stays as a safety net only.
                // Runs after the record loop (it used to run inside it) so
                // MarkOwned sees every record's category.
                foreach (var rec in result.Records)
                {
                    if (string.Equals(rec.PartCategory, "Equipment",
                                      System.StringComparison.OrdinalIgnoreCase))
                        CollectAssetOwnership(dlm, rec.PnPID, MarkOwned, Trace);
                }

                // Build topology by walking ConnectionManager.GetConnections
                // on every part ObjectId in model space, yielding the
                // connector-mediated part-to-part graph. GetRelatedRowIds was
                // attempted earlier but came back empty on real projects, so
                // we go through the connection manager directly.
                BuildTopology(db, dlm, result, MarkOwned);
                if (result.OwnershipGuardKept > 0)
                    Trace($"PartReader: {result.OwnershipGuardKept} привязок к оборудованию не применено к закупочным деталям ({guardLogged.Count} уникальных rowId).");
                return result;
            }
            finally
            {
                try { dlm.EnableCaching = prevCaching; } catch { }
                if (projectDlm != null)
                {
                    try { projectDlm.EnableCaching = prevProjCaching; } catch { }
                }
                if (swapped)
                {
                    try { HostApplicationServices.WorkingDatabase = prevWorking; } catch { }
                }
            }
        }

        /// <summary>
        /// Iterate every entity in model space and ask ConnectionManager
        /// for its *direct* P3dPartConnection neighbours. Each Connection
        /// is a single record with ObjectId1/ObjectId2 — the two ports
        /// that physically touch.
        ///
        /// Earlier attempt used ConnectionIterator, which (empirically)
        /// walked the *transitive closure* of the network from the
        /// starting part — every Pipe / Connector / Flange ever reachable
        /// from a valve came back as its neighbour. That produced false
        /// positives in companion detection (every flange in the
        /// connected network got dropped). GetConnections returns just
        /// the immediate edges, so a two-hop Valve → Connector → Flange
        /// walk stays local to the joint.
        /// </summary>
        static void BuildTopology(Database db, DataLinksManager dlm,
                                  PartReadResult result,
                                  System.Func<int, string, bool> markOwned)
        {
            ConnectionManager cm;
            try { cm = new ConnectionManager(); }
            catch { return; }

            // ConnectionManager wraps a native object (DisposableWrapper);
            // BuildTopology runs once per drawing, so without disposing it a
            // project-wide scan leaks one native handle per DWG.
            using (cm)
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    if (msId.IsNull) return;
                    var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

                    foreach (ObjectId entId in ms)
                    {
                        // Fast filter: skip native AutoCAD entities (like lines, circles, blocks)
                        // by checking their class name prefix. All Plant 3D entities start with "AcPp".
                        var className = entId.ObjectClass?.Name;
                        if (className == null || !className.StartsWith("AcPp", System.StringComparison.OrdinalIgnoreCase))
                            continue;

                        int fromRowId = 0;
                        try { fromRowId = dlm.FindAcPpRowId(entId); }
                        catch { /* not a Plant 3D entity */ }

                        // Equipment recognition is independent of a rowId — a
                        // vessel may not produce its own EngineeringItems row,
                        // but its connections still need to flag its child
                        // nozzles as owned. We always probe IsPnP3dEquipment
                        // and route accordingly.
                        bool isEquipment = false;
                        try { isEquipment = Runtime.IsPnP3dEquipment(entId); }
                        catch { }
                        if (isEquipment) result.EquipmentCount++;

                        if (fromRowId == 0 && !isEquipment) continue;

                        ConnectionCollection conns;
                        try { conns = cm.GetConnections(entId); }
                        catch { continue; }
                        if (conns == null) continue;

                        // ConnectionCollection exposes only an indexer + Item
                        // property, no Count. Try indices until it throws.
                        for (var i = 0; ; i++)
                        {
                            Connection c;
                            try { c = conns[i]; }
                            catch { break; }
                            if (c == null) break;
                            var otherId = c.ObjectId1 == entId
                                ? c.ObjectId2
                                : (c.ObjectId2 == entId ? c.ObjectId1 : ObjectId.Null);
                            if (otherId.IsNull) continue;
                            int toRowId = 0;
                            try { toRowId = dlm.FindAcPpRowId(otherId); }
                            catch { continue; }
                            if (toRowId == 0) continue;

                            // Equipment node ⇒ its neighbour is an owned part
                            // (nozzle, manway, internal support). Two-hop walk
                            // also picks up connector-bridged children —
                            // IsPnP3dConnector on the neighbour triggers a
                            // follow-on lookup.
                            // Every mark goes through markOwned: procurement
                            // categories (pipe/fittings/valves/flanges) are
                            // never hidden — see CategoryClassifier.
                            // IsProcurementCategory for the why.
                            if (isEquipment)
                            {
                                if (markOwned(toRowId, "Topology(equipment→neighbour)"))
                                    result.EquipmentNeighbourCount++;
                                // Follow the connector hop regardless of whether
                                // the connector itself was marked — connectors
                                // aren't procurement rows, but the guard must
                                // still be applied to what lies beyond.
                                FollowConnectorChildren(cm, dlm, otherId, toRowId, markOwned);
                            }

                            // The reverse direction matters too: in some
                            // projects the Equipment entity has zero outgoing
                            // P3dPartConnections, but the Nozzle/Flange parts
                            // attached to it list the Equipment (or its nozzle
                            // subpart) as one of *their* neighbours. Detect
                            // either and mark fromRowId as owned.
                            if (!isEquipment && fromRowId != 0)
                            {
                                bool neighbourIsEquipmentLike = false;
                                try
                                {
                                    neighbourIsEquipmentLike =
                                        Runtime.IsPnP3dEquipment(otherId)
                                     || Runtime.IsPnP3dNozzleSubPart(otherId);
                                }
                                catch { }
                                if (neighbourIsEquipmentLike
                                    && markOwned(fromRowId, "Topology(part→equipment/nozzle)"))
                                    result.EquipmentNeighbourCount++;
                            }

                            if (fromRowId != 0 && toRowId != fromRowId)
                            {
                                AddEdge(result.Topology, fromRowId, toRowId);
                                AddEdge(result.Topology, toRowId, fromRowId);
                            }
                        }
                    }
                    tr.Commit();
                }
                catch { /* topology is best-effort; spec still works without it */ }
            }
        }

        /// <summary>
        /// If a neighbour of Equipment is a P3dConnector (weld/buttweld),
        /// the part the user sees in the spec is the *other* side of that
        /// connector — typically a Nozzle flange. Follow one hop further
        /// and add that part to the equipment-owned set too.
        /// </summary>
        static void FollowConnectorChildren(ConnectionManager cm,
            DataLinksManager dlm, ObjectId connectorId, int connectorRowId,
            System.Func<int, string, bool> markOwned)
        {
            bool isConnector = false;
            try { isConnector = Runtime.IsPnP3dConnector(connectorId); }
            catch { return; }
            if (!isConnector) return;

            ConnectionCollection conns;
            try { conns = cm.GetConnections(connectorId); }
            catch { return; }
            if (conns == null) return;
            for (var i = 0; ; i++)
            {
                Connection c;
                try { c = conns[i]; }
                catch { break; }
                if (c == null) break;
                var otherId = c.ObjectId1 == connectorId
                    ? c.ObjectId2
                    : (c.ObjectId2 == connectorId ? c.ObjectId1 : ObjectId.Null);
                if (otherId.IsNull) continue;
                bool isEq = false;
                try { isEq = Runtime.IsPnP3dEquipment(otherId); } catch { }
                if (isEq) continue;  // skip back to the vessel itself
                int toRow = 0;
                try { toRow = dlm.FindAcPpRowId(otherId); } catch { }
                if (toRow != 0 && toRow != connectorRowId)
                    markOwned(toRow, "Topology(equipment→connector→part)");
            }
        }

        // Drawing units (INSUNITS) — for coordinates only; part dimensions
        // such as WallThickness are in the part's catalogue LengthUnit.
        // Unitless / exotic units → null ("unknown"; ValveCompanionFinder
        // assumes mm).
        static double? MillimetresPerUnit(UnitsValue units) => units switch
        {
            UnitsValue.Millimeters => 1,
            UnitsValue.Centimeters => 10,
            UnitsValue.Meters => 1000,
            UnitsValue.Inches => 25.4,
            UnitsValue.Feet => 304.8,
            _ => null,
        };

        static void AddEdge(Dictionary<int, List<int>> graph, int from, int to)
        {
            if (!graph.TryGetValue(from, out var list))
                graph[from] = list = new List<int>();
            if (!list.Contains(to)) list.Add(to);
        }

        /// <summary>
        /// Populate <paramref name="ownedIds"/> with PnPIDs the given
        /// equipment row owns. Tries the AssetOwnership relationship
        /// explicitly first, then falls back to the bare related-rows
        /// query — Plant 3D's wrappers don't expose the schema so we
        /// can't pick a single overload with certainty.
        /// </summary>
        static void CollectAssetOwnership(DataLinksManager dlm, int rowId,
                                          System.Func<int, string, bool> markOwned,
                                          System.Action<string> trace = null)
        {
            // Try the explicit relationship overload with several names
            // it could plausibly be registered under. AcceptedRelations
            // is empirical — Plant 3D's docs don't list them and the
            // taxonomy varies between releases.
            string[] candidates =
            {
                "AssetOwnership", "EquipmentOwnership",
                "PnPRelationshipDef:AssetOwnership",
            };
            foreach (var rel in candidates)
            {
                try
                {
                    var related = dlm.GetRelatedRowIds("EngineeringItems",
                                                       "EngineeringItems",
                                                       rowId, rel);
                    if (related != null && related.Count > 0)
                    {
                        // Guarded like every other ownership path: even an
                        // explicit relationship must not hide a pipe/valve.
                        foreach (int id in related)
                            if (id != rowId) markOwned(id, $"AssetOwnership({rel})");
                        return;
                    }
                }
                catch { /* try the next candidate */ }
            }

            // Fallback: bare related-rows. This is imprecise — it returns
            // every related row regardless of relationship type, so a pipe
            // run merely touching the vessel comes back here. Marking such a
            // row "owned" would silently DROP it from the spec, which for a
            // procurement document is the worst kind of error (under-count
            // that looks complete). So we guard the fallback: only exclude
            // rows whose own category is NOT a procurement category. Pipes,
            // fittings, valves and flanges are never equipment-owned in a way
            // that should hide them — mirrors the safety pass in
            // ValveCompanionFinder.
            try
            {
                var related = dlm.GetRelatedRowIds(rowId);
                if (related == null) return;
                foreach (int id in related)
                {
                    if (id == rowId) continue;
                    if (markOwned(id, "AssetOwnership(fallback)"))
                        trace?.Invoke($"AssetOwnership(fallback): rowId {id} помечен owned для оборудования {rowId}.");
                }
            }
            catch (System.Exception ex)
            {
                trace?.Invoke($"AssetOwnership(fallback): GetRelatedRowIds({rowId}) не удался — {ex.GetType().Name}: {ex.Message}");
            }
        }

        // DLM fallback for rows that are not in the record set (their own
        // property read failed). Reads the single PartCategory property;
        // any failure is treated as "not a procurement category" so
        // behaviour stays conservative (the row can still be excluded as
        // before). Records we did read go through CategoryClassifier.
        static bool IsProcurementCategory(DataLinksManager dlm, int rowId)
        {
            string cat = null;
            try
            {
                var props = dlm.GetAllProperties(rowId, true);
                if (props != null)
                    foreach (var kv in props)
                        if (kv.Key == "PartCategory") { cat = kv.Value; break; }
            }
            catch { return false; }
            return CategoryClassifier.IsProcurementCategory((cat ?? "").Trim());
        }

        /// <summary>
        /// Walk P3dLineGroupPartRelationship to find the LineGroup row
        /// for this part, then read its current Tag. Resolved through the
        /// drawing-local DLM only: partRowId lives in that id space, and a
        /// project-DLM lookup with it can hit an unrelated row (see body).
        /// The drawing reads are always from disk (side-Database), so its
        /// P3dLineGroup copy reflects the last save.
        /// </summary>
        static string ResolveLiveLineTag(int partRowId,
            DataLinksManager drawingDlm,
            DataLinksManager projectDlm,
            Dictionary<(bool project, int id), string> lineGroupCache,
            System.Action<string> trace,
            System.Action<string> partWarning = null)
        {
            // Query drawing-local DLM first since partRowId is drawing-local.
            // Drawing-local and project-wide PnPIDs belong to different
            // namespaces — querying projectDlm first with
            // a drawing-local partRowId can match an unrelated row (e.g. a connector)
            // and return the wrong line tag.
            //
            // There used to be a fallback here that re-ran the same query
            // against projectDlm when the drawing DLM found nothing ("shares
            // namespace in SQL Server/vault"). It does not, in general: the
            // drawing-local rowId fed into the project DLM can name a
            // completely different part there, and the result was a line tag
            // borrowed from someone else's row — a wrong line in the spec
            // that looks perfectly valid. There is no reliable way to verify
            // that a project rowId is "the same part" from here, so the
            // fallback is gone. Returning null makes PartPropertyReader fall
            // back to the part's OWN LineNumberTag property: possibly stale
            // after a rename, but never foreign.
            int lgRowId = QueryLineGroupRowId(drawingDlm, partRowId, trace, partWarning);
            if (lgRowId == 0) return null;

            var cacheKey = (false, lgRowId);
            if (lineGroupCache.TryGetValue(cacheKey, out var cached))
                return cached;

            // The LineGroup rowId came from the drawing DLM, so read its Tag
            // there; projectDlm is only consulted when it is literally the
            // same manager instance (same id space by construction).
            var tag = ReadTag(drawingDlm, lgRowId)
                ?? (ReferenceEquals(projectDlm, drawingDlm) ? ReadTag(projectDlm, lgRowId) : null);

            lineGroupCache[cacheKey] = tag;
            return tag;
        }

        /// <summary>
        /// LineGroup row of a part. A part normally belongs to exactly one
        /// line group; if Plant 3D reports several, the first one is still
        /// used (unchanged behaviour — there is no rule to pick "the right"
        /// one here), but it is now written to the run log so a part that
        /// lands on an unexpected line can be traced to the ambiguous link
        /// instead of the choice being silent.
        /// </summary>
        static int QueryLineGroupRowId(DataLinksManager dlmToTry, int partRowId,
                                       System.Action<string> trace,
                                       System.Action<string> partWarning = null)
        {
            if (dlmToTry == null) return 0;
            try
            {
                var related = dlmToTry.GetRelatedRowIds(
                    "P3dLineGroupPartRelationship", "Part", partRowId, "LineGroup");
                if (related == null) return 0;
                var groups = new List<int>();
                foreach (int id in related)
                    if (id != 0 && !groups.Contains(id)) groups.Add(id);
                if (groups.Count == 0) return 0;
                if (groups.Count > 1)
                {
                    trace?.Invoke($"ВНИМАНИЕ: деталь rowId {partRowId} связана с несколькими группами линий "
                        + $"({string.Join(", ", groups)}); использована первая — {groups[0]}.");
                    partWarning?.Invoke($"Деталь связана с несколькими группами линий ({groups.Count}) — номер линии взят из первой");
                }
                return groups[0];
            }
            catch (System.Exception ex)
            {
                trace?.Invoke($"PartReader: группа линии для rowId {partRowId} не прочитана — {ex.GetType().Name}: {ex.Message}");
                partWarning?.Invoke("Группа линии не прочитана — номер линии взят из свойства детали (может быть устаревшим)");
            }
            return 0;
        }

        static string ReadTag(DataLinksManager dlmToTry, int lgRowId)
        {
            if (dlmToTry == null) return null;
            try
            {
                var props = dlmToTry.GetAllProperties(lgRowId, true);
                if (props == null) return null;
                foreach (var kv in props)
                    if (string.Equals(kv.Key, "Tag", System.StringComparison.OrdinalIgnoreCase))
                        return kv.Value;
            }
            catch { }
            return null;
        }

        static PartRecord BuildRecord(int rowId,
            List<System.Collections.Generic.KeyValuePair<string, string>> props,
            DataLinksManager dlm,
            DataLinksManager projectDlm,
            Dictionary<(bool project, int id), string> lineGroupCache,
            System.Action<string> trace)
        {
            string lineWarning = null;
            var tag = ResolveLiveLineTag(rowId, dlm, projectDlm, lineGroupCache,
                                         trace, w => lineWarning = w);
            var rec = PartPropertyReader.Build(rowId, props, tag);
            // Parked on the record, not reported here: only SpecRowBuilder
            // knows whether the part survives the user's filters.
            if (rec != null && lineWarning != null)
                (rec.ReadWarnings ??= new List<string>()).Add(lineWarning);
            return rec;
        }
    }
}
