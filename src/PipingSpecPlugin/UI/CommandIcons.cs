using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PipingSpecPlugin.UI
{
    // Vector artwork on a fixed 32-unit canvas, shared by ribbon and dialog buttons.
    public static class CommandIcons
    {
        public static ImageSource Create(string command)
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            var blue = (Brush)new BrushConverter().ConvertFromString("#369CE0");
            var green = (Brush)new BrushConverter().ConvertFromString("#36B68A");
            var amber = (Brush)new BrushConverter().ConvertFromString("#E9AB44");
            var paper = (Brush)new BrushConverter().ConvertFromString("#F1F5FA");
            var ink = (Brush)new BrushConverter().ConvertFromString("#48627B");
            void Shape(string path, Brush fill, Brush stroke)
            {
                var pen = stroke == null ? null : new Pen(stroke, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                group.Children.Add(new GeometryDrawing(fill, pen, Geometry.Parse(path)));
            }
            void Sheet()
            {
                Shape("M7,3 L21,3 27,9 27,29 7,29 Z", paper, blue);
                Shape("M21,3 L21,9 27,9 M11,14 L22,14 M11,19 L22,19 M11,24 L18,24", null, blue);
            }
            switch (command)
            {
                case "SELECTBYLAYER":
                    Shape("M3,8 L14,3 25,8 14,13 Z", blue, blue);
                    Shape("M3,14 L14,20 25,14 M3,21 L14,27", null, blue);
                    Shape("M18,15 L30,23 24,24 22,30 Z", amber, ink);
                    break;
                case "PSCOLORBYLAYER":
                    Shape("M3,9 L15,3 27,9 15,15 Z", blue, blue);
                    Shape("M3,16 L15,22 27,16 M3,23 L15,29 24,24", null, blue);
                    Shape("M25,13 C23,17 20,19 20,22 A5,5 0 0 0 30,22 C30,19 27,16 25,13 Z", amber, paper);
                    break;
                case "PSTOSOLID":
                    // Two small parts merging into one cube.
                    Shape("M3,10 L9,7 15,10 9,13 Z M3,10 L3,16 9,19 9,13", paper, blue);
                    Shape("M13,15 L22,11 30,15 22,19 Z", green, ink);
                    Shape("M13,15 L13,25 22,29 22,19 Z", green, ink);
                    Shape("M22,19 L30,15 30,25 22,29 Z", green, ink);
                    break;
                case "PSTRACING":
                    // A main pipe with two red tracer pipes along it, closed by a bend at the end.
                    Shape("M3,9 L29,9 L29,19 L3,19 Z", paper, blue);
                    Shape("M3,23 L23,23 A4,4 0 0 0 23,29 L3,29", null, (Brush)new BrushConverter().ConvertFromString("#D8453B"));
                    break;
                case "PSMCPTOGGLE":
                    // Two blocks joined by a link: the connection to an outside assistant.
                    Shape("M3,10 L13,10 13,22 3,22 Z", paper, blue);
                    Shape("M13,13 L19,13 M13,19 L19,19", null, blue);
                    Shape("M19,10 L29,10 29,22 19,22 Z", green, ink);
                    Shape("M7,15 L9,17 M23,15 L25,17", null, paper);
                    break;
                case "PSAUDIT":
                    Shape("M5,3 L21,3 21,28 5,28 Z", paper, blue);
                    Shape("M9,9 L16,9 M9,14 L14,14 M9,19 L12,19", null, blue);
                    Shape("M22,13 A6,6 0 1 1 22,25 A6,6 0 1 1 22,13", paper, green);
                    Shape("M26,24 L30,29 M19,19 L21,21 25,17", null, green);
                    break;
                case "WRITESPECPROJ":
                    Shape("M3,8 L20,8 20,29 3,29 Z", paper, blue);
                    Shape("M7,5 L24,5 24,26 7,26 Z", paper, blue);
                    Shape("M11,2 L28,2 28,23 11,23 Z", paper, blue);
                    Shape("M15,8 L24,8 M15,13 L24,13 M15,18 L21,18", null, blue);
                    break;
                case "WRITESPECDWG": Sheet(); break;
                case "INSULSPECDWG":
                    // A pipe end inside its insulation jacket.
                    Shape("M16,4 A12,12 0 1 1 16,28 A12,12 0 1 1 16,4", amber, ink);
                    Shape("M16,10 A6,6 0 1 1 16,22 A6,6 0 1 1 16,10", paper, blue);
                    Shape("M16,13 A3,3 0 1 1 16,19 A3,3 0 1 1 16,13", blue, null);
                    break;
                case "INSULHELP":
                    // An open book.
                    Shape("M3,7 L15,9 15,28 3,26 Z", paper, blue);
                    Shape("M29,7 L17,9 17,28 29,26 Z", paper, blue);
                    Shape("M6,12 L12,13 M6,16 L12,17 M6,20 L12,21 M20,13 L26,12 M20,17 L26,16 M20,21 L26,20", null, blue);
                    break;
                case "INSULSPECPROJ":
                    // Several insulated pipe ends (the whole project).
                    Shape("M11,2 A8,8 0 1 1 11,18 A8,8 0 1 1 11,2", paper, blue);
                    Shape("M19,8 A11,11 0 1 1 19,30 A11,11 0 1 1 19,8", amber, ink);
                    Shape("M19,13.5 A5.5,5.5 0 1 1 19,24.5 A5.5,5.5 0 1 1 19,13.5", paper, blue);
                    Shape("M19,16.3 A2.7,2.7 0 1 1 19,21.7 A2.7,2.7 0 1 1 19,16.3", blue, null);
                    break;
                case "MOVEBYTAG":
                    Shape("M3,21 L16,15 29,21 16,28 Z", paper, blue);
                    Shape("M3,15 L16,9 29,15 16,22 Z", paper, blue);
                    Shape("M3,9 L16,3 29,9 16,16 Z", blue, blue);
                    break;
                case "SYNCVT":
                    Shape("M3,5 L23,5 23,25 3,25 Z", paper, green);
                    Shape("M3,11 L23,11 M3,18 L19,18 M10,11 L10,25", null, green);
                    Shape("M18,16 L29,16 26,13 M29,16 L26,19 M29,24 L18,24 21,21 M18,24 L21,27", null, blue);
                    break;
                case "SELECTBYSTATUS":
                    Shape("M4,4 L22,4 22,28 4,28 Z", paper, blue);
                    Shape("M8,10 L10,12 14,8 M8,20 L10,22 14,18", null, green);
                    Shape("M19,14 L29,21 24,22 22,28 Z", amber, ink);
                    break;
                case "SELECTBYLINE":
                    Shape("M4,25 L4,9 18,9 18,4", null, blue);
                    Shape("M1,22 L7,22 M1,12 L7,12 M15,6 L21,6", null, blue);
                    Shape("M17,14 L29,22 23,23 21,29 Z", amber, ink);
                    break;
                case "SPECABOUT":
                    Shape("M16,3 A13,13 0 1 1 16,29 A13,13 0 1 1 16,3", blue, null);
                    Shape("M16,14 L16,24 M16,9 L16,9.5", null, paper);
                    break;
                case "BROWSE":
                    Shape("M3,8 L12,8 15,11 28,11 28,26 3,26 Z", amber, ink);
                    Shape("M3,15 L30,15 26,26 3,26 Z", amber, ink);
                    break;
                case "SHOW":
                    // A magnifier over a part.
                    Shape("M3,11 L16,11 16,27 3,27 Z", paper, blue);
                    Shape("M20,4 A7,7 0 1 1 20,18 A7,7 0 1 1 20,4", paper, green);
                    Shape("M25,16 L30,22", null, green);
                    break;
                case "LOAD":
                    Shape("M5,21 L5,28 27,28 27,21 M16,3 L16,21 M9,14 L16,21 23,14", null, blue);
                    break;
                case "REFRESH":
                    Shape("M26,12 A11,11 0 0 0 7,7 L3,11 M3,4 L3,11 10,11 M6,20 A11,11 0 0 0 25,25 L29,21 M22,21 L29,21 29,28", null, blue);
                    break;
                case "APPLY":
                    Shape("M16,3 A13,13 0 1 1 16,29 A13,13 0 1 1 16,3", green, null);
                    Shape("M8,16 L14,22 24,11", null, paper);
                    break;
                case "CLOSE": Shape("M8,8 L24,24 M24,8 L8,24", null, ink); break;
                default: Sheet(); break;
            }
            group.Freeze();
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        public static void Decorate(Button button, string command)
        {
            var label = button.Content as string ?? "";
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new Image { Source = Create(command), Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) });
            panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            button.Content = panel;
            System.Windows.Automation.AutomationProperties.SetName(button, label);
        }
    }
}
