using System;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using PipingSpecPlugin.Update;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PipingSpecPlugin.UI
{
    /// <summary>
    /// "О программе" dialog — shows the plug-in name, version, a short
    /// feature list and a link to the repository. The version comes from
    /// the running assembly (UpdateService.CurrentVersion), so it always
    /// matches what the updater compares against.
    /// </summary>
    public partial class AboutWindow : Window
    {
        const string RepoUrl = "https://github.com/IvanGelenko/specgen-net";

        public AboutWindow()
        {
            InitializeComponent();

            var asm = Assembly.GetExecutingAssembly();
            ProductText.Text = ReadAttribute<AssemblyProductAttribute>(asm,
                a => a.Product, "Генератор спецификации Plant 3D");
            DescriptionText.Text = ReadAttribute<AssemblyDescriptionAttribute>(asm,
                a => a.Description, "Плагин для AutoCAD Plant 3D.");
            VersionText.Text = $"Версия {UpdateService.CurrentVersion()}";

            FeaturesText.Text = string.Join(Environment.NewLine, new[]
            {
                "Плагин формирует спецификацию трубопроводов (ведомость",
                "изделий и материалов) по данным модели AutoCAD Plant 3D и",
                "выводит её в документ Word на основе пользовательского шаблона.",
                "Также готовит данные для расчёта тепловой изоляции:",
                "трубы и фитинги по линиям (панель «Изоляция»).",
            });
        }

        static string ReadAttribute<T>(Assembly asm, Func<T, string> pick, string fallback)
            where T : Attribute
        {
            try
            {
                var attr = asm.GetCustomAttribute<T>();
                var val = attr == null ? null : pick(attr);
                return string.IsNullOrWhiteSpace(val) ? fallback : val;
            }
            catch { return fallback; }
        }

        async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdatesButton.IsEnabled = false;
            try
            {
                var info = await UpdateService.CheckAsync();
                if (info != null && info.UpdateAvailable)
                {
                    var win = new UpdateWindow(info);
                    new WindowInteropHelper(win).Owner = AcadApp.MainWindow.Handle;
                    win.ShowDialog();
                }
                else
                {
                    MessageBox.Show(this,
                        "У вас установлена последняя версия.",
                        "Проверка обновлений",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Не удалось проверить обновления: " + ex.Message,
                    "Проверка обновлений",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                CheckUpdatesButton.IsEnabled = true;
            }
        }

        void RepoLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = RepoUrl,
                    UseShellExecute = true,
                });
            }
            catch { /* shell refused — nothing actionable */ }
        }

        void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
