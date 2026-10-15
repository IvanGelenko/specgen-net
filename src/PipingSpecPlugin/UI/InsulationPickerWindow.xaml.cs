using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PipingSpecPlugin.Settings;
using PipingSpecPlugin.Spec;

namespace PipingSpecPlugin.UI
{
    /// <summary>
    /// INSULSPEC dialog: scope, statuses and lines of the insulated pipes /
    /// bends / elbows / tees. Choices persist into PluginSettings after a
    /// successful run.
    /// </summary>
    public partial class InsulationPickerWindow : Window
    {
        const string Caption = "Данные для расчёта изоляции";

        readonly ProjectFacts _facts;
        readonly ProjectFacts _drawingFacts;
        readonly PluginSettings _settings;

        public InsulationPickerResult Result { get; private set; }

        /// <param name="facts">Statuses / lines of the insulated parts of the project.</param>
        /// <param name="drawingFacts">The same for the active drawing.</param>
        /// <param name="forcedScope">
        /// "proj" / "dwg" when the command came from a ribbon button: the
        /// scope radios show it but are locked, exactly like the spec picker.
        /// </param>
        public InsulationPickerWindow(ProjectFacts facts, ProjectFacts drawingFacts,
                                      string projectName, string dwgName,
                                      PluginSettings settings, bool activeDrawingAvailable,
                                      string forcedScope = null)
        {
            InitializeComponent();
            _facts = facts;
            _drawingFacts = drawingFacts;
            _settings = settings;

            ProjectNameText.Text = projectName ?? "";
            DwgNameText.Text = string.IsNullOrEmpty(dwgName) ? "<не открыт>" : dwgName;
            OpenAfterGenerateChk.IsChecked = settings.OpenAfterGenerate;

            if (!activeDrawingAvailable) ScopeDrawing.IsEnabled = false;
            var scope = forcedScope ?? settings.InsulationScope;
            if (string.Equals(scope, "dwg", System.StringComparison.OrdinalIgnoreCase) && activeDrawingAvailable)
                ScopeDrawing.IsChecked = true;
            else
                ScopeProject.IsChecked = true;
            if (forcedScope == "proj" || forcedScope == "dwg")
            {
                ScopeProject.IsEnabled = false;
                ScopeDrawing.IsEnabled = false;
            }
            PopulateFilters();
        }

        ProjectFacts CurrentFacts => ScopeDrawing.IsChecked == true ? _drawingFacts : _facts;

        void Scope_Checked(object sender, RoutedEventArgs e)
        {
            // Fires from InitializeComponent before the facts are set.
            if (_facts == null || _drawingFacts == null) return;
            PopulateFilters();
        }

        void PopulateFilters()
        {
            var currentStatuses = StatusList.Items.Count > 0 ? CheckedKeys(StatusList) : null;
            var currentLines = LineList.Items.Count > 0 ? CheckedKeys(LineList) : null;
            StatusList.Items.Clear();
            LineList.Items.Clear();
            var facts = CurrentFacts;
            Fill(StatusList, facts.Statuses, currentStatuses, _settings.InsulationStatuses, _settings.KnownInsulationStatuses,
                 key => key == SpecConstants.NullStatus ? SpecConstants.NullStatusLabel : key);
            Fill(LineList, facts.Lines, currentLines, _settings.InsulationLines, _settings.KnownInsulationLines,
                 key => key == SpecConstants.NullLine ? SpecConstants.NullLineLabel : key);

            GenerateButton.IsEnabled = facts.Lines.Count > 0;
            StatusText.Text = facts.Lines.Count > 0
                ? "Готов"
                : "В выбранной области нет труб, отводов, изгибов и тройников с толщиной изоляции больше 0.";
        }

        /// <summary>
        /// Same rule as the spec picker: a key checked before (or never seen
        /// before) starts checked, a key seen and unchecked stays unchecked.
        /// </summary>
        static void Fill(ItemsControl list, List<(string key, int count)> items, ICollection<string> current,
                         string[] saved, string[] known, System.Func<string, string> label)
        {
            var selected = current ?? (saved == null ? null : new HashSet<string>(saved));
            var knownSet = known == null ? null : new HashSet<string>(known);
            foreach (var (key, count) in items)
            {
                var isChecked = selected == null || selected.Contains(key) || (knownSet != null && !knownSet.Contains(key));
                list.Items.Add(new CheckBox
                {
                    Content = $"{label(key)}  ({count})",
                    Tag = key,
                    Margin = new Thickness(0, 2, 0, 2),
                    IsChecked = isChecked,
                });
            }
        }

        static List<string> CheckedKeys(ItemsControl list)
            => list.Items.OfType<CheckBox>().Where(cb => cb.IsChecked == true && cb.Tag is string)
                   .Select(cb => (string)cb.Tag).ToList();

        void SelectAllLines_Click(object sender, RoutedEventArgs e) => SetAllLines(true);
        void DeselectAllLines_Click(object sender, RoutedEventArgs e) => SetAllLines(false);

        void SetAllLines(bool value)
        {
            foreach (var cb in LineList.Items.OfType<CheckBox>()) cb.IsChecked = value;
        }

        void HelpButton_Click(object sender, RoutedEventArgs e)
            => new InsulationHelpWindow { Owner = this }.ShowDialog();

        void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var statuses = CheckedKeys(StatusList);
            if (StatusList.Items.Count > 0 && statuses.Count == 0)
            {
                MessageBox.Show(this, "Выберите хотя бы один статус.", Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var lines = CheckedKeys(LineList);
            if (lines.Count == 0)
            {
                MessageBox.Show(this, "Выберите хотя бы одну линию.", Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Result = new InsulationPickerResult
            {
                Scope = ScopeDrawing.IsChecked == true ? "dwg" : "proj",
                Statuses = statuses,
                Lines = lines,
                OpenAfterGenerate = OpenAfterGenerateChk.IsChecked == true,
            };
            DialogResult = true;
        }
    }

    public class InsulationPickerResult
    {
        public string Scope;                   // "proj" or "dwg"
        public List<string> Statuses;
        public List<string> Lines;
        public bool OpenAfterGenerate;
    }
}
