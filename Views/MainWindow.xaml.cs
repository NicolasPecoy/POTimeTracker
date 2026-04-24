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
        private readonly POApiService _api = new();
        private DateTime _currentDate = DateTime.Today;
        private double _weeklyTarget = 40;
        private List<POProject> _projects = new();
        private readonly Dictionary<string, List<TimeEntry>> _entries = new();
        private TaskbarIcon? _notifyIcon;
        private bool _suppressAutoHide;
        private DispatcherTimer? _reminderTimer;
        private DateTime? _lastReminderDate;
        private ReminderWindow? _reminderWindow;

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
            StartDailyReminderTimer();
            PositionAboveTray();

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
                Hours = hours,
                Notes = txtNotes.Text.Trim()
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
                    $"{hours:0.0}h - {project.Name}", BalloonIcon.Info);
            }
            else
            {
                entry.Synced = false;
                btnSubmit.Content = "Guardado local";
                btnSubmit.Background = AmberBrushCached;
                ShowStatusMessage($"{message} - guardado localmente", true);
            }

            var key = DateKey(_currentDate);
            if (!_entries.ContainsKey(key)) _entries[key] = new List<TimeEntry>();
            _entries[key].Add(entry);

            await System.Threading.Tasks.Task.Delay(1500);
            btnSubmit.Content = "Registrar Horas";
            btnSubmit.Background = AccentBrushCached;
            btnSubmit.IsEnabled = true;
            txtNotes.Text = "";
            txtHours.Text = "1.0";
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
            await System.Threading.Tasks.Task.Delay(3000);
            txtStatus.Visibility = Visibility.Collapsed;
        }

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
            catch
            {
                // Startup registration is best-effort; the app still works from tray/manual launch.
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
            var reminderTime = now.Date.AddHours(17).AddMinutes(15);
            if (now < reminderTime || _lastReminderDate == now.Date)
                return;

            _lastReminderDate = now.Date;
            ShowDailyReminder();
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

            SetMenuIcon(FindMenuItem("Abrir Widget"), "open-widget.png");
            SetMenuIcon(FindMenuItem("Recargar Proyectos"), "reload-projects.png");
            SetMenuIcon(FindMenuItem("Cerrar Sesion"), "logout.png");
            SetMenuIcon(FindMenuItem("Salir"), "exit.png");
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

        private void MenuItem_Exit_Click(object sender, RoutedEventArgs e)
        {
            _notifyIcon?.Dispose();
            _api.Dispose();
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
