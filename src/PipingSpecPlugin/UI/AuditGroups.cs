using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;

namespace PipingSpecPlugin.UI
{
    // Result groups of the audit windows (Plant 3D and Navisworks): one
    // collapsible block per check, «Коллизии с другими элементами — 8»,
    // folded at first — the user opens the check they are working on.
    public static class AuditGroups
    {
        const string Xaml = @"
<Style TargetType='GroupItem' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='GroupItem'>
        <Expander IsExpanded='False' Margin='0,2,0,2'>
          <Expander.Header>
            <TextBlock FontWeight='SemiBold' Margin='4,6,0,6'>
              <TextBlock.Text>
                <MultiBinding StringFormat='{}{0} — {1}'>
                  <Binding Path='Name'/>
                  <Binding Path='ItemCount'/>
                </MultiBinding>
              </TextBlock.Text>
            </TextBlock>
          </Expander.Header>
          <ItemsPresenter/>
        </Expander>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

        // Call once the columns are added. With every group folded the grid
        // has no rows to lay out and shrinks its columns to their minimum
        // widths, and keeps them so when a group opens: the widths are set
        // again on each opening.
        public static void Apply(DataGrid grid)
        {
            grid.GroupStyle.Add(new GroupStyle { ContainerStyle = (Style)XamlReader.Parse(Xaml) });
            var widths = grid.Columns.Select(c => c.Width).ToList();
            void Reset()
            {
                for (int i = 0; i < widths.Count && i < grid.Columns.Count; i++)
                {
                    grid.Columns[i].Width = 0;
                    grid.Columns[i].Width = widths[i];
                }
            }
            grid.AddHandler(Expander.ExpandedEvent, new RoutedEventHandler((_, __) =>
                grid.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Reset))));
        }
    }
}
