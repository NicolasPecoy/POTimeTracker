using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using POTimeTracker.Avalonia.Services;
using POTimeTracker.Models;

namespace POTimeTracker.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        private readonly CredentialService _cred = new();
        private readonly POApiService _api = new();
        private readonly NotificationService _notifSvc = new();

        private DateTime _currentDate = DateTime.Today;
        private double _weeklyTarget = 40;
        private List<POProject> _projects = new();
        private readonly Dictionary<string, List<TimeEntry>> _entries = new();

        private global::Avalonia.Controls.TrayIcon? _tray;
        private DispatcherTimer? _reminderTimer;
        private DateTime? _lastReminderDate;
        private ReminderWindow? _reminderWindow;
        private bool _suppressAutoHide;

        private static readonly string[] DayNames = { "DOMINGO", "LUNES", "MARTES", "MIERCOLES", "JUEVES", "VIERNES", "SABADO" };
        private static readonly string[] MonthNames = { "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre" };
        private static readonly string[] DayShort = { "Dom", "Lun", "Mar", "Mie", "Jue", "Vie", "Sab" };

        public MainWindow()
        {
            InitializeComponent();

            _notifSvc.InitializeForWindow(this);
            this.Opacity = 0;
            this.RenderTransform = new TranslateTransform(0, 10);

            SetupTrayIcon();
            LoadSavedCredentials();

            RefreshAll();
            StartDailyReminderTimer();

            Opened += MainWindow_Opened;
            Deactivated += MainWindow_Deactivated;
            Closed += (_, _) =>
            {
                _reminderTimer?.Stop();
                _tray?.Dispose();
                _api.Dispose();
            };
        }

        private async void MainWindow_Opened(object? sender, EventArgs e)
        {
            EnsureAutostart();
            PositionBottomRight();

            var creds = _cred.LoadCredentials();
            if (creds != null && !string.IsNullOrWhiteSpace(creds.Username))
            {
                var success = await AttemptLoginAsync(
                    creds.ServerUrl,
                    creds.Username,
                    creds.Password,
                    remember: true);

                if (success && IsBackgroundStartup())
                    Hide();
            }
            else
            {
                LoginView.IsVisible = true;
                MainView.IsVisible = false;
                LoginStatusText.Text = string.Empty;
                this.Opacity = 1;
                if (this.RenderTransform is TranslateTransform tr)
                    tr.Y = 0;
            }
        }

        private void LoadSavedCredentials()
        {
            var creds = _cred.LoadCredentials();
            if (creds == null)
                return;

            ServerText.Text = creds.ServerUrl;
            UsernameText.Text = creds.Username;
            PasswordText.Text = creds.Password;
            RememberCheck.IsChecked = creds.RememberMe;
            _weeklyTarget = creds.WeeklyTarget;
            txtTargetHours.Text = _weeklyTarget.ToString("0.0");
        }

        private void SetupTrayIcon()
        {
            try
            {
                _tray = new global::Avalonia.Controls.TrayIcon();
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
                if (File.Exists(iconPath))
                    _tray.Icon = new global::Avalonia.Controls.WindowIcon(iconPath);

                var menu = new global::Avalonia.Controls.NativeMenu();
                var openItem = new global::Avalonia.Controls.NativeMenuItem("Abrir Widget");
                openItem.Click += (_, _) => Dispatcher.UIThread.Post(ShowFromTray);

                var reloadItem = new global::Avalonia.Controls.NativeMenuItem("Recargar Proyectos");
                reloadItem.Click += async (_, _) =>
                {
                    await ReloadProjectsAsync();
                    _notifSvc.Show("PO Time Tracker", "Proyectos recargados");
                };

                var logoutItem = new global::Avalonia.Controls.NativeMenuItem("Cerrar Sesion");
                logoutItem.Click += (_, _) => Dispatcher.UIThread.Post(LogoutToLogin);

                var autostartItem = new global::Avalonia.Controls.NativeMenuItem("Iniciar con el sistema");
                autostartItem.Click += (_, _) =>
                {
                    AutostartService.Enable(!AutostartService.IsEnabled());
                    _notifSvc.Show(
                        "PO Time Tracker",
                        AutostartService.IsEnabled() ? "Autostart activado" : "Autostart desactivado");
                };

                var exitItem = new global::Avalonia.Controls.NativeMenuItem("Salir");
                exitItem.Click += (_, _) => Dispatcher.UIThread.Post(CloseApplication);

                menu.Items.Add(openItem);
                menu.Items.Add(reloadItem);
                menu.Items.Add(logoutItem);
                menu.Items.Add(autostartItem);
                menu.Items.Add(new global::Avalonia.Controls.NativeMenuItemSeparator());
                menu.Items.Add(exitItem);

                _tray.Menu = menu;
                _tray.ToolTipText = "PO Time Tracker";
            }
            catch
            {
                _tray = null;
            }
        }

        private static bool IsBackgroundStartup()
        {
            return Environment.GetCommandLineArgs()
                .Any(a => string.Equals(a, "--background", StringComparison.OrdinalIgnoreCase));
        }

        private void EnsureAutostart()
        {
            if (!AutostartService.IsEnabled())
                AutostartService.Enable(true);
        }

        private async void LoginButton_Click(object? sender, RoutedEventArgs e)
        {
            var server = (ServerText.Text ?? string.Empty).Trim();
            var username = (UsernameText.Text ?? string.Empty).Trim();
            var password = PasswordText.Text ?? string.Empty;
            var remember = RememberCheck.IsChecked == true;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                LoginStatusText.Text = "Ingresa usuario y contrasena";
                return;
            }

            await AttemptLoginAsync(server, username, password, remember);
        }

        private async Task<bool> AttemptLoginAsync(string server, string username, string password, bool remember)
        {
            LoginButton.IsEnabled = false;
            LoginStatusText.Text = "Conectando...";

            try
            {
                var (success, message) = await _api.LoginAsync(server, username, password);
                if (!success)
                {
                    LoginStatusText.Text = message;
                    return false;
                }

                if (remember)
                {
                    _cred.SaveCredentials(username, password, server);
                    _cred.SaveConfig(new LoginCredentials { WeeklyTarget = _weeklyTarget, ServerUrl = server });
                }

                await SwitchToMainViewAsync(server, username);
                return true;
            }
            finally
            {
                LoginButton.IsEnabled = true;
            }
        }

        private async Task SwitchToMainViewAsync(string server, string username)
        {
            _suppressAutoHide = true;
            try
            {
                LoginView.IsVisible = false;
                MainView.IsVisible = true;

                txtUserDisplay.Text = _api.CurrentUser ?? username;
                txtServerDisplay.Text = server.Replace("http://", "").Replace("https://", "");
                txtTargetHours.Text = _weeklyTarget.ToString("0.0");
                LoginStatusText.Text = string.Empty;

                await ReloadProjectsAsync();
                RefreshAll();
                PositionBottomRight();
                await ShowWithAnimation();
            }
            finally
            {
                await Task.Delay(500);
                _suppressAutoHide = false;
            }
        }

        private async Task ShowWithAnimation()
        {
            try
            {
                if (!this.IsVisible)
                    this.Show();

                for (int i = 0; i <= 12; i++)
                {
                    var t = i / 12.0;
                    var opacity = t;
                    var translateY = (1 - t) * 10;
                    Dispatcher.UIThread.Post(() =>
                    {
                        this.Opacity = opacity;
                        if (this.RenderTransform is TranslateTransform tr)
                            tr.Y = translateY;
                        else
                            this.RenderTransform = new TranslateTransform(0, translateY);
                    });
                    await Task.Delay(12);
                }

                Dispatcher.UIThread.Post(() =>
                {
                    this.Opacity = 1;
                    if (this.RenderTransform is TranslateTransform tr2)
                        tr2.Y = 0;
                });
            }
            catch
            {
            }
        }

        private void ShowFromTray()
        {
            Show();
            PositionBottomRight();
            Activate();
            _ = ShowWithAnimation();
        }

        private async Task ReloadProjectsAsync()
        {
            var selectedProjectId = (cboProject.SelectedItem as POProject)?.Id;
            var showAllTasks = chkShowAllTasks.IsChecked == true;

            _projects = await _api.GetProjectsAsync(_currentDate, showAllTasks);
            cboProject.ItemsSource = _projects;

            var selected = _projects.FirstOrDefault(p => p.Id == selectedProjectId) ?? _projects.FirstOrDefault();
            if (selected != null)
            {
                cboProject.SelectedItem = selected;
            }
            else
            {
                cboTask.ItemsSource = null;
            }
        }

        private void CboProject_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (cboProject.SelectedItem is POProject project)
            {
                cboTask.ItemsSource = project.Tasks;
                if (project.Tasks.Count > 0)
                    cboTask.SelectedIndex = 0;
            }
            else
            {
                cboTask.ItemsSource = null;
            }
        }

        private async void ChkShowAllTasks_Changed(object? sender, RoutedEventArgs e)
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

        private void BtnPrevDay_Click(object? sender, RoutedEventArgs e)
        {
            _currentDate = _currentDate.AddDays(-1);
            RefreshAll();
        }

        private void BtnNextDay_Click(object? sender, RoutedEventArgs e)
        {
            _currentDate = _currentDate.AddDays(1);
            RefreshAll();
        }

        private void BtnToday_Click(object? sender, RoutedEventArgs e)
        {
            _currentDate = DateTime.Today;
            RefreshAll();
        }

        private void BtnMinimize_Click(object? sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void RefreshAll()
        {
            UpdateDateDisplay();
            BuildWeekStrip();
            BuildEntries();
            UpdateSummary();
        }

        private void UpdateDateDisplay()
        {
            txtDayName.Text = DayNames[(int)_currentDate.DayOfWeek];
            txtDayFull.Text = $"{_currentDate.Day} de {MonthNames[_currentDate.Month - 1]} {_currentDate.Year}";
        }

        private void BuildWeekStrip()
        {
            WeekStripPanel.Children.Clear();
            var weekStart = GetWeekStart(_currentDate);

            for (var i = 0; i < 7; i++)
            {
                var date = weekStart.AddDays(i);
                var key = DateKey(date);
                var hrs = _entries.ContainsKey(key) ? _entries[key].Sum(x => x.Hours) : 0;
                var isActive = date.Date == _currentDate.Date;

                var stack = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                stack.Children.Add(new TextBlock
                {
                    Text = DayShort[(int)date.DayOfWeek],
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                stack.Children.Add(new TextBlock
                {
                    Text = date.Day.ToString(),
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                stack.Children.Add(new TextBlock
                {
                    Text = hrs > 0 ? hrs.ToString("0.0") : "--",
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = hrs > 0 ? new SolidColorBrush(Color.Parse("#10B981")) : new SolidColorBrush(Color.Parse("#6B7280"))
                });

                var button = new Button
                {
                    Content = stack,
                    MinWidth = 58,
                    Padding = new Thickness(6, 4),
                    Background = isActive ? new SolidColorBrush(Color.Parse("#E0E7FF")) : Brushes.Transparent
                };
                var capturedDate = date;
                button.Click += (_, _) =>
                {
                    _currentDate = capturedDate;
                    RefreshAll();
                };
                WeekStripPanel.Children.Add(button);
            }
        }

        private void BuildEntries()
        {
            EntriesPanel.Children.Clear();
            var key = DateKey(_currentDate);
            var dayEntries = _entries.ContainsKey(key) ? _entries[key] : new List<TimeEntry>();

            txtEntryCount.Text = dayEntries.Count.ToString();
            txtEmptyState.IsVisible = dayEntries.Count == 0;

            foreach (var entry in dayEntries)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3)));
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(8)));
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                var colorBar = new Border
                {
                    Width = 3,
                    Background = HexToBrush(entry.ProjectColor)
                };
                Grid.SetColumn(colorBar, 0);
                grid.Children.Add(colorBar);

                var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                infoPanel.Children.Add(new TextBlock
                {
                    Text = entry.ProjectName,
                    FontWeight = FontWeight.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                var subtitle = string.IsNullOrWhiteSpace(entry.Notes)
                    ? entry.TaskName
                    : $"{entry.TaskName} - {entry.Notes}";
                infoPanel.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.Parse("#4B5563")),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                Grid.SetColumn(infoPanel, 2);
                grid.Children.Add(infoPanel);

                var hoursText = new TextBlock
                {
                    Text = $"{entry.Hours:0.0}h",
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(hoursText, 3);
                grid.Children.Add(hoursText);

                var deleteButton = new Button
                {
                    Content = "X",
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(6, 0, 0, 0),
                    Tag = entry.Id
                };
                deleteButton.Click += BtnDeleteEntry_Click;
                Grid.SetColumn(deleteButton, 4);
                grid.Children.Add(deleteButton);

                var rowBorder = new Border
                {
                    Padding = new Thickness(8, 6),
                    BorderBrush = new SolidColorBrush(Color.Parse("#E5E7EB")),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = grid
                };

                EntriesPanel.Children.Add(rowBorder);
            }
        }

        private void BtnDeleteEntry_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
                return;

            var id = btn.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(id))
                return;

            var key = DateKey(_currentDate);
            if (!_entries.ContainsKey(key))
                return;

            _entries[key].RemoveAll(x => x.Id == id);
            RefreshAll();
            _ = ShowStatusMessage("Registro eliminado", false);
        }

        private void UpdateSummary()
        {
            var key = DateKey(_currentDate);
            var todayHours = _entries.ContainsKey(key) ? _entries[key].Sum(x => x.Hours) : 0;
            txtTodayHours.Text = todayHours.ToString("0.0");

            var weekStart = GetWeekStart(_currentDate);
            var weekHours = 0.0;
            for (var i = 0; i < 7; i++)
            {
                var dayKey = DateKey(weekStart.AddDays(i));
                if (_entries.ContainsKey(dayKey))
                    weekHours += _entries[dayKey].Sum(x => x.Hours);
            }

            txtWeekHours.Text = weekHours.ToString("0.0");
            txtTargetHours.Text = _weeklyTarget.ToString("0.0");

            var percent = _weeklyTarget > 0 ? Math.Min(weekHours / _weeklyTarget, 1.0) * 100.0 : 0;
            WeekProgressBar.Value = percent;
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            var diff = ((int)date.DayOfWeek - 1 + 7) % 7;
            return date.AddDays(-diff).Date;
        }

        private void BtnHoursMinus_Click(object? sender, RoutedEventArgs e) => AdjustHours(-0.5);
        private void BtnHoursPlus_Click(object? sender, RoutedEventArgs e) => AdjustHours(0.5);

        private void AdjustHours(double delta)
        {
            if (!TryParseHours(txtHours.Text ?? "0", out var hours))
                return;

            hours = Math.Max(0, Math.Round((hours + delta) * 10) / 10);
            txtHours.Text = FormatHours(hours);
        }

        private void QuickHour_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag == null)
                return;

            if (!double.TryParse(button.Tag.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return;

            txtHours.Text = FormatHours(value);
        }

        private async void BtnSubmit_Click(object? sender, RoutedEventArgs e)
        {
            var project = cboProject.SelectedItem as POProject;
            var task = cboTask.SelectedItem as POTask;

            if (project == null)
            {
                await ShowStatusMessage("Selecciona un proyecto", true);
                return;
            }

            if (task == null)
            {
                await ShowStatusMessage("Selecciona una tarea", true);
                return;
            }

            if (!TryParseHours(txtHours.Text ?? "0", out var hours) || hours <= 0)
            {
                await ShowStatusMessage("Ingresa las horas", true);
                return;
            }

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
                Notes = (txtNotes.Text ?? string.Empty).Trim()
            };

            btnSubmit.IsEnabled = false;
            btnSubmit.Content = "Enviando...";

            var (success, message) = await _api.SubmitTimeEntryAsync(entry, chkShowAllTasks.IsChecked == true);
            entry.Synced = success;

            if (success)
            {
                btnSubmit.Content = "Registrado!";
                _notifSvc.Show("Horas registradas", $"{hours:0.0}h - {project.Name}");
                await ShowStatusMessage(message, false);
            }
            else
            {
                btnSubmit.Content = "Guardado local";
                await ShowStatusMessage($"{message} - guardado localmente", true);
            }

            var key = DateKey(_currentDate);
            if (!_entries.ContainsKey(key))
                _entries[key] = new List<TimeEntry>();
            _entries[key].Add(entry);

            await Task.Delay(1200);
            btnSubmit.Content = "Registrar Horas";
            btnSubmit.IsEnabled = true;
            txtNotes.Text = string.Empty;
            txtHours.Text = "1.0";
            RefreshAll();
        }

        private async Task ShowStatusMessage(string message, bool isError)
        {
            txtStatus.Text = message;
            txtStatus.Foreground = isError
                ? new SolidColorBrush(Color.Parse("#B91C1C"))
                : new SolidColorBrush(Color.Parse("#047857"));
            txtStatus.IsVisible = true;
            await Task.Delay(3000);
            txtStatus.IsVisible = false;
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            if (_suppressAutoHide)
                return;

            if (IsVisible && MainView.IsVisible)
                Hide();
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

        private void LogoutToLogin()
        {
            _api.Logout();
            _cred.ClearCredentials();

            PasswordText.Text = string.Empty;
            LoginStatusText.Text = string.Empty;
            txtUserDisplay.Text = "-";

            MainView.IsVisible = false;
            LoginView.IsVisible = true;
            ShowFromTray();
        }

        private void CloseApplication()
        {
            _tray?.Dispose();
            _api.Dispose();
            Close();
        }

        private void PositionBottomRight()
        {
            var screen = Screens?.Primary;
            if (screen == null)
                return;

            var area = screen.WorkingArea;
            var width = (int)(Bounds.Width > 10 ? Bounds.Width : Width);
            var height = (int)(Bounds.Height > 10 ? Bounds.Height : Height);

            var x = area.X + Math.Max(0, area.Width - width - 10);
            var y = area.Y + Math.Max(0, area.Height - height - 10);
            Position = new PixelPoint(x, y);
        }

        private static string DateKey(DateTime date) => date.ToString("yyyy-MM-dd");

        private static bool TryParseHours(string text, out double hours)
        {
            var normalized = text.Trim().Replace(',', '.');
            return double.TryParse(
                normalized,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out hours);
        }

        private static string FormatHours(double hours)
        {
            return hours.ToString("0.0", CultureInfo.CurrentCulture);
        }

        private static IBrush HexToBrush(string hex)
        {
            try
            {
                return new SolidColorBrush(Color.Parse(hex));
            }
            catch
            {
                return new SolidColorBrush(Color.Parse("#6366F1"));
            }
        }
    }
}
