using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using POTimeTracker.Models;
using POTimeTracker.Services;

namespace POTimeTracker.Views
{
    public partial class MainWindow : Window
    {
        private readonly POApiService   _api  = new();
        private readonly JiraApiService _jira = new();
        private JiraWindow? _jiraWindow;

        private List<JiraIssue>         _jiraAllIssues      = new();
        private Dictionary<string, double> _jiraSelectedIssues = new();

        private DateTime _currentDate = DateTime.Today;
        private DateTime _projectsLoadedForDate = DateTime.MinValue;
        private double _weeklyTarget = 40;
        private List<POProject> _projects = new();
        private readonly Dictionary<string, List<TimeEntry>> _entries = new();
        private TaskbarIcon? _notifyIcon;
        private bool _suppressAutoHide;
        private DispatcherTimer? _reminderTimer;
        private DispatcherTimer? _reloginTimer;
        private DateTime? _lastReminderDate;
        private ReminderWindow? _reminderWindow;

        // Settings (loaded from config)
        private int _reminderHour = 17;
        private int _reminderMinute = 15;
        private bool _reminderOnSaturday = false;
        private bool _reminderOnSunday = false;
        private double _reloginIntervalHours = 3.0;

        private static readonly string[] DayNames = { "DOMINGO","LUNES","MARTES","MIERCOLES","JUEVES","VIERNES","SABADO" };
        private static readonly string[] MonthNames = { "Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre" };
        private static readonly string[] DayShort = { "Dom","Lun","Mar","Mie","Jue","Vie","Sab" };

        // Brushes (cached for performance)
        private static readonly SolidColorBrush AccentBrushCached = new(Color.FromRgb(99, 102, 241));
        private static readonly SolidColorBrush GreenBrushCached = new(Color.FromRgb(52, 211, 153));
        private static readonly SolidColorBrush TextPrimaryBrushCached = new(Color.FromRgb(232, 234, 240));
        private static readonly SolidColorBrush TextSecondaryBrushCached = new(Color.FromRgb(139, 143, 163));
        private static readonly SolidColorBrush TextMutedBrushCached = new(Color.FromRgb(92, 96, 120));
        private static readonly SolidColorBrush BgHoverBrushCached = new(Color.FromRgb(35, 39, 56));
        private static readonly SolidColorBrush ActiveBgBrushCached = new(Color.FromArgb(25, 99, 102, 241));
        private static readonly SolidColorBrush RedBrushCached = new(Color.FromRgb(248, 113, 113));
        private static readonly SolidColorBrush AmberBrushCached = new(Color.FromRgb(251, 191, 36));
        private static readonly SolidColorBrush TransparentBrush = Brushes.Transparent;

        public MainWindow()
        {
            InitializeComponent();
        }

        // ══════════════════════════════════════════════════
        // INITIALIZATION
        // ══════════════════════════════════════════════════

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _notifyIcon = (TaskbarIcon)FindResource("NotifyIcon");
            ApplyMenuIcons();
            EnsureRunAtStartup();
            LoadSettings();
            LoadPersistedEntries();
            StartDailyReminderTimer();
            PositionAboveTray();

            InitJira();

            var creds = CredentialService.LoadCredentials();
            if (creds != null && !string.IsNullOrEmpty(creds.Username))
            {
                txtServer.Text = creds.ServerUrl;
                txtUsername.Text = creds.Username;
                txtPassword.Password = creds.Password;
                _weeklyTarget = creds.WeeklyTarget;

                ShowLoginLoading(true);
                var (success, _) = await _api.LoginAsync(creds.ServerUrl, creds.Username, creds.Password);
                if (success)
                {
                    StartReloginTimer();
                    await SwitchToMainViewAsync();
                    if (IsBackgroundStartup())
                        Hide();
                    return;
                }
                ShowLoginLoading(false);
                ShowLoginError("Sesion expirada. Volve a iniciar sesion.");
            }

            LoginView.Visibility = Visibility.Visible;
            MainView.Visibility = Visibility.Collapsed;
            Show();
            Activate();
            PlayFadeIn();
        }

        private void LoadPersistedEntries()
        {
            var saved = CredentialService.LoadEntries();
            foreach (var kv in saved)
                _entries[kv.Key] = kv.Value;
        }

        private void LoadSettings()
        {
            var config = CredentialService.LoadConfig();
            if (config == null) return;
            _reminderHour = config.ReminderHour;
            _reminderMinute = config.ReminderMinute;
            _reminderOnSaturday = config.ReminderOnSaturday;
            _reminderOnSunday = config.ReminderOnSunday;
            _reloginIntervalHours = config.ReloginIntervalHours > 0 ? config.ReloginIntervalHours : 3.0;
            if (config.WeeklyTarget > 0) _weeklyTarget = config.WeeklyTarget;
        }

        private void PositionAboveTray()
        {
            var wa = SystemParameters.WorkArea;
            var height = ActualHeight > 50 ? ActualHeight : 750;
            Left = Math.Max(wa.Left, wa.Right - Width - 10);
            Top = Math.Max(wa.Top, wa.Bottom - height - 10);
        }

        private void PlayFadeIn()
        {
            var sb = (Storyboard)FindResource("FadeIn");
            sb.Begin(this);
        }

        // ══════════════════════════════════════════════════
        // LOGIN
        // ══════════════════════════════════════════════════

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            var server = txtServer.Text.Trim();
            var username = txtUsername.Text.Trim();
            var password = txtPassword.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowLoginError("Ingresa usuario y contrasena");
                return;
            }

            ShowLoginLoading(true);
            HideLoginError();
            btnLogin.IsEnabled = false;

            var (success, message) = await _api.LoginAsync(server, username, password);

            if (success)
            {
                if (chkRemember.IsChecked == true)
                {
                    CredentialService.SaveCredentials(username, password, server);
                    CredentialService.SaveConfig(new LoginCredentials { WeeklyTarget = _weeklyTarget, ServerUrl = server });
                }
                StartReloginTimer();
                await SwitchToMainViewAsync();
            }
            else
            {
                ShowLoginError(message);
                ShowLoginLoading(false);
                btnLogin.IsEnabled = true;
            }
        }

        private async System.Threading.Tasks.Task SwitchToMainViewAsync()
        {
            _suppressAutoHide = true;
            try
            {
                ShowLoginLoading(false);
                LoginView.Visibility = Visibility.Collapsed;
                MainView.Visibility = Visibility.Visible;

                txtUserDisplay.Text = _api.CurrentUser ?? "";
                txtServerDisplay.Text = _api.ServerUrl.Replace("http://", "").Replace("https://", "");
                txtTargetHours.Text = _weeklyTarget.ToString("0.0");

                InitJira();
                await ReloadProjectsAsync();
                RefreshAll();
                UpdateLayout();
                PositionAboveTray();
                Show();
                Activate();

                if (_notifyIcon != null)
                    _notifyIcon.ToolTipText = $"PO Time Tracker - {_api.CurrentUser}";

                PlayFadeIn();
            }
            finally
            {
                await System.Threading.Tasks.Task.Delay(500);
                _suppressAutoHide = false;
            }
        }

        private void ShowLoginError(string msg)
        {
            txtLoginError.Text = msg;
            txtLoginError.Visibility = Visibility.Visible;
        }
        private void HideLoginError() => txtLoginError.Visibility = Visibility.Collapsed;
        private void ShowLoginLoading(bool show) =>
            LoginLoading.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        private async System.Threading.Tasks.Task ReloadProjectsAsync()
        {
            var selectedProjectId = (cboProject.SelectedItem as POProject)?.Id;
            var showAllTasks = chkShowAllTasks.IsChecked == true;

            _projects = await _api.GetProjectsAsync(_currentDate, showAllTasks);
            _projectsLoadedForDate = _currentDate;

            // If empty and we think we're logged in, the session likely expired on the server.
            // Try to re-authenticate silently and retry once.
            if (_projects.Count == 0 && _api.IsLoggedIn)
            {
                var creds = CredentialService.LoadCredentials();
                if (creds != null && !string.IsNullOrEmpty(creds.Username))
                {
                    var (success, _) = await _api.LoginAsync(creds.ServerUrl, creds.Username, creds.Password);
                    if (success)
                    {
                        _projects = await _api.GetProjectsAsync(_currentDate, showAllTasks);
                    }
                    else
                    {
                        _api.Logout();
                        MainView.Visibility = Visibility.Collapsed;
                        LoginView.Visibility = Visibility.Visible;
                        Show(); PositionAboveTray(); Activate();
                        ShowLoginError("Sesion expirada. Volve a iniciar sesion.");
                        return;
                    }
                }
            }

            cboProject.ItemsSource = _projects;

            var selected = _projects.FirstOrDefault(p => p.Id == selectedProjectId) ?? _projects.FirstOrDefault();
            if (selected != null)
                cboProject.SelectedItem = selected;
            else
                cboTask.ItemsSource = null;
        }

        // ══════════════════════════════════════════════════
        // DATE NAVIGATION
        // ══════════════════════════════════════════════════

        private void UpdateDateDisplay()
        {
            txtDayName.Text = DayNames[(int)_currentDate.DayOfWeek];
            txtDayFull.Text = $"{_currentDate.Day} de {MonthNames[_currentDate.Month - 1]} {_currentDate.Year}";
        }

        private void BtnPrevDay_Click(object sender, RoutedEventArgs e) { _currentDate = _currentDate.AddDays(-1); RefreshAll(); }
        private void BtnNextDay_Click(object sender, RoutedEventArgs e) { _currentDate = _currentDate.AddDays(1); RefreshAll(); }
        private void BtnToday_Click(object sender, RoutedEventArgs e) { _currentDate = DateTime.Today; RefreshAll(); }

        private void RefreshAll()
        {
            UpdateDateDisplay();
            BuildWeekStrip();
            BuildEntries();
            UpdateSummary();
            _ = Dispatcher.BeginInvoke(PositionAboveTray, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ══════════════════════════════════════════════════
        // WEEK STRIP (built in code)
        // ══════════════════════════════════════════════════

        private void BuildWeekStrip()
        {
            WeekStripPanel.Children.Clear();
            var weekStart = GetWeekStart(_currentDate);

            for (int i = 0; i < 7; i++)
            {
                var date = weekStart.AddDays(i);
                var key = DateKey(date);
                double hrs = _entries.ContainsKey(key) ? _entries[key].Sum(x => x.Hours) : 0;
                bool isActive = date.Date == _currentDate.Date;

                var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

                sp.Children.Add(new TextBlock
                {
                    Text = DayShort[(int)date.DayOfWeek],
                    FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = TextMutedBrushCached,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                sp.Children.Add(new TextBlock
                {
                    Text = date.Day.ToString(),
                    FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = isActive ? AccentBrushCached : TextPrimaryBrushCached,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0)
                });

                sp.Children.Add(new TextBlock
                {
                    Text = hrs > 0 ? hrs.ToString("0.0") : "--",
                    FontSize = 9, FontFamily = new FontFamily("Consolas"),
                    Foreground = hrs > 0 ? GreenBrushCached : TextMutedBrushCached,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0)
                });

                var border = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(4, 5, 4, 5),
                    Margin = new Thickness(2),
                    Background = isActive ? ActiveBgBrushCached : TransparentBrush,
                    Child = sp,
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                // Store date for click
                var capturedDate = date;
                border.MouseLeftButtonDown += (s, ev) =>
                {
                    _currentDate = capturedDate;
                    RefreshAll();
                };

                // Hover effect
                border.MouseEnter += (s, ev) =>
                {
                    if (!isActive) ((Border)s!).Background = BgHoverBrushCached;
                };
                border.MouseLeave += (s, ev) =>
                {
                    if (!isActive) ((Border)s!).Background = TransparentBrush;
                };

                WeekStripPanel.Children.Add(border);
            }
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            int diff = ((int)date.DayOfWeek - 1 + 7) % 7;
            return date.AddDays(-diff).Date;
        }

        // ══════════════════════════════════════════════════
        // SUMMARY
        // ══════════════════════════════════════════════════

        private void UpdateSummary()
        {
            var key = DateKey(_currentDate);
            double todayHrs = _entries.ContainsKey(key) ? _entries[key].Sum(x => x.Hours) : 0;
            txtTodayHours.Text = todayHrs.ToString("0.0");

            var weekStart = GetWeekStart(_currentDate);
            double weekHrs = 0;
            for (int i = 0; i < 7; i++)
            {
                var k = DateKey(weekStart.AddDays(i));
                if (_entries.ContainsKey(k)) weekHrs += _entries[k].Sum(x => x.Hours);
            }
            txtWeekHours.Text = weekHrs.ToString("0.0");

            double pct = _weeklyTarget > 0 ? Math.Min(weekHrs / _weeklyTarget, 1.0) : 0;
            WeekProgressBar.Width = pct * 80;
        }

        // ══════════════════════════════════════════════════
        // ENTRIES LIST (built in code)
        // ══════════════════════════════════════════════════

        private void BuildEntries()
        {
            EntriesPanel.Children.Clear();
            var key = DateKey(_currentDate);
            var dayEntries = _entries.ContainsKey(key) ? _entries[key] : new List<TimeEntry>();

            txtEntryCount.Text = dayEntries.Count.ToString();
            txtEmptyState.Visibility = dayEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EntriesScrollViewer.Height = dayEntries.Count > 2 ? 112 : double.NaN;

            foreach (var entry in dayEntries)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Color bar
                var colorBar = new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Width = 3,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = HexToBrush(entry.ProjectColor)
                };
                Grid.SetColumn(colorBar, 0);
                grid.Children.Add(colorBar);

                // Info
                var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                infoPanel.Children.Add(new TextBlock
                {
                    Text = entry.ProjectName,
                    FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = TextPrimaryBrushCached,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                var subtitleText = entry.TaskName;
                if (!string.IsNullOrEmpty(entry.Notes))
                    subtitleText += " - " + entry.Notes;

                infoPanel.Children.Add(new TextBlock
                {
                    Text = subtitleText,
                    FontSize = 11,
                    Foreground = TextSecondaryBrushCached,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 1, 0, 0)
                });
                Grid.SetColumn(infoPanel, 2);
                grid.Children.Add(infoPanel);

                // Hours
                var hoursTb = new TextBlock
                {
                    Text = entry.Hours.ToString("0.0") + "h",
                    FontSize = 14, FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = AccentBrushCached,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                };
                Grid.SetColumn(hoursTb, 3);
                grid.Children.Add(hoursTb);

                // Delete button
                var delBtn = new Button
                {
                    Content = "X", FontSize = 12,
                    Width = 24, Height = 24,
                    Padding = new Thickness(0),
                    Opacity = 0.3,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Margin = new Thickness(6, 0, 0, 0),
                    Tag = entry.Id
                };
                delBtn.Style = (Style)FindResource("GhostButton");
                delBtn.BorderThickness = new Thickness(0);
                delBtn.Click += BtnDeleteEntry_Click;
                Grid.SetColumn(delBtn, 4);
                grid.Children.Add(delBtn);

                var entryBorder = new Border
                {
                    Padding = new Thickness(18, 8, 18, 8),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(25, 42, 45, 62)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Background = TransparentBrush,
                    Child = grid
                };

                // Hover
                var capturedDelBtn = delBtn;
                entryBorder.MouseEnter += (s, ev) =>
                {
                    ((Border)s!).Background = BgHoverBrushCached;
                    capturedDelBtn.Opacity = 1;
                };
                entryBorder.MouseLeave += (s, ev) =>
                {
                    ((Border)s!).Background = TransparentBrush;
                    capturedDelBtn.Opacity = 0.3;
                };

                EntriesPanel.Children.Add(entryBorder);
            }
        }

        private void BtnDeleteEntry_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var key = DateKey(_currentDate);
                if (_entries.ContainsKey(key))
                {
                    _entries[key].RemoveAll(x => x.Id == id);
                    CredentialService.SaveEntries(_entries);
                    RefreshAll();
                    ShowStatusMessage("Registro eliminado", false);
                }
            }
        }

        // ══════════════════════════════════════════════════
        // PROJECT / TASK SELECTION
        // ══════════════════════════════════════════════════

        private void CboProject_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboProject.SelectedItem is POProject project)
            {
                cboTask.ItemsSource = project.Tasks;
                if (project.Tasks.Count > 0) cboTask.SelectedIndex = 0;
            }
            else
            {
                cboTask.ItemsSource = null;
            }
        }

        private async void ChkShowAllTasks_Changed(object sender, RoutedEventArgs e)
        {
            if (!_api.IsLoggedIn)
                return;

            cboProject.IsEnabled = false;
            cboTask.IsEnabled = false;
            try
            {
                await ReloadProjectsAsync();
            }
            finally
            {
                cboProject.IsEnabled = true;
                cboTask.IsEnabled = true;
            }
        }

        // ══════════════════════════════════════════════════
        // HOURS INPUT
        // ══════════════════════════════════════════════════

        private void BtnHoursMinus_Click(object sender, RoutedEventArgs e) => AdjustHours(-0.5);
        private void BtnHoursPlus_Click(object sender, RoutedEventArgs e) => AdjustHours(0.5);

        private void AdjustHours(double delta)
        {
            if (TryParseHours(txtHours.Text, out var h))
            {
                h = Math.Max(0, Math.Round((h + delta) * 10) / 10);
                txtHours.Text = FormatHours(h);
            }
        }

        private void QuickHour_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
                txtHours.Text = FormatHours(double.Parse(btn.Tag.ToString()!, CultureInfo.InvariantCulture));
        }

        // ══════════════════════════════════════════════════
        // SUBMIT
        // ══════════════════════════════════════════════════

        private async void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            var project = cboProject.SelectedItem as POProject;
            var task = cboTask.SelectedItem as POTask;

            if (project == null) { ShowStatusMessage("Selecciona un proyecto", true); return; }
            if (task == null) { ShowStatusMessage("Selecciona una tarea", true); return; }
            if (!TryParseHours(txtHours.Text, out var hours) || hours <= 0)
            { ShowStatusMessage("Ingresa las horas", true); return; }

            var key = DateKey(_currentDate);
            if (!_entries.ContainsKey(key)) _entries[key] = new List<TimeEntry>();

            // Look for an existing local entry for the same project+task today
            var existingEntry = _entries[key].FirstOrDefault(x =>
                x.ProjectId == project.Id && x.TaskId == task.Id);

            // Accumulate only within the same day from local cache.
            // ExistingHours from the API is only reliable when projects were loaded for this exact date.
            double totalHours = hours;
            if (existingEntry != null)
                totalHours = existingEntry.Hours + hours;
            else if (task.ExistingHours > 0 && _currentDate.Date == _projectsLoadedForDate.Date)
                totalHours = task.ExistingHours + hours;

            // Combine notes before creating entry so the API receives the full text.
            var newNotes = txtNotes.Text.Trim();
            var combinedNotes = existingEntry != null && !string.IsNullOrEmpty(existingEntry.Notes) && !string.IsNullOrEmpty(newNotes)
                ? existingEntry.Notes + " - " + newNotes
                : !string.IsNullOrEmpty(newNotes) ? newNotes : existingEntry?.Notes ?? "";

            var entry = new TimeEntry
            {
                Date = _currentDate,
                ProjectId = project.Id,
                ProjectName = project.Name,
                ProjectColor = project.Color,
                TaskId = task.Id,
                TaskName = task.Name,
                GxTaskRowId = task.GxRowId,
                GxProjectRowId = task.GxProjectRowId,
                Hours = totalHours,
                Notes = combinedNotes
            };

            btnSubmit.IsEnabled = false;
            btnSubmit.Content = "Enviando...";

            var (success, message) = await _api.SubmitTimeEntryAsync(entry, chkShowAllTasks.IsChecked == true);

            if (success)
            {
                entry.Synced = true;
                btnSubmit.Content = "Registrado!";
                btnSubmit.Background = GreenBrushCached;
                ShowStatusMessage(message, false);
                _notifyIcon?.ShowBalloonTip("Horas registradas",
                    $"{totalHours:0.0}h - {project.Name}", BalloonIcon.Info);
            }
            else
            {
                entry.Synced = false;
                btnSubmit.Content = "Guardado local";
                btnSubmit.Background = AmberBrushCached;
                ShowStatusMessage($"{message} - guardado localmente", true);
            }

            // Dual-log to Jira if enabled
            if (chkLogToJira.IsChecked == true && _jiraSelectedIssues.Count > 0)
            {
                if (!_jira.IsConnected)
                {
                    var (cfg, token) = JiraConfigService.LoadConfig();
                    if (cfg != null && !string.IsNullOrEmpty(token))
                    {
                        _jira.Configure(cfg.BaseUrl, cfg.Email, token);
                        await _jira.TestConnectionAsync();
                    }
                }

                var jiraOkKeys  = new List<string>();
                var jiraErrMsgs = new List<string>();

                foreach (var (jiraKey, jiraHours) in _jiraSelectedIssues.Where(x => x.Value > 0))
                {
                    var (jiraOk, jiraMsg) = await _jira.LogWorkAsync(jiraKey, jiraHours, _currentDate, txtNotes.Text.Trim());
                    if (jiraOk) jiraOkKeys.Add(jiraKey);
                    else        jiraErrMsgs.Add($"{jiraKey}: {jiraMsg}");
                }

                entry.JiraIssueKey = string.Join(", ", jiraOkKeys);
                entry.JiraSynced   = jiraErrMsgs.Count == 0;

                if (jiraErrMsgs.Count == 0)
                    ShowStatusMessage($"PO + Jira ({string.Join(", ", jiraOkKeys)}): OK", false);
                else if (jiraOkKeys.Count > 0)
                    ShowStatusMessage($"PO OK — Jira parcial: {string.Join("; ", jiraErrMsgs)}", true);
                else
                    ShowStatusMessage($"PO OK — Jira error: {string.Join("; ", jiraErrMsgs)}", true);
            }

            // Update local entry (merge or add)
            if (existingEntry != null)
            {
                existingEntry.Hours = totalHours;
                existingEntry.Notes = entry.Notes;
                existingEntry.Synced = entry.Synced;
            }
            else
            {
                _entries[key].Add(entry);
            }

            CredentialService.SaveEntries(_entries);

            await System.Threading.Tasks.Task.Delay(1500);
            btnSubmit.Content = "Registrar Horas";
            btnSubmit.Background = AccentBrushCached;
            btnSubmit.IsEnabled = true;
            txtNotes.Text = "";
            txtHours.Text = "1.0";
            if (chkLogToJira.IsChecked == true)
                chkLogToJira.IsChecked = false;
            RefreshAll();
        }

        // ══════════════════════════════════════════════════
        // STATUS
        // ══════════════════════════════════════════════════

        private async void ShowStatusMessage(string msg, bool isError)
        {
            txtStatus.Text = msg;
            txtStatus.Foreground = isError ? RedBrushCached : GreenBrushCached;
            txtStatus.Visibility = Visibility.Visible;
            _ = Dispatcher.BeginInvoke(PositionAboveTray, System.Windows.Threading.DispatcherPriority.Loaded);
            await System.Threading.Tasks.Task.Delay(3000);
            txtStatus.Visibility = Visibility.Collapsed;
            _ = Dispatcher.BeginInvoke(PositionAboveTray, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ══════════════════════════════════════════════════
        // JIRA INTEGRATION
        // ══════════════════════════════════════════════════

        private void InitJira()
        {
            var (config, token) = JiraConfigService.LoadConfig();
            bool configured = config != null
                && !string.IsNullOrWhiteSpace(config.BaseUrl)
                && !string.IsNullOrWhiteSpace(token);

            JiraLinkPanel.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;

            if (!configured) { UpdateJiraButtonState(false); return; }

            _jira.Configure(config!.BaseUrl, config.Email, token);
            _ = TryAutoConnectJiraAsync();
        }

        private async System.Threading.Tasks.Task TryAutoConnectJiraAsync()
        {
            var (ok, _, _) = await _jira.TestConnectionAsync();
            UpdateJiraButtonState(ok);
        }

        private void UpdateJiraButtonState(bool connected)
        {
            JiraConnectedDot.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            btnJira.ToolTip = connected ? "Jira conectado" : "Abrir Jira";
        }

        private void BtnJira_Click(object sender, RoutedEventArgs e) => OpenJiraWindow();

        private void MenuItem_Jira_Click(object sender, RoutedEventArgs e) => OpenJiraWindow();

        private void OpenJiraWindow()
        {
            if (_jiraWindow == null || !_jiraWindow.IsLoaded)
            {
                _jiraWindow = new JiraWindow();
                _jiraWindow.WindowHidden += (_, _) => ShowMainWindowAfterJira();
                _jiraWindow.Closed       += (_, _) => { _jiraWindow = null; ShowMainWindowAfterJira(); };
            }

            Hide();
            PositionWindowAboveTray(_jiraWindow);
            _jiraWindow.Show();
            _jiraWindow.Activate();
        }

        private void ShowMainWindowAfterJira()
        {
            // Re-check Jira connection state when returning
            UpdateJiraButtonState(_jira.IsConnected);
            InitJira(); // refresh panel visibility and re-connect if needed

            _suppressAutoHide = true;
            Show();
            PositionAboveTray();
            Activate();
            PlayFadeIn();
            _ = System.Threading.Tasks.Task.Delay(500).ContinueWith(
                _ => Dispatcher.Invoke(() => _suppressAutoHide = false));
        }

        private static void PositionWindowAboveTray(Window win)
        {
            win.UpdateLayout();
            var wa = SystemParameters.WorkArea;
            double h = win.ActualHeight > 50 ? win.ActualHeight : 750;
            win.Left = Math.Max(wa.Left, wa.Right - win.Width - 10);
            win.Top  = Math.Max(wa.Top,  wa.Bottom - h - 10);
        }

        private void ChkLogToJira_Changed(object sender, RoutedEventArgs e)
        {
            JiraIssueInput.Visibility = chkLogToJira.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (chkLogToJira.IsChecked == true)
            {
                _jiraSelectedIssues.Clear();
                _ = LoadJiraIssuesAsync();
            }
            else
            {
                _jiraSelectedIssues.Clear();
                _jiraAllIssues.Clear();
            }

            _ = Dispatcher.BeginInvoke(PositionAboveTray, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private async System.Threading.Tasks.Task LoadJiraIssuesAsync()
        {
            if (!_jira.IsConnected)
            {
                var (cfg, token) = JiraConfigService.LoadConfig();
                if (cfg != null && !string.IsNullOrEmpty(token))
                {
                    _jira.Configure(cfg.BaseUrl, cfg.Email, token);
                    await _jira.TestConnectionAsync();
                }
            }

            JiraIssuesLoading.Visibility      = Visibility.Visible;
            JiraIssuesScrollViewer.Visibility  = Visibility.Collapsed;
            txtJiraIssuesEmpty.Visibility      = Visibility.Collapsed;
            JiraHoursSummary.Visibility        = Visibility.Collapsed;
            JiraIssuesPanel.Children.Clear();
            _ = Dispatcher.BeginInvoke(PositionAboveTray, System.Windows.Threading.DispatcherPriority.Loaded);

            _jiraAllIssues = await _jira.GetMyIssuesAsync("");

            JiraIssuesLoading.Visibility = Visibility.Collapsed;

            if (_jiraAllIssues.Count == 0)
            {
                txtJiraIssuesEmpty.Visibility = Visibility.Visible;
            }
            else
            {
                JiraIssuesScrollViewer.Visibility = Visibility.Visible;
                BuildJiraIssuesList();
            }

            _ = Dispatcher.BeginInvoke(PositionAboveTray, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void BuildJiraIssuesList()
        {
            JiraIssuesPanel.Children.Clear();
            foreach (var issue in _jiraAllIssues)
                JiraIssuesPanel.Children.Add(BuildJiraIssueRow(issue));
            UpdateJiraHoursSummary();
        }

        private Border BuildJiraIssueRow(JiraIssue issue)
        {
            var outerGrid = new Grid();
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var bar = new Border
            {
                CornerRadius      = new CornerRadius(2),
                Width             = 3,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background        = GetJiraStatusBrush(issue.StatusCategory)
            };
            Grid.SetColumn(bar, 0);
            outerGrid.Children.Add(bar);

            var keyBlock = new TextBlock
            {
                Text       = issue.Key,
                FontSize   = 10,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(38, 132, 255))
            };
            var summaryBlock = new TextBlock
            {
                Text         = issue.Summary,
                FontSize     = 11,
                Foreground   = TextPrimaryBrushCached,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin       = new Thickness(0, 1, 0, 0)
            };
            var infoPanel = new StackPanel();
            infoPanel.Children.Add(keyBlock);
            infoPanel.Children.Add(summaryBlock);

            var chk = new CheckBox
            {
                Content                  = infoPanel,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin                   = new Thickness(0)
            };
            Grid.SetColumn(chk, 2);
            outerGrid.Children.Add(chk);

            var hoursBox = new TextBox
            {
                Text              = "0.0",
                Width             = 48,
                FontFamily        = new FontFamily("Consolas"),
                FontSize          = 12,
                TextAlignment     = TextAlignment.Center,
                Visibility        = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(8, 0, 0, 0)
            };
            if (TryFindResource("ModernTextBox") is Style boxStyle)
                hoursBox.Style = boxStyle;

            Grid.SetColumn(hoursBox, 3);
            outerGrid.Children.Add(hoursBox);

            var captured = issue;
            chk.Checked += (s, ev) =>
            {
                hoursBox.Visibility = Visibility.Visible;
                double remaining    = GetJiraRemainingHours();
                hoursBox.Text       = remaining.ToString("0.0", CultureInfo.CurrentCulture);
                Dispatcher.BeginInvoke(PositionAboveTray, System.Windows.Threading.DispatcherPriority.Loaded);
            };
            chk.Unchecked += (s, ev) =>
            {
                hoursBox.Visibility = Visibility.Collapsed;
                _jiraSelectedIssues.Remove(captured.Key);
                UpdateJiraHoursSummary();
                Dispatcher.BeginInvoke(PositionAboveTray, System.Windows.Threading.DispatcherPriority.Loaded);
            };
            hoursBox.TextChanged += (s, _) =>
            {
                if (chk.IsChecked == true && TryParseHours(hoursBox.Text, out var h) && h >= 0)
                {
                    _jiraSelectedIssues[captured.Key] = h;
                    UpdateJiraHoursSummary();
                }
            };

            return new Border
            {
                Padding         = new Thickness(14, 7, 14, 7),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(25, 42, 45, 62)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background      = TransparentBrush,
                Child           = outerGrid
            };
        }

        private double GetJiraRemainingHours()
        {
            TryParseHours(txtHours.Text, out var total);
            var assigned = _jiraSelectedIssues.Values.Sum();
            return Math.Max(0, Math.Round((total - assigned) * 10) / 10);
        }

        private void UpdateJiraHoursSummary()
        {
            bool anySelected = _jiraSelectedIssues.Count > 0;
            JiraHoursSummary.Visibility = anySelected ? Visibility.Visible : Visibility.Collapsed;

            if (anySelected)
            {
                TryParseHours(txtHours.Text, out var total);
                var assigned = _jiraSelectedIssues.Values.Sum();
                txtJiraHoursAssigned.Text      = assigned.ToString("0.0", CultureInfo.CurrentCulture);
                txtJiraHoursTotal.Text         = total.ToString("0.0", CultureInfo.CurrentCulture);
                txtJiraHoursAssigned.Foreground = assigned > total + 0.01 ? RedBrushCached : AccentBrushCached;
            }

            _ = Dispatcher.BeginInvoke(PositionAboveTray, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static SolidColorBrush GetJiraStatusBrush(string category) => category switch
        {
            "done"          => new SolidColorBrush(Color.FromRgb(52, 211, 153)),
            "indeterminate" => new SolidColorBrush(Color.FromRgb(251, 191, 36)),
            _               => new SolidColorBrush(Color.FromRgb(38, 132, 255))
        };

        // ══════════════════════════════════════════════════
        // SYSTEM TRAY
        // ══════════════════════════════════════════════════

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => Hide();

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (_suppressAutoHide)
                return;

            if (IsVisible && MainView.Visibility == Visibility.Visible) Hide();
        }

        private void EnsureRunAtStartup()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                    exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exePath))
                    return;

                const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
                using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
                key?.SetValue("POTimeTracker", $"\"{exePath}\" --background");
            }
            catch (Exception ex)
            {
                LogService.Warn("EnsureRunAtStartup: no se pudo registrar inicio automatico", ex);
            }
        }

        private static bool IsBackgroundStartup()
        {
            return Environment.GetCommandLineArgs()
                .Any(a => string.Equals(a, "--background", StringComparison.OrdinalIgnoreCase));
        }

        private void StartDailyReminderTimer()
        {
            _reminderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _reminderTimer.Tick += (_, _) => CheckDailyReminder();
            _reminderTimer.Start();
            CheckDailyReminder();
        }

        private void CheckDailyReminder()
        {
            var now = DateTime.Now;

            if (now.DayOfWeek == DayOfWeek.Saturday && !_reminderOnSaturday) return;
            if (now.DayOfWeek == DayOfWeek.Sunday && !_reminderOnSunday) return;

            var reminderTime = now.Date.AddHours(_reminderHour).AddMinutes(_reminderMinute);
            if (now < reminderTime || _lastReminderDate == now.Date)
                return;

            _lastReminderDate = now.Date;
            ShowDailyReminder();
        }

        private void StartReloginTimer()
        {
            _reloginTimer?.Stop();
            double interval = _reloginIntervalHours > 0 ? _reloginIntervalHours : 3.0;
            _reloginTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromHours(interval)
            };
            _reloginTimer.Tick += async (_, _) => await BackgroundReloginAsync();
            _reloginTimer.Start();
        }

        private async System.Threading.Tasks.Task BackgroundReloginAsync()
        {
            if (!_api.IsLoggedIn) return;
            var creds = CredentialService.LoadCredentials();
            if (creds == null || string.IsNullOrEmpty(creds.Username)) return;
            try
            {
                var (success, message) = await _api.LoginAsync(creds.ServerUrl, creds.Username, creds.Password);
                if (!success)
                    LogService.Warn($"BackgroundRelogin: fallo el re-login: {message}");
            }
            catch (Exception ex)
            {
                LogService.Error("BackgroundReloginAsync: excepcion inesperada", ex);
            }
        }

        private void ShowDailyReminder()
        {
            if (_reminderWindow?.IsVisible == true)
            {
                _reminderWindow.Activate();
                return;
            }

            _reminderWindow = new ReminderWindow();
            _reminderWindow.Closed += (_, _) => _reminderWindow = null;
            _reminderWindow.Show();
            _reminderWindow.PositionBottomRight();
            _reminderWindow.Activate();
        }

        private void ApplyMenuIcons()
        {
            if (_notifyIcon?.ContextMenu == null)
                return;

            SetMenuIcon(FindMenuItem("Abrir Widget"),      "open-widget.png");
            SetMenuIcon(FindMenuItem("Abrir Jira"),        "jira.png");
            SetMenuIcon(FindMenuItem("Recargar Proyectos"),"reload-projects.png");
            SetMenuIcon(FindMenuItem("Configuracion"),     "settings.png");
            SetMenuIcon(FindMenuItem("Cerrar Sesion"),     "logout.png");
            SetMenuIcon(FindMenuItem("Salir"),             "exit.png");
        }

        private MenuItem? FindMenuItem(string header)
        {
            return _notifyIcon?.ContextMenu.Items
                .OfType<MenuItem>()
                .FirstOrDefault(i => string.Equals(i.Header?.ToString(), header, StringComparison.Ordinal));
        }

        private static void SetMenuIcon(MenuItem? item, string fileName)
        {
            if (item == null)
                return;

            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            if (!System.IO.File.Exists(path))
                return;

            item.Icon = new Image
            {
                Width = 16,
                Height = 16,
                Source = new BitmapImage(new Uri(path, UriKind.Absolute))
            };
        }

        private void NotifyIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e) => ToggleVisibility();

        private void ToggleVisibility()
        {
            if (IsVisible) { Hide(); }
            else { Show(); PositionAboveTray(); Activate(); PlayFadeIn(); }
        }

        private void MenuItem_Open_Click(object sender, RoutedEventArgs e)
        { Show(); PositionAboveTray(); Activate(); }

        private async void MenuItem_Reload_Click(object sender, RoutedEventArgs e)
        {
            await ReloadProjectsAsync();
            _notifyIcon?.ShowBalloonTip("PO Time Tracker", "Proyectos recargados", BalloonIcon.Info);
        }

        private void MenuItem_Logout_Click(object sender, RoutedEventArgs e)
        {
            _api.Logout();
            CredentialService.ClearCredentials();
            txtPassword.Password = "";
            MainView.Visibility = Visibility.Collapsed;
            LoginView.Visibility = Visibility.Visible;
            Show(); PositionAboveTray(); Activate();
        }

        private void MenuItem_Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();
        private void BtnSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

        private void OpenSettings()
        {
            var win = new SettingsWindow();

            Hide();

            win.ShowDialog();

            if (win.SettingsSaved)
            {
                LoadSettings();
                StartReloginTimer();
                _weeklyTarget = CredentialService.LoadConfig()?.WeeklyTarget ?? _weeklyTarget;
                txtTargetHours.Text = _weeklyTarget.ToString("0.0");
                UpdateSummary();
            }

            _suppressAutoHide = true;
            Show();
            PositionAboveTray();
            Activate();
            PlayFadeIn();
            _ = System.Threading.Tasks.Task.Delay(500).ContinueWith(
                _ => Dispatcher.Invoke(() => _suppressAutoHide = false));
        }

        private void MenuItem_Exit_Click(object sender, RoutedEventArgs e)
        {
            _notifyIcon?.Dispose();
            _api.Dispose();
            _jira.Dispose();
            _jiraWindow?.Close();
            Application.Current.Shutdown();
        }

        // ══════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════

        private static string DateKey(DateTime d) => d.ToString("yyyy-MM-dd");

        private static bool TryParseHours(string text, out double hours)
        {
            var normalized = text.Trim().Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out hours);
        }

        private static string FormatHours(double hours)
        {
            return hours.ToString("0.0", CultureInfo.CurrentCulture);
        }

        private static SolidColorBrush HexToBrush(string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch
            {
                return AccentBrushCached;
            }
        }
    }
}
