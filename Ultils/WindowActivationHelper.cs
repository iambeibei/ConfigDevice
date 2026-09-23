using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace LightGateway.Ultils
{
    public static class WindowActivationHelper
    {
        // 注册一个附加属性，类型为 bool，名字叫 IsMouseInsideWindow
        public static readonly DependencyProperty IsMouseInsideWindowProperty =
            DependencyProperty.RegisterAttached(
                "IsMouseInsideWindow",
                typeof(bool),
                typeof(WindowActivationHelper),
                new PropertyMetadata(false));

        public static bool GetIsMouseInsideWindow(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsMouseInsideWindowProperty);
        }

        public static void SetIsMouseInsideWindow(DependencyObject obj, bool value)
        {
            obj.SetValue(IsMouseInsideWindowProperty, value);
        }

        // 当附加属性被加载到 Window 上时，启动监听
        public static readonly DependencyProperty TrackMouseProperty =
            DependencyProperty.RegisterAttached(
                "TrackMouse",
                typeof(bool),
                typeof(WindowActivationHelper),
                new PropertyMetadata(false, OnTrackMouseChanged));

        public static bool GetTrackMouse(DependencyObject obj)
        {
            return (bool)obj.GetValue(TrackMouseProperty);
        }

        public static void SetTrackMouse(DependencyObject obj, bool value)
        {
            obj.SetValue(TrackMouseProperty, value);
        }

        private static void OnTrackMouseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Window window)
            {
                if ((bool)e.NewValue)
                {
                    window.Loaded += Window_Loaded;
                }
                else
                {
                    window.Loaded -= Window_Loaded;
                }
            }
        }

        private static void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
            {
                HwndSource hwndSource = PresentationSource.FromVisual(window) as HwndSource;
                if (hwndSource != null)
                {
                    hwndSource.AddHook(WndProc);
                }

                // 监听窗口内部鼠标按下，解决 WPF 点击空白处丢失焦点的 bug
                window.PreviewMouseDown += Window_PreviewMouseDown;
            }
        }

        private static void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Window window)
            {
                SetIsMouseInsideWindow(window, true);
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 0x0006 是 WM_ACTIVATE
            if (msg == 0x0006)
            {
                // 获取窗口句柄对应的 Window 对象
                Window window = HwndSource.FromHwnd(hwnd)?.RootVisual as Window;
                if (window == null) return IntPtr.Zero;

                int activationState = wParam.ToInt32() & 0xFFFF;
                // 0 表示失去焦点 (点击了外部)
                if (activationState == 0)
                {
                    SetIsMouseInsideWindow(window, false);
                }
            }
            return IntPtr.Zero;
        }
    }
}

