using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        readonly VtSyncSession _session;
        readonly IVtDuplicateStore _duplicates;
        readonly PluginSettings _settings;
        readonly Action<string> _trace;

        LineSyncPlan Plan => _session.Plan;

        bool CurrentDrawingOnly => CurrentDrawingScopeItem.IsSelected;
        string LineCountLabel => CurrentDrawingOnly ? "Линий на текущем чертеже" : "Линий в проекте";
        string EmptyScopeStatus => CurrentDrawingOnly
            ? "На текущем чертеже не найдено ни одной трубопроводной линии."
            : "В проекте Plant 3D не найдено ни одной производственной линии (P3dLineGroup).";

        public bool Applied { get; private set; }

        internal SyncVtWindow(VtSyncWorkflow workflow, IVtDuplicateStore duplicates, PluginSettings settings, Action<string> trace)
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
            _session = new VtSyncSession(workflow);
            _duplicates = duplicates;
            _settings = settings;
            _trace = trace;
            VtPathBox.Text = settings.LastVtFilePath ?? "";
            CurrentDrawingScopeItem.IsEnabled = _session.HasDrawing;
            if (!_session.HasDrawing)
                CurrentDrawingScopeItem.ToolTip = "Текущий чертёж не зарегистрирован в трубопроводном проекте.";
            LedgerScopeBox.SelectedItem = _session.HasDrawing ? CurrentDrawingScopeItem : ProjectScopeItem;

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
                var preview = _session.Refresh(CurrentDrawingOnly);
                var lines = preview.VisibleLines;
                RenderLedger(lines, preview.ProjectLines);
                ApplyButton.IsEnabled = false;
                UpdateSelectedState();
                if (lines.Count == 0)
                {
                    SummaryText.Text = $"{LineCountLabel}: 0.";
                    ProblemsText.Text = "Нет.";
                    StatusText.Text = EmptyScopeStatus;
                    return;
                }
                SummaryText.Text = $"Строк в ведомости: {LedgerGrid.Items.Count}. {LineCountLabel}: {lines.Count}. Загрузите файл ВТ, чтобы увидеть план изменений.";
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

        void ResetVtPlan()
        {
            _session.ResetWorkbook();
            ApplyButton.IsEnabled = false;
            UpdateSelectedState();
            SummaryText.Text = "Файл ВТ не загружен.";
            ProblemsText.Text = "";
            StatusText.Text = "Нажмите «Загрузить», чтобы построить план из выбранного файла ВТ.";
        }

        void VtPathBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded) ResetVtPlan();
        }

        void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            ResetVtPlan();
            var path = (VtPathBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show(this, "Выберите файл ВТ через кнопку «Обзор...».",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var vt = _session.LoadWorkbook(path);
                if (vt.Error != null)
                {
                    MessageBox.Show(this, vt.Error, "Синхронизация ВТ",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (vt.Rows.Count == 0)
                {
                    MessageBox.Show(this,
                        "В файле не найдено ни одной строки ВТ с заполненным «n/n».",
                        "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _settings.LastVtFilePath = path;
                try { RebuildPlanAndRender(offerIdFill: true); }
                catch (Exception ex)
                {
                    // The ВТ file was read fine (VtReader reports its own
                    // errors via vt.Error and never throws) — what failed is
                    // reading the project lines; say so.
                    MessageBox.Show(this, $"Файл ВТ прочитан, но подготовка синхронизации не завершена:\n{ex.Message}",
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
        /// "Применить" so the grid shows the
        /// post-write state without closing the window.
        /// </summary>
        void RebuildPlanAndRender(bool offerIdFill = false)
        {
            _session.InvalidatePreview();
            ApplyButton.IsEnabled = false;
            UpdateSelectedState();
            var preview = _session.Refresh(CurrentDrawingOnly, offerIdFill);
            if (preview.IdFillProposal != null && OfferIdFill(preview.IdFillProposal))
                preview = _session.Refresh(CurrentDrawingOnly);
            RenderLedger(preview.VisibleLines, preview.ProjectLines);
            RenderSummaryAndProblems(_session.WorkbookRowCount, preview.VisibleLines.Count);
            UpdateSelectedState();
            if (preview.VisibleLines.Count == 0)
                StatusText.Text = EmptyScopeStatus;
        }

        bool OfferIdFill(LineSyncPlan proposal)
        {
            if (proposal.Actions.Count == 0) return false;

            const int previewLimit = 10;
            var releases = proposal.Actions.Where(action => string.IsNullOrEmpty(action.Changes[0].NewValue))
                .ToDictionary(action => action.Changes[0].OldValue.Trim(), StringComparer.OrdinalIgnoreCase);
            var assignments = proposal.Actions.Where(action => !string.IsNullOrEmpty(action.Changes[0].NewValue)).ToList();
            // A transfer must always show both tags in the confirmation, even in a large batch.
            var previewActions = assignments.Where(action => releases.ContainsKey(action.Changes[0].NewValue))
                .Concat(assignments.Where(action => !releases.ContainsKey(action.Changes[0].NewValue)).Take(previewLimit)).ToList();
            var preview = string.Join("\n", previewActions.Select(action =>
            {
                var id = action.Changes[0].NewValue;
                var assignment = $"• {action.Line.Tag} → ID {id}";
                return releases.TryGetValue(id, out var release)
                    ? assignment + $"\n  Освободить ID у «{release.Line.Tag}» (PnPID {release.Line.RowId}, запись без деталей)." : assignment;
            }));
            if (assignments.Count > previewActions.Count)
                preview += $"\n… и ещё {assignments.Count - previewActions.Count}.";

            Mouse.OverrideCursor = null;
            var confirm = MessageBox.Show(this,
                $"Найдено линий для заполнения ID на текущем чертеже: {assignments.Count}.\n\n"
                + $"{preview}\n\nЗаполнить пустые поля ID значениями из колонки n/n ВТ?",
                "Заполнение ID по обозначению", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            Mouse.OverrideCursor = Cursors.Wait;
            if (confirm != MessageBoxResult.Yes) return false;

            var (stale, errors) = _session.ApplyIdFill(proposal);
            if (stale)
            {
                MessageBox.Show(this, "Данные линий изменились. Загрузите ВТ повторно, чтобы проверить совпадения.",
                    "Заполнение ID по обозначению", MessageBoxButton.OK, MessageBoxImage.Warning);
                return true;
            }

            if (errors.Count == 0)
                Applied = true;
            else
                MessageBox.Show(this, string.Join("\n", errors), "Заполнение ID по обозначению",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            return true;
        }

        /// <summary>
        /// Builds the "внутренняя ведомость" grid: one row per project
        /// line with a non-blank ID, one column per property
        /// СИНХРОНИЗАЦИЯ ВТ knows how to write. Columns are generated in
        /// code from the field mappings. The projected row retains typed
        /// PnPIDs and the original tag for selection and actions.
        /// </summary>
        // The ledger already shows the tag; the numeric line number is part of it.
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

        void RenderLedger(List<LineGroupInfo> lines, List<LineGroupInfo> projectLines)
        {
            var visibleColumns = LedgerColumnOrder.Concat(VtFieldMappings.KnownLineGroupColumns)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => VtFieldMappings.KnownLineGroupColumnLabels.ContainsKey(name)
                    && LineLedgerProjection.IsLedgerVisibleColumn(name));

            LedgerGrid.Columns.Clear();
            AddLedgerColumn("ID", nameof(LineLedgerRow.Id), horizontalHeader: true, maxWidth: 58);
            AddLedgerColumn("Тег линии", nameof(LineLedgerRow.DisplayTag), horizontalHeader: true, maxWidth: 170);
            foreach (var name in visibleColumns)
                AddLedgerColumn(VtFieldMappings.KnownLineGroupColumnLabels[name], $"Values[{name}]");
            LedgerGrid.ItemsSource = LineLedgerProjection.BuildLedger(lines, projectLines: projectLines, matchedRowIds: Plan?.MatchedRowIds);
        }

        void AddLedgerColumn(string header, string path, bool horizontalHeader = false, double maxWidth = 58)
        {
            var cellStyle = new Style(typeof(TextBlock));
            cellStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            cellStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(path)));
            LedgerGrid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                HeaderTemplate = horizontalHeader ? null : MakeVerticalHeaderTemplate(header),
                Binding = new System.Windows.Data.Binding(path),
                SortMemberPath = path,
                ElementStyle = cellStyle,
                Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                MinWidth = 42,
                MaxWidth = maxWidth,
            });
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
            var linesTouched = Plan.Actions.Count(a => a.Changes.Count > 0);
            var fieldsTotal = Plan.TotalChangedFields();
            SummaryText.Text =
                $"Строк в ВТ с заполненным n/n: {vtRowCount}. {LineCountLabel}: {lineCount}. " +
                $"Сопоставлено линий: {Plan.MatchedRowIds.Count}. Линий с изменениями: {linesTouched}. Полей к изменению: {fieldsTotal}. " +
                $"Не найдено в проекте: {Plan.UnmatchedVtRows.Count}. " +
                $"Линий без соответствия в ВТ: {Plan.UnmatchedLines.Count}." +
                (Plan.DuplicateIds.Count > 0 ? $" ⚠ Повторяющихся ID в проекте: {Plan.DuplicateIds.Count}." : "") +
                (Plan.DuplicateVtNN.Count > 0 ? $" ⚠ Повторяющихся n/n в файле ВТ: {Plan.DuplicateVtNN.Count}." : "");

            var problems = new System.Text.StringBuilder();
            foreach (var warning in _session.WorkbookWarnings)
                problems.AppendLine($"⚠ {warning}");
            foreach (var id in Plan.DuplicateIds)
                problems.AppendLine($"⚠ ID «{id}» встречается у нескольких линий. Такой ID нельзя использовать для сопоставления без обозначения. Дубли без деталей можно удалить в режиме «Весь проект».");
            foreach (var nn in Plan.DuplicateVtNN)
                problems.AppendLine($"⚠ n/n «{nn}» повторяется в ВТ. Свойства можно синхронизировать по однозначному обозначению. Автозаполнение такого ID заблокировано.");
            foreach (var vtRow in Plan.UnmatchedVtRows)
                problems.AppendLine($"— Строка {vtRow.RowNumber} ВТ (n/n «{vtRow.NN}», обозначение «{vtRow.Designation?.Trim()}») не сопоставлена с линией проекта.");
            foreach (var reason in Plan.BlockedLines.Values.Distinct())
                problems.AppendLine($"— {reason}");
            foreach (var reason in Plan.IdConflicts.Values)
                problems.AppendLine($"⚠ {reason}");
            foreach (var reason in _session.IdFillSkips)
                problems.AppendLine($"— Автозаполнение ID на текущем чертеже пропущено. {reason}");
            foreach (var line in Plan.UnmatchedLines)
                problems.AppendLine($"— Линия «{line.Tag}» (ID «{line.Id}») в проекте не встречается в ВТ.");
            foreach (var action in Plan.Actions)
                foreach (var rej in action.PicklistRejections)
                    problems.AppendLine($"— ID «{action.Line.Id}»: {rej}");
            ProblemsText.Text = problems.Length > 0 ? problems.ToString() : "Нет.";

            ApplyButton.IsEnabled = fieldsTotal > 0;
            StatusText.Text = fieldsTotal > 0
                ? $"Найдено изменений: {fieldsTotal}. Нажмите «Применить в Plant 3D»."
                : Plan.BlockedLines.Count > 0 || Plan.DuplicateVtNN.Count > 0
                    ? "Нет изменений для записи. Проверьте конфликты в списке проблем."
                    : "Нет изменений для записи.";
        }

        void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_session.HasWorkbook) { RefreshLedgerOnly(); return; }
            Mouse.OverrideCursor = Cursors.Wait;
            try { RebuildPlanAndRender(); }
            catch (Exception ex)
            {
                // Same as Load/RefreshLedgerOnly: never let a read failure
                // escape a click handler (it would crash the AutoCAD session).
                MessageBox.Show(this, $"Не удалось прочитать линии из Plant 3D:\n{ex.GetType().Name}: {ex.Message}",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { Mouse.OverrideCursor = null; }
        }

        void LedgerScope_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _session.InvalidatePreview();
            ApplyButton.IsEnabled = false;
            LedgerGrid.ItemsSource = null;
            SummaryText.Text = "";
            ProblemsText.Text = "";
            StatusText.Text = "Обновите ведомость, чтобы прочитать линии выбранной области.";
            UpdateSelectedState();
            RefreshButton_Click(sender, e);
        }

        void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        bool ConfirmServiceLifeColumn()
        {
            var cursor = Mouse.OverrideCursor;
            Mouse.OverrideCursor = null;
            try
            {
                var confirm = MessageBox.Show(this,
                    "Для записи срока службы нужно добавить свойство «Срок службы, лет» (srok) в схему проекта Plant 3D. Добавить свойство и продолжить синхронизацию?",
                    "Изменение схемы проекта", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                return confirm == MessageBoxResult.Yes;
            }
            finally { Mouse.OverrideCursor = cursor; }
        }

        void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            var plan = Plan;
            var fieldsTotal = plan?.TotalChangedFields() ?? 0;
            if (fieldsTotal == 0) return;

            var linesTouched = plan.Actions.Count(action => action.Changes.Count > 0);
            var scope = CurrentDrawingOnly ? "на текущем чертеже" : "во всём проекте Plant 3D";
            var confirm = MessageBox.Show(this,
                $"Будет изменено полей: {fieldsTotal} на {linesTouched} линиях {scope}.\n\n"
                + "Действие нельзя отменить средствами плагина (только вручную в Plant 3D). Продолжить?",
                "Синхронизация ВТ", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            ApplyConfirmedPlan(plan, $"Готово. Изменено полей: {fieldsTotal} на {linesTouched} линиях.",
                "Синхронизация не завершена.");
        }

        void ApplyConfirmedPlan(LineSyncPlan plan, string success, string failure, IReadOnlyCollection<int> rowIds = null)
        {
            Mouse.OverrideCursor = Cursors.Wait;
            IReadOnlyList<string> errors;
            try
            {
                var result = _session.Apply(plan, ConfirmServiceLifeColumn);
                if (result.Cancelled) return;
                errors = result.Errors;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Синхронизация прервана:\n{ex.GetType().Name}: {ex.Message}",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Error);
                RefreshAfterWrite(false, rowIds);
                return;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            var confirmed = errors.Count == 0;
            if (confirmed) Applied = true;
            if (!_settings.LoadFailed)
                try { _settings.Save(); }
                catch (Exception ex) { _trace?.Invoke($"Не удалось сохранить настройки: {ex.Message}"); }

            RefreshAfterWrite(confirmed, rowIds);
            if (confirmed)
            {
                MessageBox.Show(this, success + "\n\n"
                    + "Значения записаны в проектную базу Plant 3D (P3dLineGroup). "
                    + "Если на деталях этих линий настроено наследование свойств "
                    + "(правила Property Acquisition), для их обновления может "
                    + "понадобиться пересохранить/переоткрыть соответствующие чертежи.",
                    "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBox.Show(this,
                $"{failure} Ошибок: {errors.Count}. Проверьте состояние проекта.\n\n"
                + string.Join("\n", errors.Take(20))
                + (errors.Count > 20 ? $"\n… и ещё {errors.Count - 20}." : ""),
                "Синхронизация ВТ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        void RefreshAfterWrite(bool confirmed, IReadOnlyCollection<int> rowIds)
        {
            if (!_session.HasWorkbook) return;
            try
            {
                RebuildPlanAndRender();
                ReselectLine(rowIds);
            }
            catch (Exception ex)
            {
                _trace?.Invoke($"Не удалось перечитать линии после записи: {ex.GetType().Name}: {ex.Message}");
                StatusText.Text = confirmed
                    ? "Запись подтверждена, но обновить ведомость не удалось."
                    : "Запись завершилась с ошибкой, перечитать линии не удалось.";
            }
        }

        LineLedgerRow SelectedRow => LedgerGrid.SelectedItem as LineLedgerRow;

        List<int> GetSelectedRowIds() => SelectedRow?.AllRowIds.ToList() ?? new List<int>();

        string GetSelectedLineId() => SelectedRow?.Id?.Trim() ?? "";

        string GetSelectedLineTag() => SelectedRow?.Tag?.Trim() ?? "";

        List<LineSyncAction> GetSelectedActions()
        {
            var rowIds = GetSelectedRowIds().ToHashSet();
            return Plan?.Actions.Where(action => rowIds.Contains(action.Line.RowId)).ToList()
                ?? new List<LineSyncAction>();
        }

        void UpdateSelectedState()
        {
            var rowIds = GetSelectedRowIds();
            var lineId = GetSelectedLineId();
            var lineTag = GetSelectedLineTag();
            if (DeleteDuplicateMenuItem != null)
                DeleteDuplicateMenuItem.IsEnabled = rowIds.Count == 1 && SelectedRow?.HasProjectDuplicate == true;

            if (rowIds.Count == 0 && string.IsNullOrEmpty(lineId))
            {
                ApplySelectedButton.IsEnabled = false;
                if (SyncSelectedMenuItem != null) SyncSelectedMenuItem.IsEnabled = false;
                return;
            }

            if (Plan == null)
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

                var isUnmatchedLine = Plan.UnmatchedLines.Any(l => rowIds.Contains(l.RowId));
                var blockedReason = rowIds.Select(id => Plan.BlockedLines.GetValueOrDefault(id)).FirstOrDefault(reason => reason != null);

                if (blockedReason != null)
                {
                    StatusText.Text = blockedReason;
                }
                else if (isUnmatchedLine)
                {
                    StatusText.Text = $"Линия «{lineTag}» не сопоставлена со строкой ВТ.";
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
                dep = VisualTreeHelper.GetParent(dep);
            LedgerGrid.SelectedItem = (dep as DataGridRow)?.Item;
        }

        void DeleteDuplicateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var rowIds = GetSelectedRowIds();
            if (rowIds.Count != 1 || SelectedRow?.HasProjectDuplicate != true) return;

            var rowId = rowIds[0];
            var lineId = GetSelectedLineId();
            const string unavailable = "Запись можно удалить, если её ID повторяется и в Plant 3D удалось подтвердить отсутствие связей с деталями и другими записями проекта.";
            try
            {
                var candidate = _duplicates.FindDeletableDuplicate(rowId, lineId);
                if (candidate == null)
                {
                    MessageBox.Show(this, unavailable, "Удаление дубля", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var confirm = MessageBox.Show(this,
                    $"Удалить из проекта запись «{candidate.Line.Tag}» (ID «{lineId}», PnPID {rowId})?\n\n"
                    + "Удаление записи нельзя отменить командой AutoCAD «Отменить».",
                    "Удаление дубля", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (confirm != MessageBoxResult.Yes) return;

                candidate = _duplicates.FindDeletableDuplicate(rowId, lineId);
                if (candidate == null)
                {
                    MessageBox.Show(this, unavailable, "Удаление дубля", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                IReadOnlyList<string> errors;
                try
                {
                    errors = _duplicates.DeleteDuplicate(candidate);
                }
                finally
                {
                    _session.InvalidatePreview();
                    ApplyButton.IsEnabled = false;
                    UpdateSelectedState();
                    RefreshButton_Click(sender, e);
                }
                if (errors.Count == 0)
                    StatusText.Text = $"Удалён дубль ID «{lineId}» (PnPID {rowId}).";
                else
                    MessageBox.Show(this, string.Join("\n", errors), "Удаление дубля", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Не удалось удалить дубль:\n{ex.Message}",
                    "Удаление дубля", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void ReselectLine(IReadOnlyCollection<int> rowIds)
        {
            if (rowIds == null || rowIds.Count == 0 || LedgerGrid.ItemsSource is not IEnumerable<LineLedgerRow> rows) return;
            var targetIds = rowIds.ToHashSet();
            var row = rows.FirstOrDefault(item => item.AllRowIds.Any(targetIds.Contains));
            if (row == null) return;
            LedgerGrid.SelectedItem = row;
            LedgerGrid.ScrollIntoView(row);
        }

        void ApplySelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (Plan == null) return;

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

            ApplyConfirmedPlan(singlePlan,
                $"Готово. Линия «{lineTag}» (ID «{lineId}») успешно синхронизирована.\nИзменено полей: {changes.Count}.",
                $"Синхронизация линии «{lineTag}» не завершена.", rowIds);
        }
    }
}
