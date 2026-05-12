using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace POTimeTracker.Avalonia.Views
{
    public partial class ReminderWindow : Window
    {
        public ReminderWindow()
        {
            InitializeComponent();
        }

        public void PositionBottomRight()
        {
            var screen = Screens?.Primary;
            if (screen == null)
                return;

            var area = screen.WorkingArea;
            var width = (int)(Bounds.Width > 10 ? Bounds.Width : Width);
            var height = (int)(Bounds.Height > 10 ? Bounds.Height : Height);
            var x = area.X + Math.Max(0, area.Width - width - 20);
            var y = area.Y + Math.Max(0, area.Height - height - 20);
            this.Position = new PixelPoint(x, y);
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
