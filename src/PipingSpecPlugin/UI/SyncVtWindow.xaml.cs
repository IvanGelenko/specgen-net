using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.ProcessPower.DataLinks;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.Spec;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.UI
{
    /// <summary>
    /// Review-and-confirm dialog for СИНХРОНИЗАЦИЯ ВТ: pick the workbook,
    /// see the computed diff against the live P3dLineGroup rows, apply,
    /// then keep the window open with the "внутренняя ведомость" grid
    /// (every project line with a non-blank ID, every synced property)
    /// refreshed to the post-write state so the result is visible without
    /// re-opening the command. Mirrors the "never write silently" pattern
    /// SpecPickerWindow / WriteSpecCommand use for the .docx path — this
    /// is the plug-in's first DLM write path, so the review step matters
    /// even more here.
    /// </summary>
    public partial class SyncVtWindow : Window
    {
        readonly DataLinksManager _projectDlm;
        readonly PluginSettings _settings;
        readonly Action<string> _trace;

        List<VtRowData> _vtRows;
        LineSyncPlan _plan;
        bool _collapseLedgerDuplicates;
        int _hiddenLedgerDuplicates;

        string DuplicateSummary => _collapseLedgerDuplicates
            ? $" Объединено повторных строк: {_hiddenLedgerDuplicates}. Различия в ячейках отмечены ⚠; варианты разделены |." : "";

        public bool Applied { get; private set; }

        public SyncVtWindow(DataLinksManager projectDlm, PluginSettings settings, Action<string> trace)
        {
            InitializeComponent();
            CommandIcons.Decorate(BrowseButton, "BROWSE");
            CommandIcons.Decorate(LoadButton, "LOAD");
            CommandIcons.Decorate(RefreshButton, "REFRESH");
            CommandIcons.Decorate(ApplySelectedButton, "APPLY");
            CommandIcons.Decorate(ApplyButton, "APPLY");
            CommandIcons.Decorate(CloseButton, "CLOSE");
            if (SyncSelectedMenuItem != null)
                SyncSelectedMenuItem.Icon = new Image { Source = CommandIcons.Create("APPLY"), Width = 16, Height = 16 };
            BrowseButton.Width = 100;
            LoadButton.Width = 110;
            RefreshButton.Width = 180;
            ApplySelectedButton.Width = 195;
            ApplyButton.Width = 205;
            _projectDlm = projectDlm;
            _settings = settings;
            _trace = trace;
            VtPathBox.Text = settings.LastVtFilePath ?? "";

            // Show the current Plant 3D state immediately — the user
            // shouldn't have to pick a ВТ file just to see what's already
            // in the project. Picking a file separately builds the diff
            // plan on top of this same ledger.
            Loaded += (_, __) => RefreshLedgerOnly();
        }

        /// <summary>
        /// Reads P3dLineGroup and renders the ledger with no diff plan
        /// (ApplyButton stays disabled, StatusText explains why) — used
        /// on window open and by "Обновить ведомость" before any ВТ file
        /// has been loaded.
        /// </summary>
        void RefreshLedgerOnly()
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var lines = LineSyncService.ReadAllLines(_projectDlm, _trace);
                RenderLedger(lines);
                ApplyButton.IsEnabled = false;
                UpdateSelectedState();
                if (lines.Count == 0)
                {
                    SummaryText.Text = "Линий в проекте: 0.";
                    ProblemsText.Text = "Нет.";
                    StatusText.Text = "В проекте Plant 3D не найдено ни одной производственной линии (P3dLineGroup).";
                    return;
                }
                SummaryText.Text = $"Строк в ведомости: {LineSyncService.BuildLedger(lines).Count - _hiddenLedgerDuplicates}. Линий в проекте: {lines.Count}. Загрузите файл ВТ, чтобы увидеть план изменений." + DuplicateSummary;
                ProblemsText.Text = "Нет.";
                ApplyButton.IsEnabled = false;
                StatusText.Text = "Выберите файл ВТ и нажмите «Загрузить», чтобы найти изменения.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Не удалось прочитать линии из Plant 3D:\n{ex.GetType().Name}: {ex.Message}",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.OpenFileDialog
            {
                Title = "Выберите файл Ведомости трубопроводов (.xlsx)",
                Filter = "Excel (*.xlsx)|*.xlsx|Все файлы|*.*",
                CheckFileExists = true,
                FileName = VtPathBox.Text ?? "",
            };
            if (!string.IsNullOrEmpty(VtPathBox.Text))
            {
                var dir = Path.GetDirectoryName(VtPathBox.Text);
                if (Directory.Exists(dir)) dlg.InitialDirectory = dir;
            }
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                VtPathBox.Text = dlg.FileName;
        }

        void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            var path = (VtPathBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show(this, "Выберите файл ВТ через кнопку «Обзор...».",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!File.Exists(path))
            {
                MessageBox.Show(this, $"Файл не найден:\n{path}",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var vt = VtReader.Read(path);
                if (vt.Error != null)
                {
                    MessageBox.Show(this, vt.Error, "Синхронизация ВТ",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (vt.Rows.Count == 0)
                {
                    // The sync key has been "n/n" since 1.2.4 (VtReader skips
                    // rows without it); the message still named the old
                    // «ИД трубопровода» column and sent users to the wrong one.
                    MessageBox.Show(this,
                        "В файле не найдено ни одной строки с заполненным «n/n» — "
                        + "по этой колонке строки ВТ сопоставляются с полем ID линий проекта.",
                        "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _vtRows = vt.Rows;
                _settings.LastVtFilePath = path;
                try { RebuildPlanAndRender(vt.Rows.Count); }
                catch (Exception ex)
                {
                    // The ВТ file was read fine (VtReader reports its own
                    // errors via vt.Error and never throws) — what failed is
                    // reading the project lines; say so.
                    MessageBox.Show(this, $"Файл ВТ прочитан, но не удалось прочитать линии из Plant 3D:\n{ex.GetType().Name}: {ex.Message}",
                        "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Не удалось прочитать файл ВТ:\n{ex.GetType().Name}: {ex.Message}",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        /// <summary>
        /// Re-reads P3dLineGroup from Plant 3D, rebuilds the diff plan
        /// against the currently loaded ВТ rows, and re-renders the
        /// ledger grid. Called both after "Загрузить" and again after
        /// "Применить" (with the same _vtRows) so the grid shows the
        /// post-write state without closing the window.
        /// </summary>
        void RebuildPlanAndRender(int vtRowCount)
        {
            var lines = LineSyncService.ReadAllLines(_projectDlm, _trace);
            _plan = LineSyncService.BuildPlan(_vtRows, lines);
            RenderLedger(lines);
            RenderSummaryAndProblems(vtRowCount, lines.Count);
            UpdateSelectedState();
            if (lines.Count == 0)
            {
                MessageBox.Show(this,
                    "В проекте Plant 3D не найдено ни одной производственной линии (P3dLineGroup). "
                    + "Убедитесь, что проект открыт и содержит трубопроводные линии.",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

        }

        /// <summary>
        /// Builds the "внутренняя ведомость" grid: one row per project
        /// line with a non-blank ID, one column per property
        /// СИНХРОНИЗАЦИЯ ВТ knows how to write. Columns are generated in
        /// code because their set (KnownLineGroupColumns) isn't static
        /// XAML content — WPF's DataGrid has no built-in "bind to
        /// Dictionary key" column type, so a DataTable is the simplest
        /// bridge: each column becomes a DataColumn, AutoGenerateColumns
        /// then does the rest.
        /// </summary>
        // "Tag" and "Номер" are written by the sync (both get the full
        // designation string — see LineSyncService's comment on why both
        // are targeted) but are redundant to show as their own columns:
        // they duplicate the "Тег линии" column already rendered from
        // LineGroupInfo.Tag. Excluded here only from the UI grid — the
        // underlying write/KnownLineGroupColumns logic is untouched.

        // Fixed display order for the ledger grid, chosen by the user
        // (roughly: designation-decoded fields grouped together, then
        // testing/corrosion/insulation groups, "Доп. указания" last).
        // KnownLineGroupColumns' own order just reflects DirectFields/
        // DesignationFields declaration order in LineSyncService, which
        // was never meant to double as a display order — this list is
        // UI-only and doesn't affect what gets written.
        static readonly string[] LedgerColumnOrder =
        {
            "Size2", "Material_pipe", "Код_трубопровода", "GostGroup", "Ysl_pressure", "TRTSGroup",
            "Pwork", "Tworking", "PressureDesign", "Tdesign", "TestD", "TestW",
            "InsulationType", "InsulationThickness", "Heat_type", "flanges",
            "CorrosionSpeed", "Corrosion", "srok", "NDControl", "Thermo", "Additional",
        };

        void RenderLedger(List<LineGroupInfo> lines)
        {
            var ledgerRows = LineSyncService.BuildLedger(lines, _collapseLedgerDuplicates);
            _hiddenLedgerDuplicates = lines.Count(l => !string.IsNullOrWhiteSpace(l.Id)) - ledgerRows.Count;
            var labels = LineSyncService.KnownLineGroupColumnLabels;
            var known = new HashSet<string>(LineSyncService.KnownLineGroupColumns, StringComparer.OrdinalIgnoreCase);
            // Anything KnownLineGroupColumns has that LedgerColumnOrder
            // doesn't list (e.g. a future new field) still shows up,
            // appended after the fixed order, instead of silently
            // disappearing from the grid.
            var visibleColumns = LedgerColumnOrder
                .Where(c => known.Contains(c) && LineSyncService.IsLedgerVisibleColumn(c))
                .Concat(LineSyncService.KnownLineGroupColumns
                    .Where(c => LineSyncService.IsLedgerVisibleColumn(c) && !LedgerColumnOrder.Contains(c, StringComparer.OrdinalIgnoreCase)))
                .ToList();

            var table = new DataTable();
            table.Columns.Add("_RowIds", typeof(string));
            table.Columns.Add("ID");
            table.Columns.Add("Тег линии");
            foreach (var col in visibleColumns)
                if (!table.Columns.Contains(col))
                    table.Columns.Add(col, typeof(string));

            foreach (var row in ledgerRows)
            {
                var dr = table.NewRow();
                dr["_RowIds"] = string.Join(",", row.AllRowIds);
                dr["ID"] = row.Id ?? "";
                dr["Тег линии"] = row.Tag ?? "";
                foreach (var col in visibleColumns)
                    dr[col] = row.Values.TryGetValue(col, out var v) ? v : "";
                table.Rows.Add(dr);
            }

            LedgerGrid.Columns.Clear();
            LedgerGrid.ItemsSource = table.DefaultView;
            foreach (DataColumn col in table.Columns)
            {
                if (col.ColumnName.StartsWith("_")) continue;
                var header = col.ColumnName == "ID" || col.ColumnName == "Тег линии"
                    ? col.ColumnName
                    : (labels.TryGetValue(col.ColumnName, out var ru) ? ru : col.ColumnName);
                bool isFixedColumn = col.ColumnName == "ID" || col.ColumnName == "Тег линии";
                // "Тег линии" carries the full designation string (e.g.
                // "150-KSN002-LS016C-HN", ~20 chars) — needs real width
                // unlike the short codes/numbers in every other data
                // column, so it gets its own wider cap instead of the
                // shared 58px used for the rest.
                double maxWidth = col.ColumnName == "Тег линии" ? 170 : 58;
                var cellStyle = new Style(typeof(TextBlock));
                cellStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
                cellStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,
                    new System.Windows.Data.Binding(col.ColumnName)));
                LedgerGrid.Columns.Add(new DataGridTextColumn
                {
                    // Compact layout: data columns stay narrow (most
                    // values here are short codes/numbers like "1.6" or
                    // "Б(б)"). Their headers are long ГОСТ-style labels
                    // ("Классификация по ГОСТ 32569"), so instead of
                    // wrapping onto several horizontal lines (which
                    // still needed real column width to stay legible)
                    // they're rotated -90° — same trick the .docx spec
                    // template itself uses for narrow columns (see
                    // SpecTemplateWriter's TextDirection handling). This
                    // is what actually lets ~22 data columns fit on a
                    // 1080p-class monitor without a horizontal scrollbar.
                    // ID / Тег линии keep a normal horizontal header —
                    // short enough not to need it, and they're the two
                    // columns a user scans first.
                    Header = header,
                    HeaderTemplate = isFixedColumn ? null : MakeVerticalHeaderTemplate(header),
                    Binding = new System.Windows.Data.Binding(col.ColumnName),
                    ElementStyle = cellStyle,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                    MinWidth = 42,
                    MaxWidth = maxWidth,
                });
            }
        }

        static DataTemplate MakeVerticalHeaderTemplate(string headerText)
        {
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetValue(TextBlock.TextProperty, headerText);
            factory.SetValue(TextBlock.FontSizeProperty, 11.0);
            factory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Bottom);
            factory.SetValue(TextBlock.LayoutTransformProperty,
                new System.Windows.Media.RotateTransform(-90));
            return new DataTemplate { VisualTree = factory };
        }

        void RenderSummaryAndProblems(int vtRowCount, int lineCount)
        {
            var linesTouched = _plan.Actions.Select(a => a.Line.RowId).Distinct().Count();
            var fieldsTotal = _plan.TotalChangedFields();
            SummaryText.Text =
                $"Строк в ВТ с заполненным n/n: {vtRowCount}. Линий в проекте: {lineCount}. " +
                $"Совпало по ID: {linesTouched}. Полей к изменению: {fieldsTotal}. " +
                $"Не найдено в проекте: {_plan.UnmatchedVtRows.Count}. " +
                $"Линий без соответствия в ВТ: {_plan.UnmatchedLines.Count}." +
                (_plan.DuplicateIds.Count > 0 ? $" ⚠ Повторяющихся ID в проекте: {_plan.DuplicateIds.Count}." : "") +
                (_plan.DuplicateVtNN.Count > 0 ? $" ⚠ Повторяющихся n/n в файле ВТ: {_plan.DuplicateVtNN.Count}." : "") + DuplicateSummary;

            var problems = new System.Text.StringBuilder();
            foreach (var id in _plan.DuplicateIds)
                problems.AppendLine($"⚠ ID «{id}» встречается у нескольких линий в проекте — при применении изменения затронут все совпавшие записи. Скрытие повторов в таблице не удаляет записи проекта.");
            foreach (var nn in _plan.DuplicateVtNN)
                problems.AppendLine($"⚠ n/n «{nn}» встречается у нескольких строк в файле ВТ — все они пропущены (неоднозначно, с какой линией сопоставлять). Проверьте нумерацию в ВТ.");
            var duplicateNNSet = new HashSet<string>(_plan.DuplicateVtNN, StringComparer.OrdinalIgnoreCase);
            foreach (var vtRow in _plan.UnmatchedVtRows)
            {
                // vtRow.NN is already trimmed by VtReader (VtReader.cs)
                // before it reaches this plan, same as every value in
                // DuplicateVtNN — no second Trim() needed here.
                if (duplicateNNSet.Contains(vtRow.NN ?? "")) continue; // already reported above.
                problems.AppendLine($"— n/n «{vtRow.NN}» (строка {vtRow.RowNumber} ВТ) не найден среди линий проекта (поле ID).");
            }
            foreach (var line in _plan.UnmatchedLines)
                problems.AppendLine($"— Линия «{line.Tag}» (ID «{line.Id}») в проекте не встречается в ВТ.");
            foreach (var action in _plan.Actions)
                foreach (var rej in action.PicklistRejections)
                    problems.AppendLine($"— ID «{action.Line.Id}»: {rej}");
            ProblemsText.Text = problems.Length > 0 ? problems.ToString() : "Нет.";

            ApplyButton.IsEnabled = fieldsTotal > 0;
            StatusText.Text = fieldsTotal > 0
                ? $"Найдено изменений: {fieldsTotal}. Нажмите «Применить в Plant 3D»."
                : "Изменений не найдено — все совпавшие значения уже актуальны.";
        }

        void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _collapseLedgerDuplicates = CollapseDuplicatesChk.IsChecked == true;
            if (_vtRows == null) { RefreshLedgerOnly(); return; }
            Mouse.OverrideCursor = Cursors.Wait;
            try { RebuildPlanAndRender(_vtRows.Count); }
            catch (Exception ex)
            {
                // Same as Load/RefreshLedgerOnly: never let a read failure
                // escape a click handler (it would crash the AutoCAD session).
                MessageBox.Show(this, $"Не удалось прочитать линии из Plant 3D:\n{ex.GetType().Name}: {ex.Message}",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { Mouse.OverrideCursor = null; }
        }

        void CollapseDuplicatesChk_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            _collapseLedgerDuplicates = CollapseDuplicatesChk.IsChecked == true;
            if (_vtRows != null)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                try { RebuildPlanAndRender(_vtRows.Count); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Не удалось перечитать линии из Plant 3D:\n{ex.GetType().Name}: {ex.Message}",
                        "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { Mouse.OverrideCursor = null; }
            }
            else
            {
                RefreshLedgerOnly();
            }
        }

        void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_plan == null || _plan.Actions.Count == 0) return;

            var linesTouched = _plan.Actions.Select(a => a.Line.RowId).Distinct().Count();
            var fieldsTotal = _plan.TotalChangedFields();
            var confirm = MessageBox.Show(this,
                $"Будет изменено полей: {fieldsTotal} на {linesTouched} линиях в текущем проекте Plant 3D.\n\n"
                + "Действие нельзя отменить средствами плагина (только вручную в Plant 3D). Продолжить?",
                "Синхронизация ВТ", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            Mouse.OverrideCursor = Cursors.Wait;
            List<string> errors;
            try
            {
                errors = LineSyncService.Apply(_plan, _projectDlm, _trace);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Синхронизация прервана:\n{ex.GetType().Name}: {ex.Message}",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Error);
                if (_vtRows != null) try { RebuildPlanAndRender(_vtRows.Count); } catch { }
                return;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            // Latch: a later failed Apply in the same window must not hide an
            // earlier successful one (SYNCVT then reported "no confirmed
            // successful sync" although values had been written).
            if (errors.Count == 0) Applied = true;
            // Same guard as SyncVtCommand: defaults from a failed Load()
            // must not overwrite the user's settings.json.
            if (!_settings.LoadFailed)
                try { _settings.Save(); }
                catch (Exception ex) { _trace?.Invoke($"Не удалось сохранить настройки: {ex.Message}"); }

            // Re-read from Plant 3D and re-render the ledger so the user
            // sees the post-write state in the same window rather than
            // having to close and re-run the command to check the result.
            // Guarded: an exception escaping a WPF click handler inside
            // AutoCAD takes the dispatcher (and the session) down, and the
            // write itself has already happened at this point.
            try { RebuildPlanAndRender(_vtRows.Count); }
            catch (Exception ex)
            {
                _trace?.Invoke($"Не удалось перечитать линии после записи: {ex.GetType().Name}: {ex.Message}");
                StatusText.Text = "Изменения записаны, но перечитать линии не удалось — нажмите «Обновить ведомость».";
            }

            if (errors.Count == 0)
            {
                MessageBox.Show(this,
                    $"Готово. Изменено полей: {fieldsTotal} на {linesTouched} линиях.\n\n"
                    + "Значения записаны в проектную базу Plant 3D (P3dLineGroup). "
                    + "Если на деталях этих линий настроено наследование свойств "
                    + "(правила Property Acquisition), для их обновления может "
                    + "понадобиться пересохранить/переоткрыть соответствующие чертежи.",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this,
                    $"Синхронизация не завершена. Ошибок: {errors.Count}. Проверьте состояние проекта.\n\n"
                    + string.Join("\n", errors.Take(20))
                    + (errors.Count > 20 ? $"\n… и ещё {errors.Count - 20}." : ""),
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        DataRowView SelectedRowView => LedgerGrid.SelectedItem as DataRowView;

        List<int> GetSelectedRowIds()
        {
            var drv = SelectedRowView;
            if (drv == null) return new List<int>();
            if (drv.Row.Table.Columns.Contains("_RowIds"))
            {
                var s = drv["_RowIds"] as string;
                if (!string.IsNullOrEmpty(s))
                {
                    return s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(part => int.TryParse(part, out var id) ? id : 0)
                            .Where(id => id != 0)
                            .ToList();
                }
            }
            return new List<int>();
        }

        string GetSelectedLineId()
        {
            var drv = SelectedRowView;
            return (drv?["ID"] as string ?? "").Trim();
        }

        string GetSelectedLineTag()
        {
            var drv = SelectedRowView;
            var tag = drv?["Тег линии"] as string ?? "";
            return tag.Replace("⚠ дубль ID · ", "").Trim();
        }

        List<LineSyncAction> GetSelectedActions()
        {
            if (_plan == null || _plan.Actions == null || _plan.Actions.Count == 0)
                return new List<LineSyncAction>();

            var rowIds = GetSelectedRowIds();
            if (rowIds.Count > 0)
            {
                var idSet = new HashSet<int>(rowIds);
                var actions = _plan.Actions.Where(a => a.Line != null && idSet.Contains(a.Line.RowId)).ToList();
                if (actions.Count > 0) return actions;
            }

            var lineId = GetSelectedLineId();
            if (!string.IsNullOrEmpty(lineId))
            {
                return _plan.Actions.Where(a => a.Line != null && string.Equals(a.Line.Id?.Trim(), lineId, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return new List<LineSyncAction>();
        }

        void UpdateSelectedState()
        {
            var rowIds = GetSelectedRowIds();
            var lineId = GetSelectedLineId();
            var lineTag = GetSelectedLineTag();

            if (rowIds.Count == 0 && string.IsNullOrEmpty(lineId))
            {
                ApplySelectedButton.IsEnabled = false;
                if (SyncSelectedMenuItem != null) SyncSelectedMenuItem.IsEnabled = false;
                return;
            }

            if (_plan == null)
            {
                ApplySelectedButton.IsEnabled = false;
                if (SyncSelectedMenuItem != null) SyncSelectedMenuItem.IsEnabled = false;
                StatusText.Text = $"Выбрана линия «{lineTag}» (ID {lineId}). Загрузите файл ВТ для поиска изменений.";
                return;
            }

            var actions = GetSelectedActions();
            int changesCount = actions.Sum(a => a.Changes.Count);

            if (changesCount > 0)
            {
                ApplySelectedButton.IsEnabled = true;
                if (SyncSelectedMenuItem != null) SyncSelectedMenuItem.IsEnabled = true;
                StatusText.Text = $"Выбрана линия «{lineTag}» (ID {lineId}): полей к изменению: {changesCount}. Нажмите «Применить выделенную».";
            }
            else
            {
                ApplySelectedButton.IsEnabled = false;
                if (SyncSelectedMenuItem != null) SyncSelectedMenuItem.IsEnabled = false;

                var isUnmatchedLine = _plan.UnmatchedLines.Any(l => rowIds.Contains(l.RowId) || string.Equals(l.Id?.Trim(), lineId, StringComparison.OrdinalIgnoreCase));
                var isDuplicateVtNN = _plan.DuplicateVtNN.Contains(lineId);

                if (isDuplicateVtNN)
                {
                    StatusText.Text = $"Выбрана линия «{lineTag}» (ID {lineId}): n/n встречается несколько раз в файле ВТ (пропущена).";
                }
                else if (isUnmatchedLine)
                {
                    StatusText.Text = $"Выбрана линия «{lineTag}» (ID {lineId}): не найдена в файле ВТ (нет строки с n/n = {lineId}).";
                }
                else
                {
                    StatusText.Text = $"Выбрана линия «{lineTag}» (ID {lineId}): все параметры уже соответствуют ВТ (изменений нет).";
                }
            }
        }

        void LedgerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectedState();
        }

        void LedgerGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridRow)
            {
                if (dep is System.Windows.Controls.Primitives.DataGridColumnHeader)
                    return;
                dep = VisualTreeHelper.GetParent(dep);
            }
            if (dep is DataGridRow row)
            {
                LedgerGrid.SelectedItem = row.Item;
            }
        }

        void ReselectLine(List<int> rowIds, string lineId)
        {
            if (LedgerGrid.ItemsSource is not DataView dataView) return;
            var targetSet = new HashSet<int>(rowIds ?? Enumerable.Empty<int>());
            foreach (DataRowView drv in dataView)
            {
                if (targetSet.Count > 0 && drv.Row.Table.Columns.Contains("_RowIds"))
                {
                    var s = drv["_RowIds"] as string;
                    if (!string.IsNullOrEmpty(s))
                    {
                        var ids = s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(p => int.TryParse(p, out var id) ? id : 0)
                                   .Where(id => id != 0);
                        if (ids.Any(targetSet.Contains))
                        {
                            LedgerGrid.SelectedItem = drv;
                            LedgerGrid.ScrollIntoView(drv);
                            return;
                        }
                    }
                }
                if (!string.IsNullOrEmpty(lineId) && string.Equals((drv["ID"] as string ?? "").Trim(), lineId, StringComparison.OrdinalIgnoreCase))
                {
                    LedgerGrid.SelectedItem = drv;
                    LedgerGrid.ScrollIntoView(drv);
                    return;
                }
            }
        }

        void ApplySelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (_plan == null) return;

            var actions = GetSelectedActions();
            var changes = actions.SelectMany(a => a.Changes).ToList();
            if (actions.Count == 0 || changes.Count == 0)
            {
                MessageBox.Show(this, "Для выбранной линии нет изменений для применения.",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var lineId = GetSelectedLineId();
            var lineTag = GetSelectedLineTag();
            var rowIds = GetSelectedRowIds();

            var changeDetails = string.Join("\n", changes.Select(c => $"• {c.DisplayLabel}: «{c.OldValue}» → «{c.NewValue}»"));
            if (changeDetails.Length > 800)
                changeDetails = changeDetails.Substring(0, 800) + "\n…";

            var confirm = MessageBox.Show(this,
                $"Будет синхронизирована линия «{lineTag}» (ID «{lineId}»).\n\n"
                + $"Изменяемых полей ({changes.Count}):\n{changeDetails}\n\n"
                + "Действие нельзя отменить средствами плагина (только вручную в Plant 3D). Продолжить?",
                "Синхронизация ВТ", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            var singlePlan = new LineSyncPlan
            {
                Actions = actions
            };

            Mouse.OverrideCursor = Cursors.Wait;
            List<string> errors;
            try
            {
                errors = LineSyncService.Apply(singlePlan, _projectDlm, _trace);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Синхронизация прервана:\n{ex.GetType().Name}: {ex.Message}",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Error);
                if (_vtRows != null)
                {
                    try { RebuildPlanAndRender(_vtRows.Count); ReselectLine(rowIds, lineId); } catch { }
                }
                return;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            if (errors.Count == 0) Applied = true;
            if (!_settings.LoadFailed)
                try { _settings.Save(); }
                catch (Exception ex) { _trace?.Invoke($"Не удалось сохранить настройки: {ex.Message}"); }

            try
            {
                RebuildPlanAndRender(_vtRows.Count);
                ReselectLine(rowIds, lineId);
            }
            catch (Exception ex)
            {
                _trace?.Invoke($"Не удалось перечитать линии после записи: {ex.GetType().Name}: {ex.Message}");
                StatusText.Text = "Изменения записаны, но перечитать линии не удалось — нажмите «Обновить ведомость».";
            }

            if (errors.Count == 0)
            {
                MessageBox.Show(this,
                    $"Готово. Линия «{lineTag}» (ID «{lineId}») успешно синхронизирована.\n"
                    + $"Изменено полей: {changes.Count}.\n\n"
                    + "Значения записаны в проектную базу Plant 3D (P3dLineGroup). "
                    + "Если на деталях этой линии настроено наследование свойств "
                    + "(правила Property Acquisition), для их обновления может "
                    + "понадобиться пересохранить/переоткрыть соответствующие чертежи.",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this,
                    $"Синхронизация линии «{lineTag}» не завершена. Ошибок: {errors.Count}. Проверьте состояние проекта.\n\n"
                    + string.Join("\n", errors.Take(20))
                    + (errors.Count > 20 ? $"\n… и ещё {errors.Count - 20}." : ""),
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
