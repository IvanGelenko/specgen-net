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
    /// AutoLoader bundle). We use it to register a native ribbon tab in
    /// code, so no .cuix file has to be shipped or loaded by the user.
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

            try { HookWorkspaceReload(); }
            catch { /* the tab is still there for this workspace */ }

            try { TryCheckForUpdates(); }
            catch { /* updates must never block or break load */ }

            // The embedded MCP server (Claude <-> Plant 3D) is off unless the user opted in (PSMCP / PLANT3D_MCP_AUTOSTART=1 / autostart file).
            try { Mcp.McpServer.AutoStart(); }
            catch { /* the server must never break the load */ }
        }

        /// <summary>
        /// Kick off a background check against GitHub Releases. Runs fire-
        /// and-forget so plug-in load isn't blocked by the network; if a
        /// newer build is found we marshal back onto the AutoCAD UI thread
        /// (captured here) to show the modal notification. Honours the
        /// CheckForUpdates opt-out and stays completely silent on failure.
        /// </summary>
        static void TryCheckForUpdates()
        {
            if (!Settings.PluginSettings.Load().CheckForUpdates) return;

            // Capture the UI-thread dispatcher now, while we're on it.
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

            // Fire-and-forget by design: the check must not block load. The
            // discard documents that the un-awaited task is intentional.
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var info = await Update.UpdateService.CheckAsync().ConfigureAwait(false);
                if (info == null || !info.UpdateAvailable) return;

                _ = dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var win = new UI.UpdateWindow(info);
                        new System.Windows.Interop.WindowInteropHelper(win).Owner =
                            AcadApp.MainWindow.Handle;
                        win.ShowDialog();
                    }
                    catch { /* never let the notification crash AutoCAD */ }
                }));
            });
        }

        public void Terminate()
        {
            try { Mcp.McpServer.Stop(); } catch { }
            // Session end: detach what Initialize attached (AutoCAD is going
            // away anyway, but a handler outliving the plug-in is never
            // useful).
            try
            {
                if (_hookedPaletteSet != null) _hookedPaletteSet.WorkspaceLoaded -= OnWorkspaceLoaded;
                Autodesk.AutoCAD.Ribbon.RibbonServices.RibbonPaletteSetCreated -= OnRibbonPaletteSetCreated;
                if (_waitingForRibbon) ComponentManager.ItemInitialized -= OnRibbonReady;
            }
            catch { }
        }

        // True while OnRibbonReady is subscribed — one subscription at a
        // time, however often TryRegisterRibbon is called.
        static bool _waitingForRibbon;

        static void TryRegisterRibbon()
        {
            // At plug-in load the ribbon is often not built yet
            // (ComponentManager.Ribbon == null). If so, wait for the
            // ItemInitialized event and build the tab once it appears.
            // We unsubscribe as soon as the tab exists — keeping the handler
            // live for the whole session means it fires on EVERY ribbon item
            // initialisation (hundreds of times) and re-scans Tabs each time.
            // Re-creating the tab after a workspace switch is
            // OnWorkspaceLoaded's job, not this handler's.
            if (TryBuildTab()) return;
            if (_waitingForRibbon) return;
            _waitingForRibbon = true;
            ComponentManager.ItemInitialized += OnRibbonReady;
        }

        static void OnRibbonReady(object sender, RibbonItemEventArgs e)
        {
            try
            {
                if (TryBuildTab())
                {
                    ComponentManager.ItemInitialized -= OnRibbonReady;
                    _waitingForRibbon = false;
                }
            }
            catch { /* an exception out of a ribbon event must not reach AutoCAD */ }
        }

        // AutoCAD rebuilds the ribbon from the CUIx whenever a workspace is
        // loaded — switching workspace (3D Piping ↔ Isometric / Ortho …),
        // CUILOAD / MENULOAD — and a tab added from code is not in the CUIx,
        // so «Спецификация» vanished until the next AutoCAD start. (The
        // first version kept ItemInitialized subscribed for the whole
        // session for this; it was removed for firing hundreds of times,
        // which also removed the re-creation.) RibbonPaletteSet.WorkspaceLoaded
        // fires once per (re)load — the documented hook for this. The palette
        // set itself may not exist yet at load (ribbon closed), hence
        // RibbonPaletteSetCreated.
        static Autodesk.AutoCAD.Ribbon.RibbonPaletteSet _hookedPaletteSet;

        static void HookWorkspaceReload()
        {
            var ps = Autodesk.AutoCAD.Ribbon.RibbonServices.RibbonPaletteSet;
            if (ps != null) AttachWorkspaceLoaded(ps);
            else Autodesk.AutoCAD.Ribbon.RibbonServices.RibbonPaletteSetCreated += OnRibbonPaletteSetCreated;
        }

        static void OnRibbonPaletteSetCreated(object sender, EventArgs e)
        {
            try
            {
                Autodesk.AutoCAD.Ribbon.RibbonServices.RibbonPaletteSetCreated -= OnRibbonPaletteSetCreated;
                var ps = Autodesk.AutoCAD.Ribbon.RibbonServices.RibbonPaletteSet;
                if (ps != null) AttachWorkspaceLoaded(ps);
            }
            catch { }
        }

        static void AttachWorkspaceLoaded(Autodesk.AutoCAD.Ribbon.RibbonPaletteSet ps)
        {
            if (ReferenceEquals(_hookedPaletteSet, ps)) return;
            ps.WorkspaceLoaded += OnWorkspaceLoaded;
            _hookedPaletteSet = ps;
        }

        static void OnWorkspaceLoaded(object sender, EventArgs e)
        {
            // BuildTab is idempotent (looks the tab up by Id), so a reload
            // that kept our tab adds nothing.
            try { TryRegisterRibbon(); }
            catch { /* never let a ribbon event take AutoCAD down */ }
        }

        /// <summary>
        /// Build the tab if the ribbon is available. Returns true when the
        /// tab now exists (built just now or already present) — the signal
        /// to stop listening; false only when the ribbon isn't ready yet.
        /// </summary>
        static bool TryBuildTab()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return false;
            BuildTab(ribbon);
            return true;
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
            panelSource.Items.Add(MakeButton("Весь проект", "WRITESPECPROJ",
                "Сгенерировать спецификацию по всему проекту Plant 3D (.docx).",
                "icon32.png"));
            panelSource.Items.Add(MakeButton("Текущий чертёж", "WRITESPECDWG",
                "Сгенерировать спецификацию только по активному чертежу (.docx).",
                "icon32.png"));
            tab.Panels.Add(new RibbonPanel { Source = panelSource });

            var insulationSource = new RibbonPanelSource { Title = "Изоляция" };
            insulationSource.Items.Add(MakeButton("Весь проект", "INSULSPECPROJ",
                "Данные для расчёта изоляции по всему проекту Plant 3D (.docx): по каждой линии — "
                + "длины труб с изоляцией и отводы, изгибы, тройники."));
            insulationSource.Items.Add(MakeButton("Текущий чертёж", "INSULSPECDWG",
                "Данные для расчёта изоляции только по активному чертежу (.docx)."));
            insulationSource.Items.Add(MakeButton("Методика", "INSULHELP",
                "Как собираются данные для расчёта изоляции: какие детали попадают, подсчёт как в "
                + "спецификации, изгибы (Д1 + Д2 к трубе)."));
            tab.Panels.Add(new RibbonPanel { Source = insulationSource });

            var layersSource = new RibbonPanelSource { Title = "Слои" };
            layersSource.Items.Add(MakeButton("Выбрать по слою", "SELECTBYLAYER",
                "Выделить все элементы выбранного слоя в текущем пространстве чертежа."));
            layersSource.Items.Add(MakeButton("Цвет по слою", "PSCOLORBYLAYER",
                "Назначить объектам цвет их слоя. Клапанам на отдельных слоях — цвет слоя труб соответствующей линии."));
            layersSource.Items.Add(MakeButton("Разнести по слоям", "MOVEBYTAG",
                "Перенести арматуру и оборудование активного чертежа на слои "
                + "по их идентификатору (Tag).",
                "icon32.png"));
            tab.Panels.Add(new RibbonPanel { Source = layersSource });

            var syncSource = new RibbonPanelSource { Title = "Ведомость трубопроводов" };
            syncSource.Items.Add(MakeButton("Синхронизация ВТ", "SYNCVT",
                "Перенести параметры трубопроводов из Excel-файла Ведомости "
                + "трубопроводов (ВТ) в производственные линии Plant 3D по "
                + "совпадению ID.",
                "icon32.png"));
            tab.Panels.Add(new RibbonPanel { Source = syncSource });

            var selectSource = new RibbonPanelSource { Title = "Выбор" };
            selectSource.Items.Add(MakeButton("Выбрать по статусу", "SELECTBYSTATUS",
                "Выделить на чертеже все детали Plant 3D с заданным статусом "
                + "строительства (например, «Существующее», «Новое», «Под снос»).",
                "icon32.png"));
            selectSource.Items.Add(MakeButton("Выбрать по линии", "SELECTBYLINE",
                "Выделить на чертеже все детали Plant 3D с заданным номером "
                + "линии (идентификатором линии).",
                "icon32.png"));
            tab.Panels.Add(new RibbonPanel { Source = selectSource });

            var geometrySource = new RibbonPanelSource { Title = "Геометрия" };
            geometrySource.Items.Add(MakeButton("В 3D-тело", "PSTOSOLID",
                "Превратить выбранные объекты в одно 3D-тело: расчленить детали, блоки и внешние ссылки "
                + "на любую глубину, объединить тела и сохранить исходные цвета (на гранях тела). "
                + "Исходные объекты — оставить или удалить."));
            tab.Panels.Add(new RibbonPanel { Source = geometrySource });

            var tracingSource = new RibbonPanelSource { Title = "Спутник" };
            tracingSource.Items.Add(MakeButton("Спутник линии", "PSTRACING",
                "Создать спутник обогрева (две трубы DN20 по бокам снизу, вид 3) вдоль выбранных линий: стояки, "
                + "обход арматуры, воздушники и дренажи, замыкание на концах. Линии выбираются в окне со списком; "
                + "алгоритм считает план без изменения чертежа и создаёт детали на слое «Спутник» только после "
                + "подтверждения. Места неизбежной коллизии создаются на фиолетовом слое «Спутник-коллизия»."));
            tab.Panels.Add(new RibbonPanel { Source = tracingSource });

            var mcpSource = new RibbonPanelSource { Title = "Claude" };
            mcpSource.Items.Add(MakeButton("Сервер MCP", "PSMCPTOGGLE",
                "Включить или выключить подключение Claude к этому Plant 3D (локальный сервер MCP на 127.0.0.1:18765, "
                + "доступ только по токену сеанса). Первое нажатие включает, второе выключает. Состояние и автозапуск — команда PSMCP."));
            tab.Panels.Add(new RibbonPanel { Source = mcpSource });

            // Панель «Связи» (CONNECTALL / CONNECTALLDIAG) временно скрыта:
            // «Соединить смежные» работает некорректно и дорабатывается.
            // Команды остаются в сборке (зарегистрированы в AssemblyInfo) —
            // их можно вызвать вручную из командной строки, но с ленты они
            // убраны, чтобы пользователи их не запускали. Чтобы вернуть —
            // раскомментировать блок ниже.
            // var toolsSource = new RibbonPanelSource { Title = "Связи" };
            // toolsSource.Items.Add(MakeButton("Соединить смежные", "CONNECTALL",
            //     "Соединить все смежные детали активного чертежа.",
            //     "icon32.png"));
            // toolsSource.Items.Add(MakeButton("Диагностика связей", "CONNECTALLDIAG",
            //     "Анализ без изменения чертежа: свободные порты и причины отказа.",
            //     "icon32.png"));
            // tab.Panels.Add(new RibbonPanel { Source = toolsSource });

            var helpSource = new RibbonPanelSource { Title = "Справка" };
            var auditSource = new RibbonPanelSource { Title = "Проверка модели" };
            auditSource.Items.Add(MakeButton("Аудит", "PSAUDIT",
                "Проверить толщины стенок, соединения и коллизии в текущем чертеже. Проверки можно выбрать."));
            tab.Panels.Add(new RibbonPanel { Source = auditSource });
            helpSource.Items.Add(MakeButton("О программе", "SPECABOUT",
                "Информация о плагине и его версии.",
                "icon32.png"));
            tab.Panels.Add(new RibbonPanel { Source = helpSource });

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
            var icon = PipingSpecPlugin.UI.CommandIcons.Create(command);
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
