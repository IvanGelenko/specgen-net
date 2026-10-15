using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.AutoCAD.Runtime;
using PipingSpecPlugin.Layers;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PipingSpecPlugin.Commands
{
    public sealed class SelectByLayerCommand
    {
        [CommandMethod("SELECTBYLAYER", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void Run()
        {
            var document = AcadApp.DocumentManager.MdiActiveDocument;
            if (document == null) return;
            try
            {
                var choices = LayerSelectionService.Read(document.Database);
                if (choices.Count == 0) { AcadApp.ShowAlertDialog("В текущем пространстве чертежа нет элементов."); return; }
                var window = new Window { Title = "Выбрать по слою", Width = 560, Height = 510,
                    MinWidth = 400, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 13 };
                new WindowInteropHelper(window).Owner = AcadApp.MainWindow.Handle;
                var root = new DockPanel { Margin = new Thickness(14) };
                var top = new StackPanel();
                top.Children.Add(new TextBlock { Text = "Выберите слой — будут выделены все его элементы.", TextWrapping = TextWrapping.Wrap });
                top.Children.Add(new TextBlock { Text = "Поиск по названию слоя", Margin = new Thickness(0, 12, 0, 4) });
                var search = new TextBox { Margin = new Thickness(0, 0, 0, 10) }; top.Children.Add(search);
                DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
                var bottom = new StackPanel();
                bottom.Children.Add(new TextBlock { Text = "Текущее пространство модели или листа. Блоки и внешние ссылки выбираются целиком по слою вставки.",
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10), FontSize = 12 });
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var ok = new Button { Content = "Выбрать", Width = 110, Height = 32, IsDefault = true };
                var cancel = new Button { Content = "Отмена", Width = 100, Height = 32, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
                buttons.Children.Add(ok); buttons.Children.Add(cancel); bottom.Children.Add(buttons);
                DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
                var list = new ListBox { ItemsSource = choices, SelectedIndex = 0 };
                root.Children.Add(list); window.Content = root;
                search.TextChanged += (_, __) =>
                {
                    list.ItemsSource = choices.Where(c => c.Name.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                    list.SelectedIndex = list.Items.Count > 0 ? 0 : -1;
                };
                list.SelectionChanged += (_, __) => ok.IsEnabled = list.SelectedItem != null;
                ok.Click += (_, __) => { if (list.SelectedItem != null) window.DialogResult = true; };
                list.MouseDoubleClick += (_, __) => { if (list.SelectedItem != null) window.DialogResult = true; };
                if (window.ShowDialog() != true || list.SelectedItem is not LayerSelectionChoice selected) return;
                document.Editor.SetImpliedSelection(selected.Ids);
                document.Editor.WriteMessage($"\n[Выбор по слою] Слой «{selected.Name}»: выделено элементов {selected.Ids.Length}.");
            }
            catch (System.Exception ex) { AcadApp.ShowAlertDialog("Не удалось выбрать элементы по слою: " + ex.Message); }
        }
    }
}
