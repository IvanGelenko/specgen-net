using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.PnP3dObjects;

namespace PipingSpecPlugin.Audit
{
    public static partial class DrawingAuditReader
    {
        static void ReadSupportContacts(Entity entity, AuditPart info, AuditSnapshot snapshot)
        {
            info.IsPipingComponent = entity is Pipe || entity is InlineAsset || entity is Connector;
            info.IsSupport = entity is Support;
            if (!info.IsSupport || entity.ObjectId.IsNull) return;
            try
            {
                // Read symbolic support attachments independently of the user's
                // choice to run the separate port/connectivity audit.
                using var manager = new ConnectionManager();
                var links = manager.GetConnections(entity.ObjectId);
                if (links == null) return;
                string prefix = info.Key.Contains('/') ? info.Key.Substring(0, info.Key.LastIndexOf('/') + 1) : "";
                for (int i = 0; ; i++)
                {
                    Connection link;
                    try { link = links[i]; }
                    catch (IndexOutOfRangeException) { break; }
                    catch (ArgumentOutOfRangeException) { break; }
                    if (link == null) break;
                    var other = link.ObjectId1 == entity.ObjectId ? link.ObjectId2 :
                        link.ObjectId2 == entity.ObjectId ? link.ObjectId1 : ObjectId.Null;
                    if (!other.IsNull && !other.IsErased && other.Database == entity.Database)
                        info.SupportedPartKeys.Add(prefix + other.Handle);
                }
            }
            catch (Exception ex)
            {
                info.SupportedPartKeys.Clear();
                snapshot.GeometryIssues.Add(AuditRules.Issue("collisions", "Коллизии", "Не проверено",
                    "Не удалось прочитать присоединение опоры; её пересечения не исключены. " + ex.Message, new[] { info }));
            }
        }
    }
}
