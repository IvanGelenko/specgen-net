using Autodesk.AutoCAD.DatabaseServices;
namespace IsoGenerator.AutoCAD;
internal static class IsoEnvironmentChecks
{
    public static string PdfDevice()
    {
        var devices=PlotSettingsValidator.Current.GetPlotDeviceList().Cast<string>().ToArray();
        return devices.FirstOrDefault(s=>s.Equals("DWG To PDF.pc3",StringComparison.OrdinalIgnoreCase))
            ??devices.FirstOrDefault(s=>s.StartsWith("AutoCAD PDF",StringComparison.OrdinalIgnoreCase)&&s.EndsWith(".pc3",StringComparison.OrdinalIgnoreCase))
            ??throw new System.IO.FileNotFoundException("Не найдена конфигурация печати Autodesk PDF (.pc3). Восстановите стандартные плоттеры в настройках AutoCAD Plant 3D.");
    }
}
