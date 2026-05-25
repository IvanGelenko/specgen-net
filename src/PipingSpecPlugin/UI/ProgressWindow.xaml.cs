using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace PipingSpecPlugin.UI
{
    /// <summary>
    /// Modeless progress popup we show while CollectFromProject churns
    /// through every drawing. The scan loop runs on the UI thread (it
    /// has to — AutoCAD's Database API is single-threaded), so we use
    /// Dispatcher.Invoke + Application.DoEvents-style pumping to keep
    /// the bar animating instead of starting a worker thread.
    ///
    /// Cancellation: clicking the X or the Cancel button flips a
    /// CancellationToken the caller polls each iteration. Closes
    /// automatically when the caller calls Complete().
    /// </summary>
    public partial class ProgressWindow : Window
    {
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        bool _completing;

        public CancellationToken Token => _cts.Token;
        public bool IsCancelled => _cts.IsCancellationRequested;

        public ProgressWindow()
        {
            InitializeComponent();
            Closing += (_, e) =>
            {
                if (!_completing) _cts.Cancel();
            };
        }

        public void SetHeader(string text)
        {
            Dispatcher.Invoke(() => HeaderText.Text = text, DispatcherPriority.Send);
        }

        /// <summary>
        /// Update the bar and the under-bar text in one go. Safe to call
        /// from the same thread that's running the scan — Dispatcher.Invoke
        /// flushes input events so the bar paints between long DataLinks
        /// reads.
        /// </summary>
        public void Report(int current, int total, string detail)
        {
            Dispatcher.Invoke(() =>
            {
                if (total > 0)
                {
                    Bar.IsIndeterminate = false;
                    Bar.Value = Math.Min(100, (current * 100.0) / total);
                    CounterText.Text = $"{current} из {total}";
                }
                else
                {
                    Bar.IsIndeterminate = true;
                    CounterText.Text = "";
                }
                DetailText.Text = detail ?? "";
            }, DispatcherPriority.Render);
        }

        public void Indeterminate(string detail)
        {
            Dispatcher.Invoke(() =>
            {
                Bar.IsIndeterminate = true;
                CounterText.Text = "";
                DetailText.Text = detail ?? "";
            }, DispatcherPriority.Render);
        }

        public void Complete()
        {
            _completing = true;
            Dispatcher.Invoke(Close);
        }

        void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts.Cancel();
            CancelButton.IsEnabled = false;
            DetailText.Text = "Отмена…";
        }
    }
}
