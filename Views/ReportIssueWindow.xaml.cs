using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using POTimeTracker.Services;

namespace POTimeTracker.Views
{
    public partial class ReportIssueWindow : Window
    {
        private const double BaseWidth = 420;

        // Where reported problems/improvements land in Jira.
        private const string ReportProjectKey  = "POT";
        private const string ReportIssueType   = "Tarea";

        private readonly JiraApiService _jira = new();
        private bool _isProblem = true;
        private bool _sending;

        private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(52, 211, 153));
        private static readonly SolidColorBrush RedBrush   = new(Color.FromRgb(248, 113, 113));

        public ReportIssueWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            DragHeader.PreviewMouseLeftButtonDown += (s, ev) =>
            {
                if (!IsInteractiveSource(ev.OriginalSource))
                    try { DragMove(); } catch { }
            };

            ApplyUiScale();
            UiScaleService.ScaleChanged += OnUiScaleChanged;

            PositionAboveTray();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            UiScaleService.ScaleChanged -= OnUiScaleChanged;
        }

        private void OnUiScaleChanged(double scale)
        {
            Dispatcher.Invoke(() =>
            {
                ApplyUiScale();
                PositionAboveTray();
            });
        }

        private void ApplyUiScale()
        {
            var scale = UiScaleService.Current;
            Width = BaseWidth * scale;
            RootBorder.LayoutTransform = new ScaleTransform(scale, scale);
        }

        private void PositionAboveTray()
        {
            UpdateLayout();
            var wa = SystemParameters.WorkArea;
            MaxHeight = wa.Height - 20;
            var h = ActualHeight > 50 ? ActualHeight : Math.Min(600, wa.Height - 20);
            Left = Math.Max(wa.Left, wa.Right - Width - 10);
            Top  = Math.Max(wa.Top,  wa.Bottom - h - 10);
        }

        private static bool IsInteractiveSource(object src)
        {
            if (src is not DependencyObject dep) return false;
            var el = dep as FrameworkElement;
            while (el != null)
            {
                if (el is Button or TextBox or PasswordBox or CheckBox or
                    RadioButton or ComboBox or Slider or
                    ListBox or ListBoxItem or ToggleButton) return true;
                el = VisualTreeHelper.GetParent(el) as FrameworkElement;
                if (el is Window) break;
            }
            return false;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnTypeProblem_Click(object sender, RoutedEventArgs e) => SetType(isProblem: true);

        private void BtnTypeMejora_Click(object sender, RoutedEventArgs e) => SetType(isProblem: false);

        private void SetType(bool isProblem)
        {
            _isProblem = isProblem;
            btnTypeProblem.Style = (Style)FindResource(isProblem ? "AccentButton" : "GhostButton");
            btnTypeMejora.Style  = (Style)FindResource(isProblem ? "GhostButton" : "AccentButton");
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            if (_sending) return;

            var description = txtDescription.Text.Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                ShowStatus("Escribe una descripcion", true);
                return;
            }

            var (config, token) = JiraConfigService.LoadConfig();
            bool configured = config != null
                && !string.IsNullOrWhiteSpace(config.BaseUrl)
                && !string.IsNullOrWhiteSpace(token);

            if (!configured)
            {
                ShowStatus("Configura Jira primero (abri la ventana de Jira)", true);
                return;
            }

            _sending = true;
            btnSend.IsEnabled = false;
            btnSend.Content = "Enviando...";
            HideStatus();

            try
            {
                _jira.Configure(config!.BaseUrl, config.Email, token);

                var typeLabel = _isProblem ? "Problema" : "Mejora";
                var firstLine = description.Split('\n')[0].Trim();
                var summary = $"[{typeLabel}] {(firstLine.Length > 80 ? firstLine[..80] : firstLine)}";

                var (success, message, issueKey) = await _jira.CreateIssueAsync(
                    ReportProjectKey, summary, description, ReportIssueType);

                if (success)
                {
                    ShowStatus($"Reportado en Jira: {issueKey}", false);
                    btnSend.Content = "Enviado ✓";
                    await System.Threading.Tasks.Task.Delay(1200);
                    Close();
                }
                else
                {
                    ShowStatus(message, true);
                    btnSend.IsEnabled = true;
                    btnSend.Content = "Enviar";
                }
            }
            catch (Exception ex)
            {
                LogService.Error("ReportIssueWindow.BtnSend_Click: error inesperado", ex);
                ShowStatus("Error inesperado al enviar. Revisa los logs.", true);
                btnSend.IsEnabled = true;
                btnSend.Content = "Enviar";
            }
            finally
            {
                _sending = false;
            }
        }

        private void ShowStatus(string msg, bool isError)
        {
            txtStatus.Text = msg;
            txtStatus.Foreground = isError ? RedBrush : GreenBrush;
            txtStatus.Visibility = Visibility.Visible;
            _ = Dispatcher.BeginInvoke(PositionAboveTray, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void HideStatus() => txtStatus.Visibility = Visibility.Collapsed;
    }
}
