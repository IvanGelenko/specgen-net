using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.Spec;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.UI
{
    /// <summary>
    /// Picker dialog. Lets the user filter by status / category / line and
    /// pick exclude rules before producing the spec. All choices persist
    /// into PluginSettings so the next launch starts where this one ended.
    /// </summary>
    public partial class SpecPickerWindow : Window
    {
        readonly ProjectFacts _facts;
        readonly ProjectFacts _activeFacts;
        readonly PluginSettings _settings;
        readonly bool _activeDrawingAvailable;
        readonly string _forcedScope;  // "proj", "dwg", or null

        // Populated by the user picking "Сгенерировать"; null if cancelled.
        public SpecPickerResult Result { get; private set; }

        public SpecPickerWindow(ProjectFacts facts,
                                ProjectFacts activeFacts,
                                string projectName,
                                string dwgName,
                                PluginSettings settings,
                                bool activeDrawingAvailable,
                                string forcedScope = null)
        {
            InitializeComponent();
            _facts = facts;
            _activeFacts = activeFacts;
            _settings = settings;
            _activeDrawingAvailable = activeDrawingAvailable;
            _forcedScope = forcedScope;

            ProjectNameText.Text = projectName ?? "";
            DwgNameText.Text = string.IsNullOrEmpty(dwgName) ? "<не передан>" : dwgName;

            // Disable "active drawing" scope when no drawing is open.
            if (!activeDrawingAvailable) ScopeDrawing.IsEnabled = false;

            ApplySavedScope();

            // When the user invoked one of the two scope-specific ribbon
            // buttons, both radios stay visible (so the chosen scope is
            // obvious) but become read-only — switching at this point
            // would contradict the button they just clicked.
            if (_forcedScope == "proj" || _forcedScope == "dwg")
            {
                ScopeProject.IsChecked = _forcedScope == "proj";
                ScopeDrawing.IsChecked = _forcedScope == "dwg";
                ScopeProject.IsEnabled = false;
                ScopeDrawing.IsEnabled = false;
            }
            TemplatePathBox.Text = settings.TemplatePath ?? "";

            PopulateFilters();

            GroupByLineChk.IsChecked = settings.GroupByLine;
            ApplySavedExclusions();

            DisableElementGroupingChk.Checked += DisableElementGroupingChk_Changed;
            DisableElementGroupingChk.Unchecked += DisableElementGroupingChk_Changed;
            IncludePipeBendsAsPipeChk.Checked += IncludePipeBendsAsPipeChk_Changed;
            IncludePipeBendsAsPipeChk.Unchecked += IncludePipeBendsAsPipeChk_Changed;
            UpdateExclusionStates();
        }

        // ----- population ----------------------------------------------------

        bool _isUpdatingExclusionStates = false;
        void UpdateExclusionStates()
        {
            if (_isUpdatingExclusionStates) return;
            _isUpdatingExclusionStates = true;
            try
            {
                if (DisableElementGroupingChk.IsChecked == true)
                {
                    IncludePipeBendsAsPipeChk.IsChecked = false;
                    IncludePipeBendsAsPipeChk.IsEnabled = false;
                }
                else
                {
                    IncludePipeBendsAsPipeChk.IsEnabled = true;
                }

                if (IncludePipeBendsAsPipeChk.IsChecked == true)
                {
                    DisableElementGroupingChk.IsChecked = false;
                    DisableElementGroupingChk.IsEnabled = false;
                }
                else
                {
                    DisableElementGroupingChk.IsEnabled = true;
                }
            }
            finally
            {
                _isUpdatingExclusionStates = false;
            }
        }

        void DisableElementGroupingChk_Changed(object sender, RoutedEventArgs e)
        {
            UpdateExclusionStates();
        }

        void IncludePipeBendsAsPipeChk_Changed(object sender, RoutedEventArgs e)
        {
            UpdateExclusionStates();
        }

        void PopulateFilters()
        {
            // 1. Save currently checked states if lists are already populated.
            HashSet<string> currentStatuses = null;
            HashSet<string> currentCategories = null;
            HashSet<string> currentLines = null;

            if (StatusList.Items.Count > 0)
                currentStatuses = GetCheckedKeys(StatusList);
            if (CategoryList.Items.Count > 0)
                currentCategories = GetCheckedKeys(CategoryList);
            if (LineList.Items.Count > 0)
                currentLines = GetCheckedKeys(LineList);

            // 2. Clear lists.
            StatusList.Items.Clear();
            CategoryList.Items.Clear();
            LineList.Items.Clear();

            // 3. Choose which facts to use.
            var activeFacts = (ScopeDrawing.IsChecked == true) ? _activeFacts : _facts;

            // 4. Populate Statuses.
            var statusSaved = currentStatuses ?? (_settings.Statuses == null ? null : new HashSet<string>(_settings.Statuses));
            var statusKnown = _settings.KnownStatuses == null ? null : new HashSet<string>(_settings.KnownStatuses);
            foreach (var (key, count) in activeFacts.Statuses)
            {
                var label = key == SpecConstants.NullStatus ? SpecConstants.NullStatusLabel : key;
                bool isChecked;
                if (statusSaved == null)
                {
                    isChecked = true;
                }
                else if (statusSaved.Contains(key))
                {
                    isChecked = true;
                }
                else if (statusKnown != null && !statusKnown.Contains(key))
                {
                    isChecked = true; // Brand new status
                }
                else
                {
                    isChecked = false;
                }

                var cb = new CheckBox
                {
                    Content = $"{label}  ({count})",
                    Tag = key,
                    Margin = new Thickness(0, 2, 0, 2),
                    IsChecked = isChecked,
                };
                StatusList.Items.Add(cb);
            }

            // 5. Populate Categories.
            var catSaved = currentCategories ?? (_settings.Categories == null ? null : new HashSet<string>(_settings.Categories));
            var catKnown = _settings.KnownCategories == null ? null : new HashSet<string>(_settings.KnownCategories);
            foreach (var (key, count) in activeFacts.Categories)
            {
                // Don't offer "Без категории": the builder unconditionally
                // drops parts whose category couldn't be classified (they're
                // unrecognised junk with no name/data and never belong in a
                // procurement spec). Showing the chip promised the user they
                // could include those parts, but ticking it produced nothing
                // — a count-vs-output mismatch. Hiding it makes the picker
                // honest.
                if (key == SpecConstants.NullCategory) continue;
                var label = SpecConstants.CategoryLabelRu.TryGetValue(key, out var ru) ? ru : key;
                bool defaultOn = true;
                bool isChecked;
                if (catSaved == null)
                {
                    isChecked = defaultOn;
                }
                else if (catSaved.Contains(key))
                {
                    isChecked = true;
                }
                else if (catKnown != null && !catKnown.Contains(key))
                {
                    isChecked = defaultOn; // Brand new category
                }
                else
                {
                    isChecked = false;
                }

                var cb = new CheckBox
                {
                    Content = $"{label}  ({count})",
                    Tag = key,
                    Margin = new Thickness(0, 2, 0, 2),
                    IsChecked = isChecked,
                };
                CategoryList.Items.Add(cb);
            }

            // 6. Populate Lines.
            var lineSaved = currentLines ?? (_settings.Lines == null ? null : new HashSet<string>(_settings.Lines));
            var lineKnown = _settings.KnownLines == null ? null : new HashSet<string>(_settings.KnownLines);
            foreach (var (key, count) in activeFacts.Lines)
            {
                var label = key == SpecConstants.NullLine
                    ? $"{SpecConstants.NullLineLabel}  ({count})"
                    : $"{key}  ({count})";
                bool isChecked;
                if (lineSaved == null)
                {
                    isChecked = true;
                }
                else if (lineSaved.Contains(key))
                {
                    isChecked = true;
                }
                else if (lineKnown != null && !lineKnown.Contains(key))
                {
                    isChecked = true; // Brand new line
                }
                else
                {
                    isChecked = false;
                }

                var cb = new CheckBox
                {
                    Content = label,
                    Tag = key,
                    Margin = new Thickness(0, 2, 0, 2),
                    IsChecked = isChecked,
                };
                LineList.Items.Add(cb);
            }

            UpdateBlockingReason();
        }

        static HashSet<string> GetCheckedKeys(ItemsControl list)
        {
            var keys = new HashSet<string>();
            foreach (var item in list.Items)
                if (item is CheckBox cb && cb.IsChecked == true && cb.Tag is string key)
                    keys.Add(key);
            return keys;
        }

        private void Scope_Checked(object sender, RoutedEventArgs e)
        {
            if (_facts == null || _activeFacts == null)
                return;

            PopulateFilters();
        }

        void ApplySavedScope()
        {
            var saved = (_settings.Scope ?? "proj").ToLowerInvariant();
            if (saved == "dwg" && _activeDrawingAvailable)
                ScopeDrawing.IsChecked = true;
            else
                ScopeProject.IsChecked = true;
        }

        void ApplySavedExclusions()
        {
            ExcludeValveCompanionsChk.IsChecked = _settings.ExcludeValveCompanions;
            IncludePipeBendsAsPipeChk.IsChecked = _settings.IncludePipeBendsAsPipe;
            OpenAfterGenerateChk.IsChecked = _settings.OpenAfterGenerate;
            DisableElementGroupingChk.IsChecked = _settings.DisableElementGrouping;
            CheckForUpdatesChk.IsChecked = _settings.CheckForUpdates;
            // ExcludeEquipment and ExcludeConnectors no longer have
            // standalone check-boxes — both ship as ordinary category
            // chips ('Оборудование', 'Сварные швы / коннекторы') in the
            // category list. The settings fields stay for back-compat.
        }

        void UpdateBlockingReason()
        {
            var activeFacts = (ScopeDrawing.IsChecked == true) ? _activeFacts : _facts;
            string reason = null;
            if (activeFacts.Statuses.Count == 0)
                reason = "В выбранной области не найдено компонентов ни с одним статусом.";
            else if (activeFacts.Categories.Count == 0)
                reason = "В выбранной области нет компонентов в поддерживаемых категориях.";

            if (reason != null)
            {
                GenerateButton.IsEnabled = false;
                StatusText.Text = reason;
            }
            else
            {
                GenerateButton.IsEnabled = true;
                StatusText.Text = "Готов";
            }
        }

        // ----- button handlers ----------------------------------------------

        void BrowseTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.OpenFileDialog
            {
                Title = "Выберите файл шаблона (.docx)",
                Filter = "Word документ (*.docx)|*.docx|Все файлы|*.*",
                CheckFileExists = true,
                FileName = TemplatePathBox.Text ?? "",
            };
            if (!string.IsNullOrEmpty(TemplatePathBox.Text))
            {
                var dir = Path.GetDirectoryName(TemplatePathBox.Text);
                if (Directory.Exists(dir)) dlg.InitialDirectory = dir;
            }
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                TemplatePathBox.Text = dlg.FileName;
        }

        void SelectAllLines_Click(object sender, RoutedEventArgs e)
            => SetAllLines(true);

        void DeselectAllLines_Click(object sender, RoutedEventArgs e)
            => SetAllLines(false);

        void SetAllLines(bool value)
        {
            foreach (var item in LineList.Items)
                if (item is CheckBox cb) cb.IsChecked = value;
        }

        void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var tpl = (TemplatePathBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(tpl))
            {
                MessageBox.Show(this,
                    "Выберите файл шаблона (.docx) через кнопку «Обзор...».",
                    "Спецификация Plant 3D",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!File.Exists(tpl))
            {
                MessageBox.Show(this, $"Файл шаблона не найден:\n{tpl}",
                    "Спецификация Plant 3D",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var selStatuses = CheckedKeys(StatusList);
            if (selStatuses.Count == 0)
            {
                MessageBox.Show(this, "Выберите хотя бы один статус.",
                    "Спецификация Plant 3D",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var selCats = CheckedKeys(CategoryList);
            if (CategoryList.Items.Count > 0 && selCats.Count == 0)
            {
                MessageBox.Show(this, "Выберите хотя бы одну категорию.",
                    "Спецификация Plant 3D",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var selLines = CheckedKeys(LineList);
            if (LineList.Items.Count > 0 && selLines.Count == 0)
            {
                MessageBox.Show(this, "Выберите хотя бы одну линию.",
                    "Спецификация Plant 3D",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new SpecPickerResult
            {
                TemplatePath = tpl,
                Scope = ScopeDrawing.IsChecked == true ? "dwg" : "proj",
                Statuses = selStatuses,
                Categories = selCats,
                Lines = selLines,
                GroupByLine = GroupByLineChk.IsChecked == true,
                ExcludeValveCompanions = ExcludeValveCompanionsChk.IsChecked == true,
                IncludePipeBendsAsPipe = IncludePipeBendsAsPipeChk.IsChecked == true,
                OpenAfterGenerate = OpenAfterGenerateChk.IsChecked == true,
                DisableElementGrouping = DisableElementGroupingChk.IsChecked == true,
                CheckForUpdates = CheckForUpdatesChk.IsChecked == true,
                // Equipment and connectors are toggled through their
                // category check-boxes now; the result fields stay only
                // for back-compat with the settings file. Both false so
                // the category filter is the sole gate.
                ExcludeEquipment = false,
                ExcludeConnectors = false,
            };
            DialogResult = true;
        }

        static List<string> CheckedKeys(ItemsControl list)
        {
            var keys = new List<string>();
            foreach (var item in list.Items)
                if (item is CheckBox cb && cb.IsChecked == true && cb.Tag is string key)
                    keys.Add(key);
            return keys;
        }
    }

    /// <summary>
    /// Plain-old data container the picker hands back to the WRITESPEC
    /// command.
    /// </summary>
    public class SpecPickerResult
    {
        public string TemplatePath;
        public string Scope;                 // "proj" or "dwg"
        public List<string> Statuses;
        public List<string> Categories;
        public List<string> Lines;
        public bool GroupByLine;
        public bool ExcludeValveCompanions;
        public bool ExcludeEquipment;
        public bool ExcludeConnectors;
        public bool IncludePipeBendsAsPipe;
        public bool OpenAfterGenerate;
        public bool DisableElementGrouping;
        public bool CheckForUpdates;
    }
}
