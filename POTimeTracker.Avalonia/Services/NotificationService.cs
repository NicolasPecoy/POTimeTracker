using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace POTimeTracker.Avalonia.Services
{
    // Lightweight in-window notification system to avoid external package dependency
    public class NotificationService
    {
        private Window? _window;

        public void InitializeForWindow(Window window)
        {
            _window = window;
        }

        public void Show(string title, string message)
        {
            if (_window == null) return;
            Dispatcher.UIThread.Post(() =>
            {
                var toast = new Border
                {
                    Background = Brushes.Black,
                    Opacity = 0.85,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    Child = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeight.Bold },
                            new TextBlock { Text = message, Foreground = Brushes.White, FontSize = 12 }
                        }
                    }
                };

                // Place in top-right corner of window overlay
                var adorner = new Canvas();
                Canvas.SetTop(toast, 12);
                Canvas.SetRight(toast, 12);
                adorner.Children.Add(toast);
                if (_window.Content is Panel p)
                {
                    p.Children.Add(adorner);
                    // remove after 3 seconds
                    _ = RemoveAfter(adorner, p, TimeSpan.FromSeconds(3));
                }
            });
        }

        private async System.Threading.Tasks.Task RemoveAfter(Control adorner, Panel parent, TimeSpan delay)
        {
            await System.Threading.Tasks.Task.Delay(delay);
            Dispatcher.UIThread.Post(() => { parent.Children.Remove(adorner); });
        }
    }
}
