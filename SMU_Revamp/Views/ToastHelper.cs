using Avalonia.Controls;
using Avalonia.Threading;
using System;

namespace SMU_Revamp.Views
{
    public static class ToastHelper
    {
        private static DispatcherTimer? _timer;

        public static void ShowToast(Control control, string message)
        {
            var topLevel = TopLevel.GetTopLevel(control) as Window;
            if (topLevel == null) return;

            var toastBorder = topLevel.FindControl<Border>("ToastBorder");
            var toastTextBlock = topLevel.FindControl<TextBlock>("ToastTextBlock");

            if (toastBorder == null || toastTextBlock == null) return;

            // Stop existing timers
            _timer?.Stop();

            toastTextBlock.Text = message;
            toastBorder.IsVisible = true;
            toastBorder.Opacity = 1;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _timer.Tick += (s, e) =>
            {
                _timer.Stop();
                toastBorder.Opacity = 0;
                
                // Delay setting IsVisible = false so fade out transition can play
                var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                hideTimer.Tick += (s2, e2) =>
                {
                    hideTimer.Stop();
                    if (toastBorder.Opacity == 0) // Ensure no new toast was shown in the meantime
                    {
                        toastBorder.IsVisible = false;
                    }
                };
                hideTimer.Start();
            };
            _timer.Start();
        }
    }
}
