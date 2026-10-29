using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using WinForms = System.Windows.Forms;

namespace PipingSpecPlugin.Tracing
{
    /// <summary>The parameters of the tracing (the user's rules are the defaults).</summary>
    internal sealed class TracingParameters
    {
        public string Spec = "!_Rus-PipeAndFittings_ст20_PN16";
        public double Nd = 20, Wall = 4, Gap = 2, Radius = 90, MinGap = 1;   // MinGap: the smallest accepted gap (1, 2 or 3 mm in tight places; never an interference)
    }

    /// <summary>Choice of the lines to be traced: the lines of the drawing in a list, with a filter and the parameters.</summary>
    internal sealed class TracingLinesDialog : WinForms.Form
    {
        readonly List<TracingLine> _lines;
        readonly TracingParameters _par;
        readonly WinForms.ListView _list = new WinForms.ListView();
        readonly WinForms.TextBox _filter = new WinForms.TextBox();
        readonly WinForms.ComboBox _spec = new WinForms.ComboBox();
        readonly WinForms.TextBox _gap = new WinForms.TextBox(), _radius = new WinForms.TextBox(), _tol = new WinForms.TextBox();
        readonly WinForms.Button _ok = new WinForms.Button();
        readonly HashSet<string> _checked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public List<TracingLine> Selected => _lines.Where(l => _checked.Contains(l.Tag)).ToList();

        public TracingLinesDialog(List<TracingLine> lines, TracingParameters par, List<string> specs)
        {
            _lines = lines; _par = par;
            Text = "Спутник: выбор линий";
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            Size = new Size(680, 560); MinimumSize = new Size(560, 460);
            Font = new Font("Segoe UI", 9f);

            var top = new WinForms.Label { Text = "Отметьте линии, на которые нужно создать спутник:", Left = 12, Top = 12, Width = 620, AutoSize = false, Height = 20 };
            var fl = new WinForms.Label { Text = "Фильтр:", Left = 12, Top = 40, Width = 52, Height = 20 };
            _filter.SetBounds(70, 37, 300, 24); _filter.TextChanged += (s, e) => Fill();

            _list.SetBounds(12, 68, 640, 300);
            _list.Anchor = WinForms.AnchorStyles.Top | WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Left | WinForms.AnchorStyles.Right;
            _list.View = WinForms.View.Details; _list.CheckBoxes = true; _list.FullRowSelect = true; _list.GridLines = true;
            _list.Columns.Add("Номер линии", 300); _list.Columns.Add("DN", 90); _list.Columns.Add("Труб", 60, WinForms.HorizontalAlignment.Right);
            _list.Columns.Add("Длина, м", 90, WinForms.HorizontalAlignment.Right);
            _list.ItemChecked += (s, e) =>
            {
                if (_filling) return;
                if (e.Item.Checked) _checked.Add((string)e.Item.Tag); else _checked.Remove((string)e.Item.Tag);
                UpdateButton();
            };

            var all = new WinForms.Button { Text = "Отметить все", Left = 12, Top = 374, Width = 110, Height = 26, Anchor = WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Left };
            all.Click += (s, e) => SetAll(true);
            var none = new WinForms.Button { Text = "Снять все", Left = 128, Top = 374, Width = 110, Height = 26, Anchor = WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Left };
            none.Click += (s, e) => SetAll(false);

            var box = new WinForms.GroupBox { Text = "Параметры", Left = 12, Top = 400, Width = 640, Height = 78, Anchor = WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Left | WinForms.AnchorStyles.Right };
            box.Controls.Add(new WinForms.Label { Text = "Миникаталог (спецификация) трубы спутника:", Left = 10, Top = 22, Width = 190, Height = 34 });
            _spec.SetBounds(200, 19, 280, 24);
            _spec.DropDownStyle = specs.Count > 0 ? WinForms.ComboBoxStyle.DropDownList : WinForms.ComboBoxStyle.DropDown;   // the list of the project; typed by hand only when it cannot be read
            _spec.DropDownWidth = 520;
            foreach (var sn in specs) _spec.Items.Add(sn);
            int at = specs.FindIndex(x => string.Equals(x, par.Spec, StringComparison.OrdinalIgnoreCase));
            if (at < 0) at = specs.FindIndex(x => x.IndexOf("ст20", StringComparison.OrdinalIgnoreCase) >= 0);
            if (specs.Count > 0) _spec.SelectedIndex = at >= 0 ? at : 0; else _spec.Text = par.Spec;
            box.Controls.Add(_spec);
            box.Controls.Add(new WinForms.Label { Text = "Зазор, мм:", Left = 490, Top = 22, Width = 70 });
            _gap.SetBounds(562, 19, 60, 24); _gap.Text = Num(par.Gap); box.Controls.Add(_gap);
            box.Controls.Add(new WinForms.Label { Text = "Радиус изгиба, мм:", Left = 10, Top = 46, Width = 190 });
            _radius.SetBounds(200, 43, 80, 24); _radius.Text = Num(par.Radius); box.Controls.Add(_radius);
            box.Controls.Add(new WinForms.Label { Text = "Наименьший зазор, мм:", Left = 300, Top = 46, Width = 150 });
            _tol.SetBounds(452, 43, 60, 24); _tol.Text = Num(par.MinGap); box.Controls.Add(_tol);

            _ok.Text = "Рассчитать"; _ok.Width = 110; _ok.Height = 30; _ok.Left = 430; _ok.Top = 488; _ok.Enabled = false;
            _ok.Anchor = WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Right;
            _ok.Click += (s, e) => { if (ReadParameters()) { DialogResult = WinForms.DialogResult.OK; Close(); } };
            var cancel = new WinForms.Button { Text = "Отмена", Width = 100, Height = 30, Left = 552, Top = 488, DialogResult = WinForms.DialogResult.Cancel, Anchor = WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Right };
            AcceptButton = _ok; CancelButton = cancel;

            Controls.AddRange(new WinForms.Control[] { top, fl, _filter, _list, all, none, box, _ok, cancel });
            Fill();
        }

        bool _filling;
        void Fill()
        {
            _filling = true;
            _list.BeginUpdate(); _list.Items.Clear();
            string f = _filter.Text.Trim();
            foreach (var l in _lines)
            {
                if (f.Length > 0 && l.Tag.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                var it = new WinForms.ListViewItem(l.Tag) { Tag = l.Tag, Checked = _checked.Contains(l.Tag) };
                it.SubItems.Add(l.Sizes); it.SubItems.Add(l.PipeCount.ToString(CultureInfo.InvariantCulture));
                it.SubItems.Add((l.LengthMm / 1000).ToString("0.0", CultureInfo.CurrentCulture));
                _list.Items.Add(it);
            }
            _list.EndUpdate(); _filling = false;
            UpdateButton();
        }

        void SetAll(bool on)
        {
            foreach (WinForms.ListViewItem it in _list.Items) it.Checked = on;
        }

        void UpdateButton()
        {
            int n = _checked.Count;
            _ok.Enabled = n > 0;
            _ok.Text = n > 0 ? $"Рассчитать ({n})" : "Рассчитать";
        }

        static string Num(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);

        bool ReadParameters()
        {
            bool Parse(string s, double min, double max, out double v)
            {
                s = s.Trim().Replace(',', '.');
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= min && v <= max;
            }
            if (string.IsNullOrWhiteSpace(_spec.Text)) { WinForms.MessageBox.Show("Выберите миникаталог (спецификацию) трубы спутника.", Text); return false; }
            if (!Parse(_gap.Text, 0, 50, out var gap)) { WinForms.MessageBox.Show("Зазор: число от 0 до 50 мм.", Text); return false; }
            if (!Parse(_radius.Text, 20, 500, out var r)) { WinForms.MessageBox.Show("Радиус изгиба: число от 20 до 500 мм.", Text); return false; }
            if (!Parse(_tol.Text, 0, 50, out var minGap) || minGap > gap) { WinForms.MessageBox.Show("Наименьший зазор: число от 0 мм до величины зазора (" + Num(gap) + " мм).", Text); return false; }
            _par.Spec = _spec.Text.Trim(); _par.Gap = gap; _par.Radius = r; _par.MinGap = minGap;
            return true;
        }
    }

    /// <summary>A report with a question: the plan of the tracing (nothing created yet) or the result.</summary>
    internal sealed class TracingReportDialog : WinForms.Form
    {
        public TracingReportDialog(string title, string text, string okText, bool cancelButton)
        {
            Text = title;
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            Size = new Size(760, 520); MinimumSize = new Size(520, 360);
            Font = new Font("Segoe UI", 9f);
            var box = new WinForms.TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = WinForms.ScrollBars.Both, WordWrap = false,
                Left = 12, Top = 12, Width = 720, Height = 420, Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                Font = new Font("Consolas", 9f),
                Anchor = WinForms.AnchorStyles.Top | WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Left | WinForms.AnchorStyles.Right,
            };
            var ok = new WinForms.Button { Text = okText, Width = 150, Height = 30, Left = cancelButton ? 466 : 582, Top = 444, DialogResult = WinForms.DialogResult.OK, Anchor = WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Right };
            Controls.Add(box); Controls.Add(ok); AcceptButton = ok;
            if (cancelButton)
            {
                var cancel = new WinForms.Button { Text = "Отмена", Width = 100, Height = 30, Left = 632, Top = 444, DialogResult = WinForms.DialogResult.Cancel, Anchor = WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Right };
                Controls.Add(cancel); CancelButton = cancel;
            }
            box.Select(0, 0);
        }
    }
}
