using System.Windows;

namespace PipingSpecPlugin.UI
{
    /// <summary>
    /// «Методика» of the insulation input data — opened from the insulation
    /// dialog and from the ribbon (INSULHELP). Static text; keep it in step
    /// with InsulationTakeoff when the rules change.
    /// </summary>
    public partial class InsulationHelpWindow : Window
    {
        public InsulationHelpWindow() => InitializeComponent();

        void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
