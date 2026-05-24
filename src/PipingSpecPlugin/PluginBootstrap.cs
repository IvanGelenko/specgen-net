using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PipingSpecPlugin
{
    /// <summary>
    /// AutoCAD calls <see cref="IExtensionApplication.Initialize"/> exactly
    /// once per session when our assembly is loaded (via NETLOAD or the
    /// AutoLoader bundle). We use it to register a native ribbon tab —
    /// the Python edition has to ship a .cuix file and ask users to load
    /// it; doing it in code here removes that whole step.
    /// Ribbon registration is wrapped in try/catch because a malformed
    /// ribbon node can prevent the plug-in from loading entirely, which
    /// would also block the commands. Better to log and continue.
    /// </summary>
    public class PluginBootstrap : IExtensionApplication
    {
        public void Initialize()
        {
            try { TryRegisterRibbon(); }
            catch (System.Exception ex)
            {
                var editor = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
                editor?.WriteMessage("\n[PipingSpecPlugin] Ribbon registration failed: "
                    + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public void Terminate() { /* nothing to clean up yet */ }

        static void TryRegisterRibbon()
        {
            // ComponentManager.Ribbon can be null when AutoCAD is still
            // starting; in that case AutoCAD raises ItemInitialized once
            // the ribbon is live. Hook that and retry.
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null)
            {
                ComponentManager.ItemInitialized += OnRibbonReady;
                return;
            }
            BuildTab(ribbon);
        }

        static void OnRibbonReady(object sender, RibbonItemEventArgs e)
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;
            ComponentManager.ItemInitialized -= OnRibbonReady;
            BuildTab(ribbon);
        }

        static void BuildTab(RibbonControl ribbon)
        {
            const string tabId = "PipingSpecPlugin_Tab";
            // Re-registration is harmless if we already added the tab
            // (e.g. NETLOAD'd twice in one session); short-circuit.
            foreach (var t in ribbon.Tabs)
                if (t.Id == tabId) return;

            var tab = new RibbonTab
            {
                Title = "Спецификация",
                Id = tabId,
            };

            var panelSource = new RibbonPanelSource { Title = "Спецификация" };
            panelSource.Items.Add(MakeButton("Сгенерировать", "WRITESPEC",
                "Открыть окно выбора фильтров и сгенерировать спецификацию (.docx).",
                "icon32.png"));

            tab.Panels.Add(new RibbonPanel { Source = panelSource });
            ribbon.Tabs.Add(tab);
        }

        static RibbonButton MakeButton(string label, string command,
                                        string tooltip, string iconName = null)
        {
            var btn = new RibbonButton
            {
                Text = label,
                ShowText = true,
                Size = RibbonItemSize.Large,
                Orientation = Orientation.Vertical,
                ToolTip = tooltip,
                CommandHandler = new RelayCommand(() => RunAcadCommand(command)),
            };
            var icon = iconName != null ? LoadEmbeddedIcon(iconName) : null;
            if (icon != null)
            {
                btn.LargeImage = icon;
                btn.Image = icon;
                btn.ShowImage = true;
            }
            else
            {
                btn.ShowImage = false;
            }
            return btn;
        }

        /// <summary>
        /// Load a PNG packaged as an embedded resource into the plug-in
        /// assembly. Returning null when missing keeps the button
        /// usable (text-only fall-back) instead of crashing the panel.
        /// </summary>
        static System.Windows.Media.ImageSource LoadEmbeddedIcon(string fileName)
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                // Embedded resources land under "<RootNamespace>.<path>";
                // the file lives in Resources/ so the resource name is
                // "PipingSpecPlugin.Resources.<fileName>".
                var resName = "PipingSpecPlugin.Resources." + fileName;
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream == null) return null;
                var img = new System.Windows.Media.Imaging.BitmapImage();
                img.BeginInit();
                img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                img.StreamSource = stream;
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch
            {
                return null;
            }
        }

        static void RunAcadCommand(string command)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute(command + " ", true, false, true);
        }
    }

    /// <summary>
    /// Minimal ICommand for ribbon buttons. AutoCAD's RibbonButton routes
    /// clicks through this — we just forward to a callback. We don't
    /// pull in MVVM Toolkit just for this one type.
    /// </summary>
    internal class RelayCommand : System.Windows.Input.ICommand
    {
        readonly Action _action;
        public RelayCommand(Action action) { _action = action; }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _action();
        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }
    }
}
