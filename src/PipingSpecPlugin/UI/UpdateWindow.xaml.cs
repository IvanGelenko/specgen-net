using System;
using System.Windows;
using PipingSpecPlugin.Update;

namespace PipingSpecPlugin.UI
{
    /// <summary>
    /// Modal notification shown when a newer plug-in release is found on
    /// GitHub. "Обновить" downloads the installer and launches it; because
    /// AutoCAD holds the loaded DLL locked for the session, the install
    /// only completes after AutoCAD is closed — the window says so.
    /// </summary>
    public partial class UpdateWindow : Window
    {
        readonly UpdateInfo _info;
        System.Threading.CancellationTokenSource _downloadCancellation;

        public UpdateWindow(UpdateInfo info)
        {
            InitializeComponent();
            _info = info;

            VersionText.Text =
                $"Текущая версия: {info.CurrentVersion}    →    Новая: {info.LatestVersion}";
            NotesText.Text = string.IsNullOrWhiteSpace(info.ReleaseNotes)
                ? "Описание изменений недоступно."
                : info.ReleaseNotes.Trim();

            // No installer asset on the release ⇒ can't auto-download;
            // steer the user to the release page instead.
            if (string.IsNullOrWhiteSpace(info.InstallerUrl))
            {
                UpdateButton.IsEnabled = false;
                StatusText.Text = "Автоматическая загрузка недоступна — откройте страницу релиза.";
            }
        }

        async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateButton.IsEnabled = false;
            LaterButton.IsEnabled = true;
            using var cancellation=_downloadCancellation=new System.Threading.CancellationTokenSource();
            StatusText.Text = "Загрузка установщика…";
            try
            {
                var progress=new Progress<int>(value=>StatusText.Text=value>=0?$"Загрузка установщика: {value}%":"Загрузка установщика…");
                bool ok = await UpdateService.DownloadAndLaunchInstallerAsync(_info,progress,cancellation.Token);
                if (ok)
                {
                    MessageBox.Show(
                        "Установщик запущен.\n\nЗакройте AutoCAD, чтобы завершить "
                        + "обновление — плагин нельзя заменить, пока AutoCAD открыт.",
                        "Обновление плагина",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    StatusText.Text = "Не удалось загрузить установщик. Повторите или откройте страницу релиза вручную.";
                    LaterButton.IsEnabled = true;
                    // Allow a retry (a flaky connection is the usual cause);
                    // the button used to stay disabled for good.
                    UpdateButton.IsEnabled = true;
                }
            }
            catch (OperationCanceledException) { StatusText.Text="Загрузка отменена."; }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки: " + ex.Message;
                LaterButton.IsEnabled = true;
                UpdateButton.IsEnabled = true;
            }
            finally{_downloadCancellation=null;LaterButton.IsEnabled=true;if(IsVisible)UpdateButton.IsEnabled=true;}
        }

        protected override void OnClosed(EventArgs e){_downloadCancellation?.Cancel();base.OnClosed(e);}

        void ReleasePageButton_Click(object sender, RoutedEventArgs e)
        {
            var url = string.IsNullOrWhiteSpace(_info.ReleaseUrl)
                ? "https://github.com/IvanGelenko/specgen-net/releases/latest"
                : _info.ReleaseUrl;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            catch { /* nothing we can do if the shell refuses */ }
        }

        void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
