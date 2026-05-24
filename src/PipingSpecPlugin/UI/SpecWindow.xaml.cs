using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PipingSpecPlugin.UI
{
    /// <summary>
    /// First WPF window in the plug-in. Deliberately bare — its purpose is
    /// to prove WPF dispatch and modeless behaviour cohabit with AutoCAD's
    /// own framework before we start binding real ViewModels. The two
    /// trees mirror the layout sketched in the project roadmap (status /
    /// category / line columns); we only populate the category and class
    /// trees today.
    /// </summary>
    public partial class SpecWindow : Window
    {
        public SpecWindow()
        {
            InitializeComponent();
        }

        public void Populate(string projectName,
                             IReadOnlyDictionary<string, int> byCategory,
                             IReadOnlyDictionary<string, int> byClass)
        {
            ProjectHeader.Text = $"Project: {projectName}    (rows: {byCategory.Values.Sum()})";

            CategoryTree.Items.Clear();
            foreach (var kv in byCategory.OrderByDescending(kv => kv.Value))
                CategoryTree.Items.Add(new TreeViewItem { Header = $"{kv.Value,6}  {kv.Key}" });

            ClassTree.Items.Clear();
            foreach (var kv in byClass.OrderByDescending(kv => kv.Value))
                ClassTree.Items.Add(new TreeViewItem { Header = $"{kv.Value,6}  {kv.Key}" });
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
