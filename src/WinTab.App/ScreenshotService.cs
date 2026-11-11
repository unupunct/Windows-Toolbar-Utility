using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
// (Shapes are referenced with their full name below.)
using Forms = System.Windows.Forms;
using Brushes = System.Windows.Media.Brushes;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Rectangle = System.Drawing.Rectangle;

namespace WinTab.App;

/// <summary>
/// Region screenshot: freezes the screen, lets the person drag a rectangle, then
/// copies the crop to the clipboard and saves it in Pictures\Screenshots.
/// </summary>
internal static class ScreenshotService
{
    public static string SaveFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    public static void CaptureRegion(Action<string> done)
    {
        Rectangle bounds = Forms.SystemInformation.VirtualScreen;
        var shot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(shot))
        {
            g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
        }
        var window = new RegionWindow(shot, bounds, path =>
        {
            if (path != null) done(path);
        });
        window.Show();
    }

    public static string Save(Bitmap bitmap)
    {
        Directory.CreateDirectory(SaveFolder);
        string path = Path.Combine(SaveFolder, $"WinTab {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        IntPtr handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    private sealed class RegionWindow : Window
    {
        private readonly Bitmap _shot;
        private readonly Rectangle _bounds;
        private readonly Action<string?> _done;
        private readonly Canvas _canvas = new();
        private System.Windows.Point _start;
        private bool _dragging;
        private bool _finished;

        private readonly System.Windows.Shapes.Path _dim = new() { Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 0, 0, 0)) };
        private readonly System.Windows.Shapes.Rectangle _frame = new()
        {
            Stroke = UiKit.Accent,
            StrokeThickness = 1.5,
            Visibility = Visibility.Collapsed,
        };
        private readonly TextBlock _hint = new()
        {
            Text = "Drag to select an area. Esc cancels, Enter saves the whole screen.",
            Foreground = Brushes.White,
            FontSize = 14,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(170, 0, 0, 0)),
            Padding = new Thickness(10, 5, 10, 5),
        };

        public RegionWindow(Bitmap shot, Rectangle bounds, Action<string?> done)
        {
            _shot = shot;
            _bounds = bounds;
            _done = done;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Cursor = Cursors.Cross;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = Math.Max(100, bounds.Width);
            Height = Math.Max(100, bounds.Height);

            var image = new System.Windows.Controls.Image { Source = ToBitmapSource(shot), Stretch = Stretch.Fill };
            var grid = new Grid();
            grid.Children.Add(image);
            grid.Children.Add(_dim);
            _canvas.Background = Brushes.Transparent;
            _canvas.Children.Add(_frame);
            _canvas.Children.Add(_hint);
            Canvas.SetLeft(_hint, 24);
            Canvas.SetTop(_hint, 24);
            grid.Children.Add(_canvas);
            Content = grid;

            SourceInitialized += (_, _) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                Native.SetWindowPos(hwnd, Native.HwndTopmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height, Native.SwpShowWindow);
            };
            Loaded += (_, _) =>
            {
                UpdateDim(Rect.Empty);
                Activate();
                Focus();
            };
            SizeChanged += (_, _) => UpdateDim(CurrentSelection());
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) Finish(null);
                else if (e.Key == Key.Enter) Finish(SaveCrop(new Rectangle(0, 0, _shot.Width, _shot.Height)));
            };
            Closed += (_, _) => _shot.Dispose();
        }

        private Rect CurrentSelection()
        {
            if (!_dragging && _frame.Visibility != Visibility.Visible) return Rect.Empty;
            double x = Canvas.GetLeft(_frame), y = Canvas.GetTop(_frame);
            return new Rect(x, y, _frame.Width, _frame.Height);
        }

        private void UpdateDim(Rect selection)
        {
            var all = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
            _dim.Data = selection.IsEmpty || selection.Width < 1 || selection.Height < 1
                ? all
                : new CombinedGeometry(GeometryCombineMode.Exclude, all, new RectangleGeometry(selection));
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            _start = e.GetPosition(_canvas);
            _dragging = true;
            _hint.Visibility = Visibility.Collapsed;
            _frame.Visibility = Visibility.Visible;
            Canvas.SetLeft(_frame, _start.X);
            Canvas.SetTop(_frame, _start.Y);
            _frame.Width = 0;
            _frame.Height = 0;
            CaptureMouse();
        }

        private void OnMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_dragging) return;
            var now = e.GetPosition(_canvas);
            double x = Math.Min(_start.X, now.X), y = Math.Min(_start.Y, now.Y);
            double w = Math.Abs(now.X - _start.X), h = Math.Abs(now.Y - _start.Y);
            Canvas.SetLeft(_frame, x);
            Canvas.SetTop(_frame, y);
            _frame.Width = w;
            _frame.Height = h;
            UpdateDim(new Rect(x, y, w, h));
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
            double scaleX = _shot.Width / Math.Max(1, ActualWidth);
            double scaleY = _shot.Height / Math.Max(1, ActualHeight);
            var crop = new Rectangle(
                (int)Math.Round(Canvas.GetLeft(_frame) * scaleX),
                (int)Math.Round(Canvas.GetTop(_frame) * scaleY),
                (int)Math.Round(_frame.Width * scaleX),
                (int)Math.Round(_frame.Height * scaleY));
            if (crop.Width < 4 || crop.Height < 4)
            {
                _frame.Visibility = Visibility.Collapsed;
                UpdateDim(Rect.Empty);
                return;
            }
            Finish(SaveCrop(crop));
        }

        private string? SaveCrop(Rectangle crop)
        {
            crop.Intersect(new Rectangle(0, 0, _shot.Width, _shot.Height));
            if (crop.Width < 1 || crop.Height < 1) return null;
            using var cropped = _shot.Clone(crop, PixelFormat.Format32bppArgb);
            string path = Save(cropped);
            try
            {
                System.Windows.Clipboard.SetImage(ToBitmapSource(cropped));
            }
            catch
            {
                // The clipboard can be busy; the saved file is still there.
            }
            return path;
        }

        private void Finish(string? path)
        {
            if (_finished) return;
            _finished = true;
            Close();
            _done(path);
        }
    }
}
