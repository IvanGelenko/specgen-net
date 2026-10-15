using System;
using Autodesk.ProcessPower.DataObjects;

namespace PipingSpecPlugin.Spec
{
    /// <summary>
    /// Flushes a PnPDatabase's property-value cache so GetAllProperties
    /// returns live values (Status, Tag, … edited since the last read):
    /// CacheAlways=false → Disconnect/Connect → AcceptChanges →
    /// SynchronizeMetadata → RefreshCachedRows on every table. The sequence
    /// is empirical (different Plant 3D builds surface freshness through
    /// different calls) and is kept as-is — only its failure handling
    /// changes here.
    ///
    /// Why this lives in one place: for the PROJECT DLM the PnPDatabase is
    /// the live project store Plant 3D keeps open for the whole AutoCAD
    /// session. The old inline code was a row of `try { … } catch { }`:
    /// if Connect() failed after a successful Disconnect() the project stayed
    /// disconnected until AutoCAD was restarted, and nobody was told. Now:
    ///   • Connect() always runs after a Disconnect() attempt (finally);
    ///   • a failed Connect() is retried once;
    ///   • if the store is still disconnected the method returns false —
    ///     callers treat that as an incomplete read, never as success;
    ///   • every swallowed exception is written to the run log.
    /// </summary>
    internal static class PnpDbFreshness
    {
        /// <returns>
        /// false only when Disconnect() succeeded and Connect() failed twice
        /// — the store is known to be left disconnected. All other failures
        /// are logged and tolerated (they were tolerated before, too).
        /// </returns>
        public static bool Flush(PnPDatabase pnpDb, string label, Action<string> trace)
        {
            if (pnpDb == null) return true;
            void Log(string what, Exception ex)
            {
                try { trace?.Invoke($"{label}: {what} не удался — {ex.GetType().Name}: {ex.Message}"); } catch { }
            }

            try { pnpDb.CacheAlways = false; } catch (Exception ex) { Log("CacheAlways=false", ex); }

            var disconnected = false;
            var reconnected = false;
            try
            {
                pnpDb.Disconnect();
                disconnected = true;
            }
            catch (Exception ex) { Log("Disconnect", ex); }
            finally
            {
                // Run Connect even when Disconnect threw: a partial
                // disconnect is possible and the extra Connect on an
                // already-open store is at worst a logged no-op.
                for (var attempt = 1; attempt <= 2 && !reconnected; attempt++)
                {
                    try { pnpDb.Connect(); reconnected = true; }
                    catch (Exception ex) { Log($"Connect (попытка {attempt})", ex); }
                }
            }
            if (disconnected && !reconnected)
            {
                try { trace?.Invoke($"{label}: ВНИМАНИЕ — база PnP осталась отключена после Disconnect(); чтение недостоверно. Перезапустите AutoCAD."); } catch { }
                return false;
            }

            try { pnpDb.AcceptChanges(); } catch (Exception ex) { Log("AcceptChanges", ex); }
            try { pnpDb.SynchronizeMetadata(); } catch (Exception ex) { Log("SynchronizeMetadata", ex); }
            try
            {
                foreach (PnPTable table in pnpDb.Tables)
                    try { table.RefreshCachedRows(); }
                    catch (Exception ex) { Log($"RefreshCachedRows({SafeName(table)})", ex); }
            }
            catch (Exception ex) { Log("перебор Tables", ex); }
            return true;
        }

        static string SafeName(PnPTable t)
        {
            try { return t.Name; } catch { return "?"; }
        }
    }
}
