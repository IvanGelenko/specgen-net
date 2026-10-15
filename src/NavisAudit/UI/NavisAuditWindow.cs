using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PipingSpecPlugin.Audit;
using PipingSpecPlugin.UI;

namespace NavisAudit.UI
{
    // The PSAUDIT window of the Plant 3D plug-in, for Navisworks: the same
    // lines, checks, table and report. Modeless — a click on a row selects
    // its elements in the model, a double click also zooms to them.
    public sealed class NavisAuditWindow : Window
    {
        public sealed class Host
        {
            public string Model { get; set; }
            public IReadOnlyList<AuditLineChoice> Lines { get; set; }
            public IReadOnlyList<(string Id, string Title, string Description)> Checks { get; set; }
            public Func<AuditOptions, AuditSnapshot> Run { get; set; }
            // The remarks of a run; null — the Plant 3D rules as they are.
            public Func<AuditSnapshot, AuditOptions, List<AuditIssue>> Evaluate { get; set; }
            // select: the elements of the issues; zoom: and show them.
            public Action<IReadOnlyList<AuditIssue>, bool> Show { get; set; }
            public Func<AuditSnapshot, string> Summary { get; set; }
            public Action<string, AuditSnapshot, AuditOptions, IReadOnlyList<AuditIssue>> SaveReport { get; set; }
            public NavisSettings Settings { get; set; }
            public string Empty { get; set; }
        }

        readonly Host _host;
        readonly Dictionary<string, CheckBox> _choices = new Dictionary<string, CheckBox>();
        readonly Dictionary<string, CheckBox> _lines = new Dictionary<string, CheckBox>();
        readonly CheckBox _structures = new CheckBox { Margin = new Thickness(20, 6, 0, 0), Content = new TextBlock { TextWrapping = TextWrapping.Wrap,
            Text = "Пересечения конструкций между собой (металлоконструкции, железобетон, оборудование и другие объекты без линии); узлы металлоконструкций Plant 3D — где в пересечении конец балки или колонны — не учитываются. На сводной модели проверка заметно дольше." } };
        readonly Expander _settings = new Expander { IsExpanded = true, Margin = new Thickness(0, 8, 0, 0) };
        readonly TextBox _tolerance = new TextBox { Width = 70, Margin = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
        readonly DataGrid _grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            SelectionMode = DataGridSelectionMode.Extended, Margin = new Thickness(0, 12, 0, 12) };
        readonly TextBlock _summary = new TextBlock { Text = "Выберите линии и проверки и нажмите «Проверить».", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        readonly Button _run = new Button { Content = "Проверить", Width = 125, Height = 32 };
        readonly Button _show = new Button { Content = "Показать в модели", Width = 175, Height = 32, IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
        readonly Button _export = new Button { Content = "Сохранить отчёт", Width = 160, Height = 32, IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
        List<AuditIssue> _issues;
        AuditSnapshot _snapshot;
        AuditOptions _lastOptions;
        string _runSummary;

        public NavisAuditWindow(Host host)
        {
            _host = host;
            _structures.IsChecked = host.Settings.StructureCollisions;
            _tolerance.Text = host.Settings.CollisionToleranceMm.ToString("0.###", CultureInfo.CurrentCulture);
            Title = "Аудит трубопроводов — Navisworks";
            Width = 1150; Height = 900; MinWidth = 850; MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 13;
            var root = new DockPanel { Margin = new Thickness(16), Background = SystemColors.WindowBrush };
            Background = SystemColors.WindowBrush;
            var top = new StackPanel();
            top.Children.Add(new TextBlock { Text = "Модель: " + Path.GetFileName(host.Model ?? ""), FontWeight = FontWeights.SemiBold });
            var linePanel = new StackPanel();
            var lineButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            var all = new Button { Content = "Выбрать все", Padding = new Thickness(8, 3, 8, 3) };
            var none = new Button { Content = "Снять все", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0) };
            all.Click += (_, __) => { foreach (var check in _lines.Values) check.IsChecked = true; };
            none.Click += (_, __) => { foreach (var check in _lines.Values) check.IsChecked = false; };
            lineButtons.Children.Add(all); lineButtons.Children.Add(none);
            linePanel.Children.Add(lineButtons);
            var lineList = new StackPanel();
            foreach (var line in host.Lines)
            {
                var check = new CheckBox { Content = line.ToString(), IsChecked = true, Margin = new Thickness(2, 2, 0, 2) };
                check.Checked += (_, __) => RefreshRunEnabled(); check.Unchecked += (_, __) => RefreshRunEnabled();
                _lines[line.Key] = check; lineList.Children.Add(check);
            }
            linePanel.Children.Add(new ScrollViewer { Content = lineList, Height = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var settings = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            _settings.Content = settings;
            _settings.Header = SettingsHeader(null);
            top.Children.Add(_settings);
            settings.Children.Add(new GroupBox { Header = "Линии для аудита", Content = linePanel, Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 0) });
            foreach (var (id, title, description) in host.Checks)
            {
                var check = new CheckBox { Content = title, IsChecked = !host.Settings.DisabledChecks.Contains(id), Margin = new Thickness(0, 10, 0, 0), ToolTip = description };
                check.Checked += (_, __) => RefreshRunEnabled();
                check.Unchecked += (_, __) => RefreshRunEnabled();
                _choices[id] = check;
                settings.Children.Add(check);
                settings.Children.Add(new TextBlock { Text = description, Margin = new Thickness(20, 2, 0, 0), FontSize = 12, Foreground = System.Windows.Media.Brushes.DimGray, TextWrapping = TextWrapping.Wrap });
                if (id == "collisions")
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 6, 0, 0) };
                    row.Children.Add(new TextBlock { Text = "Допуск: пересечения глубиной до", VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(_tolerance);
                    row.Children.Add(new TextBlock { Text = "мм не учитывать (0 — учитывать все)", VerticalAlignment = VerticalAlignment.Center });
                    row.ToolTip = "Глубина — проникание по Clash Detective.";
                    row.SetBinding(IsEnabledProperty, new Binding(nameof(CheckBox.IsChecked)) { Source = check });
                    settings.Children.Add(row);
                    _structures.SetBinding(IsEnabledProperty, new Binding(nameof(CheckBox.IsChecked)) { Source = check });
                    settings.Children.Add(_structures);
                }
            }
            top.Children.Add(_summary);
            DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
            var bottom = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(_show); buttons.Children.Add(_export); buttons.Children.Add(_run);
            bottom.Children.Add(buttons);
            DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
            foreach (var (label, property, width) in new[] { ("Результат", "Severity", 110d), ("Линия", "Line", 140d), ("Описание", "Message", 0d), ("Источник", "Sources", 170d), ("Элементы", "Keys", 120d) })
            {
                var style = new Style(typeof(TextBlock));
                style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
                // A remark on a repeated xref names hundreds of elements.
                var binding = property == "Keys" ? new Binding(property) { Converter = new FirstKeys() } : new Binding(property);
                _grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = binding, ElementStyle = style,
                    Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = width > 0 ? 60 : 250 });
            }
            var header = new FrameworkElementFactory(typeof(TextBlock));
            var title2 = new MultiBinding { StringFormat = "{0} — {1}" };
            title2.Bindings.Add(new Binding("Name"));
            title2.Bindings.Add(new Binding("ItemCount"));
            header.SetBinding(TextBlock.TextProperty, title2);
            header.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            header.SetValue(TextBlock.MarginProperty, new Thickness(4, 10, 0, 4));
            _grid.GroupStyle.Add(new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = header } });
            root.Children.Add(_grid); Content = root;
            CommandIcons.Decorate(_run, "PSAUDIT"); CommandIcons.Decorate(_show, "SHOW"); CommandIcons.Decorate(_export, "LOAD");
            // A click selects the row's elements in the model (the window stays).
            _grid.SelectionChanged += (_, __) =>
            {
                var selected = _grid.SelectedItems.OfType<AuditIssue>().ToList();
                _show.IsEnabled = selected.Any(i => i.Keys.Count > 0);
                if (selected.Count > 0) Show(selected, false);
            };
            _show.Click += (_, __) => Show(_grid.SelectedItems.OfType<AuditIssue>().ToList(), true);
            _grid.MouseDoubleClick += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_grid, source) is DataGridRow row && row.Item is AuditIssue issue)
                    Show(new[] { issue }, true);
            };
            _export.Click += (_, __) => Export();
            _run.Click += (_, __) => RunAudit();
            if (host.Lines.Count == 0) _summary.Text = host.Empty ?? "В модели не найдено деталей Plant 3D — проверять нечего.";
            RefreshRunEnabled();
        }

        void RunAudit()
        {
            var options = new AuditOptions();
            foreach (var choice in _choices.Where(c => c.Value.IsChecked == true)) options.Enabled.Add(choice.Key);
            options.SelectedLines = new HashSet<string>(_lines.Where(l => l.Value.IsChecked == true).Select(l => l.Key), StringComparer.Ordinal);
            if (options.Enabled.Count == 0 || options.SelectedLines.Count == 0) return;
            if (!AuditOptions.TryParseTolerance(_tolerance.Text, out var tolerance))
            {
                _summary.Text = $"Допуск коллизий «{_tolerance.Text}» — не число ≥ 0. Укажите глубину в мм, например 2 или 0,5.";
                _tolerance.Focus();
                return;
            }
            options.CollisionToleranceMm = tolerance;
            options.StructureCollisions = _structures.IsChecked == true;
            _host.Settings.CollisionToleranceMm = tolerance;
            _host.Settings.StructureCollisions = options.StructureCollisions;
            _host.Settings.DisabledChecks = new HashSet<string>(_choices.Where(c => c.Value.IsChecked != true).Select(c => c.Key), StringComparer.Ordinal);
            _host.Settings.Save();
            _run.IsEnabled = false; _show.IsEnabled = false; _export.IsEnabled = false;
            _grid.ItemsSource = null; _issues = null; _snapshot = null;
            Mouse.OverrideCursor = Cursors.Wait;
            _summary.Text = "Идёт проверка… На сводной модели поиск коллизий может занять несколько минут.";
            Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));
            try
            {
                _snapshot = _host.Run(options);
                _lastOptions = options;
                _issues = _host.Evaluate?.Invoke(_snapshot, options) ?? AuditRules.Run(_snapshot, options);
                var view = new ListCollectionView(_issues);
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AuditIssue.Group)));
                _grid.ItemsSource = view;
                _summary.Text = _runSummary = $"Выбрано линий: {options.SelectedLines.Count}. Деталей на выбранных линиях: {_snapshot.Parts.Count(options.Includes)}. Замечаний: {_issues.Count}. " +
                    $"Из них «Не проверено»: {_issues.Count(i => i.Severity == "Не проверено")}. " + (_host.Summary?.Invoke(_snapshot) ?? "") +
                    "Щелчок по строке выделяет её элементы в модели, двойной щелчок или «Показать в модели» — ещё и приближает к ним. Модель не изменена.";
                _export.IsEnabled = true;
                _settings.Header = SettingsHeader(options);
                _settings.IsExpanded = false;
            }
            catch (Exception ex) { _summary.Text = "Аудит не завершён: " + ex.Message; }
            finally { Mouse.OverrideCursor = null; RefreshRunEnabled(); }
        }

        sealed class FirstKeys : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var keys = value as IReadOnlyList<string> ?? (value as IEnumerable<string>)?.ToList() ?? new List<string>();
                return keys.Count <= 12 ? string.Join(", ", keys) : string.Join(", ", keys.Take(10)) + $" … всего {keys.Count}";
            }
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
        }

        void RefreshRunEnabled() => _run.IsEnabled = _choices.Values.Any(c => c.IsChecked == true) && _lines.Values.Any(c => c.IsChecked == true);

        TextBlock SettingsHeader(AuditOptions options) => new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Text = options == null ? "Параметры аудита: линии и проверки"
                : $"Параметры аудита — линий {options.SelectedLines?.Count ?? _lines.Count} из {_lines.Count}, проверок {options.Enabled.Count} из {_choices.Count}"
                  + (options.Enabled.Contains("collisions") && options.CollisionToleranceMm > 0 ? $", допуск коллизий {options.CollisionToleranceMm:0.###} мм" : "")
                  + (options.Enabled.Contains("collisions") && options.StructureCollisions ? ", конструкции между собой" : "")
                  + " (щёлкните, чтобы изменить)",
        };

        void Show(IReadOnlyList<AuditIssue> issues, bool zoom)
        {
            if (_host.Show == null || !issues.Any(i => i.Keys.Count > 0)) return;
            try { _host.Show(issues, zoom); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось показать элементы в модели: " + ex.Message, "Аудит", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void Export()
        {
            if (_snapshot == null || _issues == null) return;
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Отчёт Word (*.docx)|*.docx", DefaultExt = ".docx", AddExtension = true,
                FileName = "Аудит-" + Path.GetFileNameWithoutExtension(_snapshot.Drawing ?? "модель") + ".docx" };
            if (dialog.ShowDialog(this) != true) return;
            try { _host.SaveReport(dialog.FileName, _snapshot, _lastOptions, _issues); }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == 32 || (ex.HResult & 0xFFFF) == 33)
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
            _summary.Text = _runSummary + $" Отчёт сохранён: {dialog.FileName}.";
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dialog.FileName, UseShellExecute = true }); }
            catch (Exception ex) { _summary.Text += " Открыть в Word не удалось: " + ex.Message; }
        }
    }
}
