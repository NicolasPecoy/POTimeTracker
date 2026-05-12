using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using POTimeTracker.Models;
using POTimeTracker.Services;

namespace POTimeTracker.Views
{
    public partial class SettingsWindow : Window
    {
        public bool SettingsSaved { get; private set; }

        private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(52, 211, 153));
        private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(248, 113, 113));

        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateHourCombo();
            PopulateMinuteCombo();
            LoadCurrentSettings();
            PositionAboveTray();
        }

        private void PositionAboveTray()
        {
            UpdateLayout();
            var wa = SystemParameters.WorkArea;
            double h = ActualHeight > 50 ? ActualHeight : 600;
            Left = Math.Max(wa.Left, wa.Right - Width - 10);
            Top = Math.Max(wa.Top, wa.Bottom - h - 10);
        }

        private void PopulateHourCombo()
        {
            var items = new List<ComboItem>();
            for (int h = 14; h <= 23; h++)
                items.Add(new ComboItem { Display = h.ToString("00"), Value = h });
            cboHour.ItemsSource = items;
        }

        private void PopulateMinuteCombo()
        {
            var items = new List<ComboItem>();
            for (int m = 0; m <= 55; m += 5)
                items.Add(new ComboItem { Display = m.ToString("00"), Value = m });
            cboMinute.ItemsSource = items;
        }

        private void LoadCurrentSettings()
        {
            var config = CredentialService.LoadConfig() ?? new LoginCredentials();

            // Reminder hour (clamp to 14-23 range)
            int hour = Math.Max(14, Math.Min(23, config.ReminderHour));
            SelectComboByValue(cboHour, hour);

            // Reminder minute (round to nearest 5)
            int minute = (config.ReminderMinute / 5) * 5;
            minute = Math.Max(0, Math.Min(55, minute));
            SelectComboByValue(cboMinute, minute);

            chkSaturday.IsChecked = config.ReminderOnSaturday;
            chkSunday.IsChecked = config.ReminderOnSunday;

            double interval = Math.Max(0.5, Math.Min(24, config.ReloginIntervalHours));
            txtReloginInterval.Text = interval.ToString("0.0", CultureInfo.InvariantCulture);

            double weekly = Math.Max(1, config.WeeklyTarget > 0 ? config.WeeklyTarget : 40);
            txtWeeklyTarget.Text = weekly.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static void SelectComboByValue(System.Windows.Controls.ComboBox combo, int value)
        {
            foreach (ComboItem item in combo.Items)
            {
                if (item.Value == value)
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
            if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (cboHour.SelectedItem is not ComboItem hourItem ||
                cboMinute.SelectedItem is not ComboItem minuteItem)
            {
                ShowStatus("Selecciona hora y minuto validos", true);
                return;
            }

            if (!TryParseDouble(txtReloginInterval.Text, out double interval) ||
                interval < 0.5 || interval > 24)
            {
                ShowStatus("Intervalo de re-conexion: entre 0.5 y 24 horas", true);
                return;
            }

            if (!TryParseDouble(txtWeeklyTarget.Text, out double weekly) || weekly < 1)
            {
                ShowStatus("Objetivo semanal invalido", true);
                return;
            }

            var existing = CredentialService.LoadConfig() ?? new LoginCredentials();
            existing.ReminderHour = hourItem.Value;
            existing.ReminderMinute = minuteItem.Value;
            existing.ReminderOnSaturday = chkSaturday.IsChecked == true;
            existing.ReminderOnSunday = chkSunday.IsChecked == true;
            existing.ReloginIntervalHours = interval;
            existing.WeeklyTarget = weekly;

            CredentialService.SaveConfig(existing);
            SettingsSaved = true;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnReloginMinus_Click(object sender, RoutedEventArgs e) =>
            AdjustValue(txtReloginInterval, -0.5, 0.5, 24);

        private void BtnReloginPlus_Click(object sender, RoutedEventArgs e) =>
            AdjustValue(txtReloginInterval, 0.5, 0.5, 24);

        private void BtnWeeklyMinus_Click(object sender, RoutedEventArgs e) =>
            AdjustValue(txtWeeklyTarget, -1, 1, 80);

        private void BtnWeeklyPlus_Click(object sender, RoutedEventArgs e) =>
            AdjustValue(txtWeeklyTarget, 1, 1, 80);

        private static void AdjustValue(System.Windows.Controls.TextBox box, double delta, double min, double max)
        {
            if (!TryParseDouble(box.Text, out double val)) val = min;
            val = Math.Max(min, Math.Min(max, Math.Round(val + delta, 1)));
            box.Text = val.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static bool TryParseDouble(string text, out double value)
        {
            return double.TryParse(
                text.Trim().Replace(',', '.'),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out value);
        }

        private async void ShowStatus(string msg, bool isError)
        {
            txtStatus.Text = msg;
            txtStatus.Foreground = isError ? RedBrush : GreenBrush;
            txtStatus.Visibility = Visibility.Visible;
            await System.Threading.Tasks.Task.Delay(3000);
            if (IsLoaded) txtStatus.Visibility = Visibility.Collapsed;
        }

        private class ComboItem
        {
            public string Display { get; set; } = "";
            public int Value { get; set; }
            public override string ToString() => Display;
        }
    }
}
