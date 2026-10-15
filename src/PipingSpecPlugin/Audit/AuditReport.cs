using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace PipingSpecPlugin.Audit
{
    public static class AuditReport
    {
        public static string Serialize(AuditSnapshot snapshot, AuditOptions options, List<AuditIssue> issues) =>
            JsonSerializer.Serialize(new { SchemaVersion = 2, CreatedUtc = DateTime.UtcNow, snapshot.Drawing,
                Scope = (options.SelectedLines == null ? "Все детали текущего чертежа" : "Выбранные линии текущего чертежа")
                    + "; коллизии — со всеми объектами и загруженными внешними ссылками (полная геометрия без XCLIP)", Checks = options.Enabled, options.SelectedLines, options.WallToleranceMm,
                PartCount = snapshot.Parts.Count, snapshot.CollisionPairsChecked, snapshot.NonVolumetricCount, snapshot.SupportContactsIgnored, snapshot.ConnectedJointsIgnored,
                snapshot.XrefInstancesChecked, snapshot.XrefInstancesUnavailable, Issues = issues },
                new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
