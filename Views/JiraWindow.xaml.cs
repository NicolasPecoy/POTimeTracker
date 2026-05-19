using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using POTimeTracker.Models;
using POTimeTracker.Services;

namespace POTimeTracker.Views
{
    public partial class JiraWindow : Window
    {
        private readonly JiraApiService _jira = new();
        private List<JiraProject> _projects = new();
        private List<JiraIssue>   _issues   = new();
        private JiraIssue?        _selectedIssue;

        /// <summary>Fired when the window hides itself (minimize button or deactivation).</summary>
        public event EventHandler? WindowHidden;

        private static readonly SolidColorBrush AccentBrush   = new(Color.FromRgb(99, 102, 241));
        private static readonly SolidColorBrush GreenBrush    = new(Color.FromRgb(52, 211, 153));
        private static readonly SolidColorBrush AmberBrush    = new(Color.FromRgb(251, 191, 36));
        private static readonly SolidColorBrush RedBrush      = new(Color.FromRgb(248, 113, 113));
        private static readonly SolidColorBrush TextPrimary   = new(Color.FromRgb(232, 234, 240));
        private static readonly SolidColorBrush TextSecondary = new(Color.FromRgb(139, 143, 163));
        private static readonly SolidColorBrush TextMuted     = new(Color.FromRgb(92, 96, 120));
        private static readonly SolidColorBrush BgHover       = new(Color.FromRgb(35, 39, 56));
        private static readonly SolidColorBrush ActiveBg      = new(Color.FromArgb(25, 99, 102, 241));
        private static readonly SolidColorBrush Transparent   = Brushes.Transparent;
        private static readonly SolidColorBrush JiraBlue      = new(Color.FromRgb(38, 132, 255));

        public JiraWindow()
        {
            InitializeComponent();
        }

        // ══════════════════════════════════════════════════
        // INIT
        // ══════════════════════════════════════════════════

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            dtPicker.SelectedDate = DateTime.Today;
            PositionAboveTray();

            var (config, token) = JiraConfigService.LoadConfig();

            // Always pre-fill the config form fields from saved data
            if (config != null)
            {
                txtBaseUrl.Text        = !string.IsNullOrEmpty(config.BaseUrl) ? config.BaseUrl : "https://";
                txtEmail.Text          = config.Email;
                txtDefaultProject.Text = config.DefaultProjectKey;
            }
            if (!string.IsNullOrEmpty(token))
                txtApiToken.Password = token;

            // Auto-connect if fully configured
            bool fullyConfigured = config != null
                && !string.IsNullOrWhiteSpace(config.BaseUrl)
                && !string.IsNullOrWhiteSpace(config.Email)
                && !string.IsNullOrWhiteSpace(token);

            if (fullyConfigured)
            {
                _jira.Configure(config!.BaseUrl, config.Email, token);
                ShowConfigLoading(true);
                var (ok, user, _) = await _jira.TestConnectionAsync();
                ShowConfigLoading(false);

                if (ok)
                {
                    await SwitchToMainViewAsync(config, user);
                    return;
                }
            }

            ConfigView.Visibility = Visibility.Visible;
            MainView.Visibility   = Visibility.Collapsed;
        }

        private void PositionAboveTray()
        {
            UpdateLayout();
            var wa = SystemParameters.WorkArea;
            double h = ActualHeight > 50 ? ActualHeight : 750;
            Left = Math.Max(wa.Left, wa.Right - Width - 10);
            Top  = Math.Max(wa.Top,  wa.Bottom - h - 10);
        }

        private async System.Threading.Tasks.Task SwitchToMainViewAsync(JiraConfig config, string user)
        {
            ConfigView.Visibility = Visibility.Collapsed;
            MainView.Visibility   = Visibility.Visible;

            txtUserDisplay.Text    = user;
            txtBaseUrlDisplay.Text = config.BaseUrl.Replace("https://", "").Replace("http://", "");

            ShowIssuesLoading(true);
            _projects = await _jira.GetProjectsAsync();

            var projectItems = new List<JiraProject>
            {
                new() { Key = "Todos", Name = "Todos los proyectos", Id = "" }
            };
            projectItems.AddRange(_projects);
            cboProject.ItemsSource   = projectItems;
            cboProject.SelectedIndex = 0;

            if (!string.IsNullOrWhiteSpace(config.DefaultProjectKey))
            {
                var match = projectItems.FirstOrDefault(p =>
                    string.Equals(p.Key, config.DefaultProjectKey, StringComparison.OrdinalIgnoreCase));
                if (match != null) cboProject.SelectedItem = match;
            }

            await LoadIssuesAsync();
        }

        private async System.Threading.Tasks.Task LoadIssuesAsync()
        {
            ShowIssuesLoading(true);
            IssuesPanel.Children.Clear();
            _selectedIssue             = null;
            IssuDetailPanel.Visibility = Visibility.Collapsed;

            var selectedProject = cboProject.SelectedItem as JiraProject;
            var projectKey      = selectedProject?.Id == "" ? "" : selectedProject?.Key ?? "";

            _issues = await _jira.GetMyIssuesAsync(projectKey);
            BuildIssuesList();
            ShowIssuesLoading(false);
        }

        // ══════════════════════════════════════════════════
        // CONNECT (CONFIG VIEW)
        // ══════════════════════════════════════════════════

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            var baseUrl = txtBaseUrl.Text.Trim().TrimEnd('/');
            var email   = txtEmail.Text.Trim();
            var token   = txtApiToken.Password.Trim();   // trim whitespace/newlines from copy-paste
            var projKey = txtDefaultProject.Text.Trim();

            if (string.IsNullOrWhiteSpace(baseUrl) || !baseUrl.StartsWith("http"))
            {
                ShowConfigError("Ingresa una URL valida (ej: https://empresa.atlassian.net)");
                return;
            }
            if (string.IsNullOrWhiteSpace(email))
            {
                ShowConfigError("Ingresa tu email de Atlassian");
                return;
            }
            if (string.IsNullOrWhiteSpace(token))
            {
                ShowConfigError("Ingresa tu API Token de Atlassian");
                return;
            }
            if (IsDoubleEncodedToken(token))
            {
                ShowConfigError(
                    "El token parece estar en formato incorrecto (Base64 pre-codificado).\n\n" +
                    "El API Token debe copiarse directamente desde:\n" +
                    "id.atlassian.com → Seguridad → API tokens → Crear token\n\n" +
                    "El token debe verse como: ATATTxxx... o similar (sin espacios ni '==' al inicio).");
                return;
            }

            btnConnect.IsEnabled = false;
            ShowConfigLoading(true);
            HideConfigError();

            _jira.Configure(baseUrl, email, token);
            var (ok, user, message) = await _jira.TestConnectionAsync();

            if (ok)
            {
                var config = new JiraConfig
                {
                    BaseUrl           = baseUrl,
                    Email             = email,
                    DefaultProjectKey = projKey,
                    Enabled           = true
                };
                JiraConfigService.SaveConfig(config, token);
                await SwitchToMainViewAsync(config, user);
            }
            else
            {
                var hint = message.Contains("401")
                    ? $"{message}\n\nVerifica: el email debe ser el de tu cuenta Atlassian y el API Token debe generarse en id.atlassian.com → Seguridad → API tokens."
                    : message;
                ShowConfigError(hint);
                ShowConfigLoading(false);
                btnConnect.IsEnabled = true;
            }
        }

        // ══════════════════════════════════════════════════
        // ISSUES LIST
        // ══════════════════════════════════════════════════

        private void BuildIssuesList()
        {
            IssuesPanel.Children.Clear();
            txtIssueCount.Text        = _issues.Count.ToString();
            txtIssuesEmpty.Visibility = _issues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var issue in _issues)
                IssuesPanel.Children.Add(BuildIssueItem(issue));
        }

        private Border BuildIssueItem(JiraIssue issue)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var bar = new Border
            {
                CornerRadius      = new CornerRadius(2),
                Width             = 3,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background        = GetStatusBrush(issue.StatusCategory)
            };
            Grid.SetColumn(bar, 0);
            grid.Children.Add(bar);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text         = issue.Key,
                FontSize     = 11,
                FontWeight   = FontWeights.SemiBold,
                FontFamily   = new FontFamily("Consolas"),
                Foreground   = JiraBlue,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            info.Children.Add(new TextBlock
            {
                Text         = issue.Summary,
                FontSize     = 12,
                Foreground   = TextPrimary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin       = new Thickness(0, 1, 0, 0)
            });
            Grid.SetColumn(info, 2);
            grid.Children.Add(info);

            var badge = new Border
            {
                CornerRadius      = new CornerRadius(4),
                Padding           = new Thickness(5, 2, 5, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(6, 0, 0, 0),
                Background        = GetStatusSoftBrush(issue.StatusCategory)
            };
            badge.Child = new TextBlock
            {
                Text       = issue.Status,
                FontSize   = 10,
                Foreground = GetStatusBrush(issue.StatusCategory)
            };
            Grid.SetColumn(badge, 3);
            grid.Children.Add(badge);

            var border = new Border
            {
                Padding         = new Thickness(18, 8, 18, 8),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(25, 42, 45, 62)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background      = Transparent,
                Cursor          = Cursors.Hand,
                Child           = grid
            };

            var captured = issue;
            border.MouseLeftButtonDown += (s, _) => SelectIssue(captured, (Border)s!);
            border.MouseEnter  += (s, _) => { if (_selectedIssue?.Key != captured.Key) ((Border)s!).Background = BgHover; };
            border.MouseLeave  += (s, _) => { if (_selectedIssue?.Key != captured.Key) ((Border)s!).Background = Transparent; };

            return border;
        }

        private void SelectIssue(JiraIssue issue, Border clickedBorder)
        {
            foreach (Border b in IssuesPanel.Children.OfType<Border>())
                b.Background = Transparent;

            clickedBorder.Background = ActiveBg;
            _selectedIssue           = issue;

            txtSelectedKey.Text          = issue.Key;
            txtSelectedSummary.Text      = issue.Summary;
            txtSelectedStatus.Text       = issue.Status;
            StatusBadge.Background       = GetStatusSoftBrush(issue.StatusCategory);
            txtSelectedStatus.Foreground = GetStatusBrush(issue.StatusCategory);

            IssuDetailPanel.Visibility = Visibility.Visible;
        }

        // ══════════════════════════════════════════════════
        // SEARCH
        // ══════════════════════════════════════════════════

        private void TxtSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtSearch.Text == "Buscar issue...") txtSearch.Text = "";
        }

        private void TxtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtSearch.Text)) txtSearch.Text = "Buscar issue...";
        }

        private async void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var query = txtSearch.Text.Trim();
            if (string.IsNullOrWhiteSpace(query) || query == "Buscar issue...") return;

            ShowIssuesLoading(true);
            IssuesPanel.Children.Clear();
            _selectedIssue             = null;
            IssuDetailPanel.Visibility = Visibility.Collapsed;

            if (System.Text.RegularExpressions.Regex.IsMatch(query, @"^[A-Za-z]+-\d+$"))
            {
                var issue = await _jira.GetIssueAsync(query);
                _issues = issue != null ? new List<JiraIssue> { issue } : new();
            }
            else
            {
                var jql = $"text ~ \"{query}\" AND assignee = currentUser() ORDER BY updated DESC";
                _issues = await _jira.SearchIssuesAsync(jql, 30);
            }

            BuildIssuesList();
            ShowIssuesLoading(false);
        }

        // ══════════════════════════════════════════════════
        // SUBMIT WORKLOG
        // ══════════════════════════════════════════════════

        private async void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedIssue == null)
            {
                ShowStatusMessage("Selecciona un issue de la lista", true);
                return;
            }
            if (!TryParseHours(txtHours.Text, out var hours) || hours <= 0)
            {
                ShowStatusMessage("Ingresa las horas", true);
                return;
            }

            var date = dtPicker.SelectedDate ?? DateTime.Today;

            btnSubmit.IsEnabled = false;
            btnSubmit.Content   = "Enviando...";

            var (success, message) = await _jira.LogWorkAsync(
                _selectedIssue.Key, hours, date, txtNotes.Text.Trim());

            if (success)
            {
                btnSubmit.Content    = "Registrado!";
                btnSubmit.Background = GreenBrush;
                ShowStatusMessage($"{hours:0.0}h en {_selectedIssue.Key}", false);
            }
            else
            {
                btnSubmit.Content    = "Error";
                btnSubmit.Background = RedBrush;
                ShowStatusMessage(message, true);
            }

            await System.Threading.Tasks.Task.Delay(1800);
            btnSubmit.Content    = "Registrar en Jira";
            btnSubmit.Background = AccentBrush;
            btnSubmit.IsEnabled  = true;
            txtNotes.Text        = "";
            txtHours.Text        = "1.0";
        }

        // ══════════════════════════════════════════════════
        // HOURS INPUT
        // ══════════════════════════════════════════════════

        private void BtnHoursMinus_Click(object sender, RoutedEventArgs e) => AdjustHours(-0.5);
        private void BtnHoursPlus_Click(object sender, RoutedEventArgs e)  => AdjustHours(0.5);

        private void AdjustHours(double delta)
        {
            if (TryParseHours(txtHours.Text, out var h))
            {
                h = Math.Max(0, Math.Round((h + delta) * 10) / 10);
                txtHours.Text = h.ToString("0.0", CultureInfo.CurrentCulture);
            }
        }

        private void QuickHour_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
                txtHours.Text = double.Parse(btn.Tag.ToString()!, CultureInfo.InvariantCulture)
                                      .ToString("0.0", CultureInfo.CurrentCulture);
        }

        private void BtnDateToday_Click(object sender, RoutedEventArgs e) =>
            dtPicker.SelectedDate = DateTime.Today;

        // ══════════════════════════════════════════════════
        // NAVIGATION
        // ══════════════════════════════════════════════════

        private async void CboProject_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainView.Visibility != Visibility.Visible) return;
            await LoadIssuesAsync();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            txtSearch.Text = "Buscar issue...";
            await LoadIssuesAsync();
        }

        private void ChkProxy_Changed(object sender, RoutedEventArgs e)
        {
            if (chkProxy.IsChecked == true)
                _jira.EnableProxy("http://localhost:8888");
            else
                _jira.DisableProxy();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            _jira.Disconnect();
            ConfigView.Visibility = Visibility.Visible;
            MainView.Visibility   = Visibility.Collapsed;

            var (config, token) = JiraConfigService.LoadConfig();
            if (config != null)
            {
                txtBaseUrl.Text        = config.BaseUrl;
                txtEmail.Text          = config.Email;
                txtDefaultProject.Text = config.DefaultProjectKey;
            }
            if (!string.IsNullOrEmpty(token))
                txtApiToken.Password = token;

            // Reflect current proxy state in checkbox
            chkProxy.IsChecked = _jira.ProxyEnabled;
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            WindowHidden?.Invoke(this, EventArgs.Empty);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (!IsVisible) return;
            Hide();
            WindowHidden?.Invoke(this, EventArgs.Empty);
        }

        // ══════════════════════════════════════════════════
        // UI HELPERS
        // ══════════════════════════════════════════════════

        private void ShowConfigError(string msg)
        {
            txtConfigError.Text       = msg;
            txtConfigError.Visibility = Visibility.Visible;
        }
        private void HideConfigError() => txtConfigError.Visibility = Visibility.Collapsed;
        private void ShowConfigLoading(bool show) =>
            ConfigLoading.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        private void ShowIssuesLoading(bool show)
        {
            IssuesLoading.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) txtIssuesEmpty.Visibility = Visibility.Collapsed;
        }

        private async void ShowStatusMessage(string msg, bool isError)
        {
            txtStatus.Text       = msg;
            txtStatus.Foreground = isError ? RedBrush : GreenBrush;
            txtStatus.Visibility = Visibility.Visible;
            await System.Threading.Tasks.Task.Delay(3500);
            if (IsLoaded) txtStatus.Visibility = Visibility.Collapsed;
        }

        private static bool TryParseHours(string text, out double hours)
        {
            return double.TryParse(text.Trim().Replace(',', '.'),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out hours);
        }

        // Detects if the user pasted already-base64-encoded credentials (email:token)
        // instead of the raw API token. This causes double-encoding and a 401 from Jira.
        private static bool IsDoubleEncodedToken(string token)
        {
            // Strip "Basic " prefix if accidentally included
            var raw = token.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
                ? token[6..].Trim()
                : token;

            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(raw));
                // If decoding yields something with an @ followed by a colon it's credentials, not a token
                var atIdx = decoded.IndexOf('@');
                return atIdx > 0 && decoded.IndexOf(':', atIdx) > atIdx;
            }
            catch
            {
                return false;
            }
        }

        private static SolidColorBrush GetStatusBrush(string category) => category switch
        {
            "done"          => GreenBrush,
            "indeterminate" => AmberBrush,
            _               => new SolidColorBrush(Color.FromRgb(38, 132, 255))
        };

        private static SolidColorBrush GetStatusSoftBrush(string category) => category switch
        {
            "done"          => new SolidColorBrush(Color.FromArgb(40, 52, 211, 153)),
            "indeterminate" => new SolidColorBrush(Color.FromArgb(40, 251, 191, 36)),
            _               => new SolidColorBrush(Color.FromArgb(40, 38, 132, 255))
        };
    }
}
