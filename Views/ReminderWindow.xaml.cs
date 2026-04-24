using System.Windows;

namespace POTimeTracker.Views
{
    public partial class ReminderWindow : Window
    {
        public ReminderWindow()
        {
            InitializeComponent();
        }

        public void PositionBottomRight()
        {
            UpdateLayout();
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 18;
            Top = wa.Bottom - ActualHeight - 18;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
