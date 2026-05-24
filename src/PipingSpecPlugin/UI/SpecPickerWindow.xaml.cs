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
    /// Picker dialog mirroring the Python edition's tkinter UI. Lets the
    /// user filter by status / category / line and pick exclude rules
    /// before producing the spec. All choices persist into PluginSettings
    /// so the next launch starts where this one ended (matches the
    /// "ui_prefs" key the Python edition writes — same shape so users
    /// flipping between editions don't have to re-tick everything).
    /// </summary>
    public partial class SpecPickerWindow : Window
    {
        readonly ProjectFacts _facts;
        readonly PluginSettings _settings;
        readonly bool _activeDrawingAvailable;

        // Populated by the user picking "Сгенерировать"; null if cancelled.
        public SpecPickerResult Result { get; private set; }

        public SpecPickerWindow(ProjectFacts facts,
                                string projectName,
                                string dwgName,
                                PluginSettings settings,
                                bool activeDrawingAvailable)
        {
            InitializeComponent();
            _facts = facts;
            _settings = settings;
            _activeDrawingAvailable = activeDrawingAvailable;

            ProjectNameText.Text = projectName ?? "";
            DwgNameText.Text = string.IsNullOrEmpty(dwgName) ? "<не передан>" : dwgName;

            // Disable "active drawing" scope when no drawing is open.
            if (!activeDrawingAvailable) ScopeDrawing.IsEnabled = false;

            ApplySavedScope();
            TemplatePathBox.Text = settings.TemplatePath ?? "";

            PopulateStatuses();
            PopulateCategories();
            PopulateLines();

            GroupByLineChk.IsChecked = settings.GroupByLine;
            ApplySavedExclusions();
            UpdateBlockingReason();
        }

        // ----- population ----------------------------------------------------

        void PopulateStatuses()
        {
            // No saved selection ⇒ no preselection (user sees what's there
            // and ticks what they want). With a saved selection from a
            // previous run, restore exactly that set so cross-session
            // behaviour matches Python.
            var saved = _settings.Statuses == null
                ? null
                : new HashSet<string>(_settings.Statuses);
            foreach (var (key, count) in _facts.Statuses)
            {
                var label = key == SpecConstants.NullStatus
                    ? SpecConstants.NullStatusLabel
                    : key;
                var cb = new CheckBox
                {
                    Content = $"{label}  ({count})",
                    Tag = key,
                    Margin = new Thickness(0, 2, 0, 2),
                    IsChecked = saved == null ? true : saved.Contains(key),
                };
                StatusList.Items.Add(cb);
            }
        }

        void PopulateCategories()
        {
            // First run: every category except the NullCategory bucket
            // ticked. Restoring a saved set picks up where the user left.
            var saved = _settings.Categories == null
                ? null
                : new HashSet<string>(_settings.Categories);
            foreach (var (key, count) in _facts.Categories)
            {
                var label = key == SpecConstants.NullCategory
                    ? SpecConstants.NullCategoryLabel
                    : (SpecConstants.CategoryLabelRu.TryGetValue(key, out var ru) ? ru : key);
                bool defaultOn = key != SpecConstants.NullCategory;
                var cb = new CheckBox
                {
                    Content = $"{label}  ({count})",
                    Tag = key,
                    Margin = new Thickness(0, 2, 0, 2),
                    IsChecked = saved != null ? saved.Contains(key) : defaultOn,
                };
                CategoryList.Items.Add(cb);
            }
        }

        void PopulateLines()
        {
            var saved = _settings.Lines == null
                ? null
                : new HashSet<string>(_settings.Lines);
            foreach (var (key, count) in _facts.Lines)
            {
                var label = key == SpecConstants.NullLine
                    ? $"{SpecConstants.NullLineLabel}  ({count})"
                    : $"{key}  ({count})";
                var cb = new CheckBox
                {
                    Content = label,
                    Tag = key,
                    Margin = new Thickness(0, 2, 0, 2),
                    IsChecked = saved != null ? saved.Contains(key) : true,
                };
                LineList.Items.Add(cb);
            }
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
            ExcludeEquipmentChk.IsChecked = _settings.ExcludeEquipment;
            ExcludeConnectorsChk.IsChecked = _settings.ExcludeConnectors;
        }

        void UpdateBlockingReason()
        {
            // Mirror the Python edition: if the project genuinely has no
            // candidates the user can pick, disable the run button and
            // explain why instead of letting them press it and get a
            // "select at least one X" warning.
            string reason = null;
            if (_facts.Statuses.Count == 0)
                reason = "В проекте не найдено компонентов ни с одним статусом.";
            else if (_facts.Categories.Count == 0)
                reason = "В проекте нет компонентов в поддерживаемых категориях.";
            if (reason != null)
            {
                GenerateButton.IsEnabled = false;
                StatusText.Text = reason;
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
                ExcludeEquipment = ExcludeEquipmentChk.IsChecked == true,
                ExcludeConnectors = ExcludeConnectorsChk.IsChecked == true,
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
    /// command. Same shape as the Python edition's "run_args" dict so
    /// the persisted PluginSettings can map across editions.
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
    }
}
