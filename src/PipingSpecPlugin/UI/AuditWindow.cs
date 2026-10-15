using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PipingSpecPlugin.Audit;

namespace PipingSpecPlugin.UI
{
    public sealed class AuditWindow : Window
    {
        readonly Dictionary<string, CheckBox> _choices = new();
        readonly Dictionary<string, CheckBox> _lines = new();
        readonly Expander _settings = new() { IsExpanded = true, Margin = new Thickness(0, 8, 0, 0) };
        readonly TextBox _tolerance = new() { Width = 70, Margin = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
        readonly DataGrid _grid = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            SelectionMode = DataGridSelectionMode.Extended, Margin = new Thickness(0, 12, 0, 12) };
        readonly TextBlock _summary = new() { Text = "Выберите проверки и нажмите «Проверить».", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        readonly Button _run = new() { Content = "Проверить", Width = 125, Height = 32 };
        readonly Button _show = new() { Content = "Показать в модели", Width = 175, Height = 32, IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
        readonly Button _select = new() { Content = "Выделить и закрыть", Width = 175, Height = 32, IsEnabled = false };
        readonly Action<Window, IReadOnlyList<AuditIssue>> _showInModel;
        readonly Button _export = new() { Content = "Сохранить отчёт", Width = 160, Height = 32, IsEnabled = false, Margin = new Thickness(8, 0, 8, 0) };
        List<AuditIssue> _issues;
        AuditSnapshot _snapshot;
        AuditOptions _lastOptions;
        string _runSummary;
        public List<string> SelectedKeys { get; private set; } = new();

        // showInModel: highlight the issues' elements in the drawing and come
        // back (AuditModelView); null hides «Показать в модели».
        public AuditWindow(string drawing, IReadOnlyList<AuditLineChoice> lines, Func<AuditOptions, AuditSnapshot> read,
            Action<Window, IReadOnlyList<AuditIssue>> showInModel = null, double collisionToleranceMm = 0)
        {
            _tolerance.Text = collisionToleranceMm.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);
            _showInModel = showInModel;
            if (showInModel == null) _show.Visibility = Visibility.Collapsed;
            Title = "Аудит модели Plant 3D";
            Width = 1150; Height = 900; MinWidth = 850; MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 13;
            var root = new DockPanel { Margin = new Thickness(16), Background = SystemColors.WindowBrush };
            var top = new StackPanel();
            top.Children.Add(new TextBlock { Text = "Текущий чертёж: " + Path.GetFileName(drawing), FontWeight = FontWeights.SemiBold });
            var linePanel = new StackPanel();
            var lineButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            var all = new Button { Content = "Выбрать все", Padding = new Thickness(8, 3, 8, 3) };
            var none = new Button { Content = "Снять все", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0) };
            all.Click += (_, __) => { foreach (var check in _lines.Values) check.IsChecked = true; };
            none.Click += (_, __) => { foreach (var check in _lines.Values) check.IsChecked = false; };
            lineButtons.Children.Add(all); lineButtons.Children.Add(none);
            linePanel.Children.Add(lineButtons);
            var lineList = new StackPanel();
            foreach (var line in lines)
            {
                var check = new CheckBox { Content = line.ToString(), IsChecked = true, Margin = new Thickness(2, 2, 0, 2) };
                check.Checked += (_, __) => RefreshRunEnabled(); check.Unchecked += (_, __) => RefreshRunEnabled();
                _lines[line.Key] = check; lineList.Children.Add(check);
            }
            linePanel.Children.Add(new ScrollViewer { Content = lineList, Height = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            // Lines and checks fold away after a run, leaving the window to
            // the results (unfolded again from the header).
            var settings = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            _settings.Content = settings;
            _settings.Header = SettingsHeader(null);
            top.Children.Add(_settings);
            settings.Children.Add(new GroupBox { Header = "Линии для аудита", Content = linePanel, Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 0) });
            foreach (var rule in AuditRules.All)
            {
                var check = new CheckBox { Content = rule.Title, IsChecked = true, Margin = new Thickness(0, 10, 0, 0), ToolTip = rule.Description };
                check.Checked += (_, __) => RefreshRunEnabled();
                check.Unchecked += (_, __) => RefreshRunEnabled();
                _choices[rule.Id] = check;
                settings.Children.Add(check);
                settings.Children.Add(new TextBlock { Text = rule.Description, Margin = new Thickness(20, 2, 0, 0), FontSize = 12, Foreground = System.Windows.Media.Brushes.DimGray, TextWrapping = TextWrapping.Wrap });
                if (rule.Id == "collisions")
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 6, 0, 0) };
                    row.Children.Add(new TextBlock { Text = "Допуск: пересечения глубиной до", VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(_tolerance);
                    row.Children.Add(new TextBlock { Text = "мм не учитывать (0 — учитывать все)", VerticalAlignment = VerticalAlignment.Center });
                    row.ToolTip = "Глубина — наименьший размер габарита области пересечения; для косого пересечения она чуть завышается, поэтому более глубокое пересечение допуск не скроет.";
                    row.SetBinding(IsEnabledProperty, new Binding(nameof(CheckBox.IsChecked)) { Source = check });
                    settings.Children.Add(row);
                }
            }
            top.Children.Add(_summary);
            DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
            var bottom = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(_show); buttons.Children.Add(_select); buttons.Children.Add(_export); buttons.Children.Add(_run);
            bottom.Children.Add(buttons);
            DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
            // «Описание» takes the remaining width: with fixed widths the table
            // was wider than the window and «Элементы» sat behind a scroll bar.
            // The check is the group header (see GroupStyle), not a column.
            foreach (var (label, property, width) in new[] { ("Результат", "Severity", 110d), ("Линия", "Line", 140d), ("Описание", "Message", 0d), ("Источник", "Sources", 170d), ("Элементы", "Elements", 120d) })
            {
                var style = new Style(typeof(TextBlock));
                style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
                _grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(property), ElementStyle = style,
                    Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = width > 0 ? 60 : 250 });
            }
            // Group header: «Коллизии с другими элементами — 4».
            var header = new FrameworkElementFactory(typeof(TextBlock));
            var title = new MultiBinding { StringFormat = "{0} — {1}" };
            title.Bindings.Add(new Binding("Name"));
            title.Bindings.Add(new Binding("ItemCount"));
            header.SetBinding(TextBlock.TextProperty, title);
            header.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            header.SetValue(TextBlock.MarginProperty, new Thickness(4, 10, 0, 4));
            _grid.GroupStyle.Add(new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = header } });
            root.Children.Add(_grid); Content = root;
            CommandIcons.Decorate(_run, "PSAUDIT"); CommandIcons.Decorate(_show, "SHOW"); CommandIcons.Decorate(_select, "SELECTBYLINE"); CommandIcons.Decorate(_export, "LOAD");
            _grid.SelectionChanged += (_, __) => _select.IsEnabled = _show.IsEnabled = _grid.SelectedItems.Cast<AuditIssue>().Any(i => i.Keys.Count > 0);
            _select.Click += (_, __) => { SelectedKeys = _grid.SelectedItems.Cast<AuditIssue>().SelectMany(i => i.SelectableKeys).Distinct().ToList(); DialogResult = true; };
            _show.Click += (_, __) => ShowInModel(_grid.SelectedItems.Cast<AuditIssue>().ToList());
            // Double-click on a row = «Показать в модели» for that row (not on the header).
            _grid.MouseDoubleClick += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_grid, source) is DataGridRow row && row.Item is AuditIssue issue)
                    ShowInModel(new[] { issue });
            };
            _export.Click += (_, __) => Export();
            _run.Click += (_, __) =>
            {
                var options = new AuditOptions();
                foreach (var choice in _choices.Where(c => c.Value.IsChecked == true)) options.Enabled.Add(choice.Key);
                options.SelectedLines = _lines.Where(l => l.Value.IsChecked == true).Select(l => l.Key).ToHashSet(StringComparer.Ordinal);
                if (options.Enabled.Count == 0 || options.SelectedLines.Count == 0) return;
                if (!AuditOptions.TryParseTolerance(_tolerance.Text, out var tolerance))
                {
                    _summary.Text = $"Допуск коллизий «{_tolerance.Text}» — не число ≥ 0. Укажите глубину в мм, например 2 или 0,5.";
                    _tolerance.Focus();
                    return;
                }
                options.CollisionToleranceMm = tolerance;
                _run.IsEnabled = false; _show.IsEnabled = false; _select.IsEnabled = false; _export.IsEnabled = false;
                _grid.ItemsSource = null; _issues = null; _snapshot = null;
                Mouse.OverrideCursor = Cursors.Wait;
                // The read runs on this thread (AutoCAD API) and can take minutes;
                // let the status line render first instead of a frozen old result.
                _summary.Text = "Идёт проверка… На большой модели проверка коллизий может занять несколько минут.";
                Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));
                try
                {
                    _snapshot = read(options);
                    _lastOptions = options;
                    _issues = AuditRules.Run(_snapshot, options);
                    // Issues come ordered (AuditRules.Order); groups follow
                    // that order — collisions first, the deepest on top.
                    var view = new ListCollectionView(_issues);
                    view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AuditIssue.Group)));
                    _grid.ItemsSource = view;
                    _summary.Text = _runSummary = $"Выбрано линий: {options.SelectedLines.Count}. Деталей на выбранных линиях: {_snapshot.Parts.Count(options.Includes)}. Замечаний: {_issues.Count}. " +
                        $"Из них «Не проверено»: {_issues.Count(i => i.Severity == "Не проверено")}. " +
                        (options.Enabled.Contains("collisions") ? $"Проверено пар: {_snapshot.CollisionPairsChecked}; пересечений деталей, соединённых через объект соединения, не учтено: {_snapshot.ConnectedJointsIgnored}. Вставок внешних ссылок: {_snapshot.XrefInstancesChecked}; недоступно: {_snapshot.XrefInstancesUnavailable}. " : "") +
                        (options.Enabled.Contains("collisions") && options.CollisionToleranceMm > 0 ? $"Допуск коллизий {options.CollisionToleranceMm:0.###} мм: пересечений в пределах допуска не учтено — {_snapshot.CollisionsWithinTolerance}. " : "") +
                        (options.Enabled.Contains("bolts") ? $"Соединений с комплектами болтов в чертеже: {_snapshot.Parts.Count(p => p.IsConnector && p.BoltSets.Count > 0)}. " : "") +
                        (_snapshot.Parts.Count == 0 ? "Детали Plant 3D не найдены. " : "") +
                        (_showInModel != null ? "«Показать в модели» (или двойной щелчок по строке) подсвечивает элементы замечания в чертеже, Enter возвращает сюда. " : "") +
                        "«Выделить и закрыть» выделяет элементы выбранных строк; для элемента внешней ссылки — её вставку. Модель не изменена.";
                    _export.IsEnabled = true;
                    _settings.Header = SettingsHeader(options);
                    _settings.IsExpanded = false;
                }
                catch (Exception ex) { _summary.Text = "Аудит не завершён: " + ex.Message; }
                finally { Mouse.OverrideCursor = null; RefreshRunEnabled(); }
            };
            // Without lines «Проверить» stays disabled; say why.
            if (lines.Count == 0) _summary.Text = "В сохранённом чертеже не найдено деталей Plant 3D — проверять нечего.";
            RefreshRunEnabled();
        }

        void RefreshRunEnabled() => _run.IsEnabled = _choices.Values.Any(c => c.IsChecked == true) && _lines.Values.Any(c => c.IsChecked == true);

        // Folded, the header still says what the results were run with.
        TextBlock SettingsHeader(AuditOptions options) => new()
        {
            FontWeight = FontWeights.SemiBold,
            Text = options == null ? "Параметры аудита: линии и проверки"
                : $"Параметры аудита — линий {options.SelectedLines?.Count ?? _lines.Count} из {_lines.Count}, проверок {options.Enabled.Count} из {_choices.Count}"
                  + (options.Enabled.Contains("collisions") && options.CollisionToleranceMm > 0 ? $", допуск коллизий {options.CollisionToleranceMm:0.###} мм" : "")
                  + " (щёлкните, чтобы изменить)",
        };

        void ShowInModel(IReadOnlyList<AuditIssue> issues)
        {
            if (_showInModel == null || !issues.Any(i => i.Keys.Count > 0)) return;
            try { _showInModel(this, issues); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось показать элементы в модели: " + ex.Message, "Аудит", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void Export()
        {
            if (_snapshot == null || _issues == null) return;
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Отчёт Word (*.docx)|*.docx", DefaultExt = ".docx", AddExtension = true, FileName = "Аудит-" + Path.GetFileNameWithoutExtension(_snapshot.Drawing) + ".docx" };
            if (dialog.ShowDialog(this) != true) return;
            try { AuditDocxReport.Save(dialog.FileName, _snapshot, _lastOptions, _issues); }
            catch (IOException ex) when (IsLocked(ex))
            {
                MessageBox.Show(this, "Файл отчёта открыт в другой программе (например, в Word). Закройте его или выберите другое имя.",
                    "Аудит", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить отчёт: " + ex.Message, "Аудит", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            // Saving used to give no sign at all; same «open in Word» setting as the spec.
            _summary.Text = _runSummary + $" Отчёт сохранён: {dialog.FileName}.";
            if (!PipingSpecPlugin.Settings.PluginSettings.Load().OpenAfterGenerate) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dialog.FileName, UseShellExecute = true }); }
            catch (Exception ex) { _summary.Text += " Открыть в Word не удалось: " + ex.Message; }
        }

        // Sharing / lock violation — the same test as ProjectScanner.IsFileLocked;
        // kept here so the window does not depend on the AutoCAD-side scanner.
        static bool IsLocked(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;
    }
}
