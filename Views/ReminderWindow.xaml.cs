using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using POTimeTracker.Services;

namespace POTimeTracker.Views
{
    public partial class ReminderWindow : Window
    {
        private const double BaseWidth = 360;

        public ReminderWindow()
        {
            InitializeComponent();
            Loaded += (s, _) =>
            {
                DragHeader.PreviewMouseLeftButtonDown += (s2, e) =>
                {
                    if (!IsInteractiveSource(e.OriginalSource))
                        try { DragMove(); } catch { }
                };

                ApplyUiScale();
                UiScaleService.ScaleChanged += OnUiScaleChanged;
            };
            Closed += (s, _) => UiScaleService.ScaleChanged -= OnUiScaleChanged;
        }

        private void ApplyUiScale()
        {
            var scale = UiScaleService.Current;
            Width = BaseWidth * scale;
            DragHeader.LayoutTransform = new ScaleTransform(scale, scale);
        }

        private void OnUiScaleChanged(double scale)
        {
            Dispatcher.Invoke(() =>
            {
                ApplyUiScale();
                PositionBottomRight();
            });
        }

        public void PositionBottomRight()
        {
            UpdateLayout();
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 18;
            Top  = wa.Bottom - ActualHeight - 18;
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

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
