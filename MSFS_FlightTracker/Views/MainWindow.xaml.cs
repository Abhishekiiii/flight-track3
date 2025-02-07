﻿using ControlzEx.Theming;
using LiveCharts.Geared;
using MahApps.Metro.Controls;
using MapControl;
using MSFS_FlightTracker.Properties;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MSFS_FlightTracker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private int _maxDownload = -1;
        private SimvarsViewModel simvarVm;
        private double? lastLatitude;
        private double? lastLongitude;
        public const int CHART_TICK_INTERVAL = 2;

        public MainWindow()
        {
            // Very important we set the CacheFolder before doing anything so the MapCanvas knows where
            // to save the downloaded files to.
            TileGenerator.CacheFolder = @"ImageCache";
            TileGenerator.UserAgent = "MSFS_FlightTracker";
            TileGenerator.DownloadCountChanged += this.OnDownloadCountChanged;
            TileGenerator.DownloadError += this.OnDownloadError;

            this.DataContext = new SimvarsViewModel(this);

            InitializeComponent();
            CommandManager.AddPreviewExecutedHandler(this, this.PreviewExecuteCommand); // We're going to do some effects when zooming.

            simvarVm = (SimvarsViewModel)DataContext;
            simvarVm.SetOnTickCallback(new BaseCommand(async (p) => { await SimvarOnTickAsync(p); }));

            // Set initial values
            int theme = Settings.Default.UITheme;

            if (theme == 0)
            {
                LightThemeRadioButton.IsChecked = true;
            }
            else if (theme == 1)
            {
                DarkThemeRadioButton.IsChecked = true;
            }
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            CenterOnLatLong(44.837788, -0.579180, 3);
        }

        private async Task<Task> SimvarOnTickAsync(object param)
        {
            try
            {
                var index = (int)param;

                var latitude = simvarVm.bLatitude;
                var longitude = simvarVm.bLongitude;
                var heading = simvarVm.bHeading;
                var planeAltitude = simvarVm.bPlaneAltitude;
                var groundAltitude = simvarVm.bGroundAltitude;

                // Update plane marker
                await UpdatePlaneMarker(heading);

                if (simvarVm.bFollowMap)
                {
                    await CenterOnLatLong(latitude, longitude, tileCanvas.Zoom);
                }

                if (simvarVm.bTrackingStarted)
                {
                    // Draw circle marker and update charts
                    await DrawCircle(latitude, longitude);
                    if (index % CHART_TICK_INTERVAL == 0)
                    {
                        await UpdateCharts();
                    }

                    // No use for these yet, was thinking of using it to join lines
                    lastLatitude = latitude;
                    lastLongitude = longitude;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                simvarVm.lErrorMessages.Add("SimvarOnTick: " + ex.ToString());
            }

            return Task.CompletedTask;
        }

        protected HwndSource GetHWinSource()
        {
            return PresentationSource.FromVisual(this) as HwndSource;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            GetHWinSource().AddHook(WndProc);
            if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
            {
                oBaseSimConnectWrapper.SetWindowHandle(GetHWinSource().Handle);
            }
        }

        private IntPtr WndProc(IntPtr hWnd, int iMsg, IntPtr hWParam, IntPtr hLParam, ref bool bHandled)
        {
            if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
            {
                try
                {
                    if (iMsg == oBaseSimConnectWrapper.GetUserSimConnectWinEvent())
                    {
                        oBaseSimConnectWrapper.ReceiveSimConnectMessage();
                    }
                }
                catch
                {
                    oBaseSimConnectWrapper.Disconnect();
                }
            }

            return IntPtr.Zero;
        }

        private void OnHyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(e.Uri.AbsoluteUri); // Launch the site in the user's default browser.
        }

        private void OnDownloadCountChanged(object sender, EventArgs e)
        {
            if (this.Dispatcher.Thread != Thread.CurrentThread)
            {
                this.Dispatcher.BeginInvoke(new Action(() => this.OnDownloadCountChanged(sender, e)), null);
                return;
            }
            if (TileGenerator.DownloadCount == 0)
            {
                this.label.Visibility = Visibility.Hidden;
                this.progress.Visibility = Visibility.Hidden;
                _maxDownload = -1;
            }
            else
            {
                this.errorBar.Visibility = Visibility.Collapsed;

                if (_maxDownload < TileGenerator.DownloadCount)
                {
                    _maxDownload = TileGenerator.DownloadCount;
                }
                this.progress.Value = 100 - (TileGenerator.DownloadCount * 100.0 / _maxDownload);
                this.progress.Visibility = Visibility.Visible;
                this.label.Text = string.Format(
                    CultureInfo.CurrentUICulture,
                    "Downloading {0} item{1}",
                    TileGenerator.DownloadCount,
                    TileGenerator.DownloadCount != 1 ? 's' : ' ');
                this.label.Visibility = Visibility.Visible;
            }
        }

        private void OnDownloadError(object sender, EventArgs e)
        {
            if (this.Dispatcher.Thread != Thread.CurrentThread)
            {
                this.Dispatcher.BeginInvoke(new Action(() => this.OnDownloadError(sender, e)), null);
                return;
            }

            this.errorBar.Text = "Unable to contact the server to download map data.";
            this.errorBar.Visibility = Visibility.Visible;
        }

        private Task UpdateCharts()
        {
            foreach (var series in simvarVm.AltitudeSeries)
            {
                if (series.Title == "Plane Altitude")
                {
                    series.Values.Add(simvarVm.bPlaneAltitude);
                }
                else if (series.Title == "Ground Altitude")
                {
                    series.Values.Add(simvarVm.bGroundAltitude);
                }
                else if (series.Title == "Water")
                {
                    series.Values.Add(0.0);
                }
            }

            foreach (var series in simvarVm.SpeedSeries)
            {
                if (series.Title == "True Airspeed")
                {
                    series.Values.Add(simvarVm.bAirspeedTrue);
                }
                else if (series.Title == "Indicated Airspeed")
                {
                    series.Values.Add(simvarVm.bAirspeedIndicated);
                }
                else if (series.Title == "Ground Speed")
                {
                    series.Values.Add(simvarVm.bGroundSpeed);
                }
            }

            return Task.CompletedTask;
        }

        public Task DrawCircle(double latitude, double longitude)
        {
            var circle = new Ellipse();
            circle.Width = 5;
            circle.Height = 5;

            SolidColorBrush mySolidColorBrush = new SolidColorBrush();
            mySolidColorBrush.Color = Color.FromArgb(255, 255, 0, 0);
            circle.Fill = mySolidColorBrush;

            circle.SetValue(MapCanvas.LatitudeProperty, latitude);
            circle.SetValue(MapCanvas.LongitudeProperty, longitude);

            this.tileCanvas.Children.Add(circle);

            return Task.CompletedTask;
        }

        public void RemoveAllCircles()
        {
            tileCanvas.Children.RemoveRange(3, tileCanvas.Children.Count);
        }

        public Task CenterOnLatLong(double degLat, double degLong, int zoom = 12)
        {
            this.tileCanvas.Focus();
            this.tileCanvas.Center(degLat, degLong, zoom);

            return Task.CompletedTask;
        }

        private void OnZoomStoryboardCompleted(object sender, EventArgs e)
        {
            this.zoomGrid.Visibility = Visibility.Hidden;
            this.zoomImage.Source = null;
        }

        private void PreviewExecuteCommand(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command == NavigationCommands.DecreaseZoom)
            {
                if (this.tileCanvas.Zoom > 0) // Make sure we can actualy zoom out
                {
                    this.StartZoom("zoomOut", 1);
                }
            }
            else if (e.Command == NavigationCommands.IncreaseZoom)
            {
                if (this.tileCanvas.Zoom < TileGenerator.MaxZoom)
                {
                    this.StartZoom("zoomIn", 0.5);
                }
            }
        }

        private void StartZoom(string name, double scale)
        {
            this.zoomImage.Source = this.tileCanvas.CreateImage();
            this.zoomRectangle.Height = this.tileCanvas.ActualHeight * scale;
            this.zoomRectangle.Width = this.tileCanvas.ActualWidth * scale;

            this.zoomGrid.RenderTransform = new ScaleTransform(); // Clear the old transform
            this.zoomGrid.Visibility = Visibility.Visible;
            ((Storyboard)this.zoomGrid.FindResource(name)).Begin();
        }

        public Task UpdatePlaneMarker(double heading)
        {
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.UriSource = Utils.Utils.GetPlaneImageFileFromHeading(heading);
            image.EndInit();

            planeMarker.Source = image;

            return Task.CompletedTask;
        }

        private void tileCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            simvarVm.bFollowMap = false;
        }

        private void ShowPlaneAltitudeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (simvarVm != null && simvarVm.AltitudeSeries != null)
            {
                foreach (GLineSeries series in simvarVm.AltitudeSeries)
                {
                    if (series.Title == "Plane Altitude")
                    {
                        series.Visibility = simvarVm.bShowPlaneAltitude ? Visibility.Visible : Visibility.Hidden;
                    }
                }
            }
        }

        private void ShowGroundAltitudeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (simvarVm != null && simvarVm.AltitudeSeries != null)
            {
                foreach (GLineSeries series in simvarVm.AltitudeSeries)
                {
                    if (series.Title == "Ground Altitude")
                    {
                        series.Visibility = simvarVm.bShowGroundAltitude ? Visibility.Visible : Visibility.Hidden;
                    }
                }
            }
        }

        private void ShowWaterAltitudeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (simvarVm != null && simvarVm.AltitudeSeries != null)
            {
                foreach (GLineSeries series in simvarVm.AltitudeSeries)
                {
                    if (series.Title == "Water")
                    {
                        series.Visibility = simvarVm.bShowWaterAltitude ? Visibility.Visible : Visibility.Hidden;
                    }
                }
            }
        }

        private void ShowAirspeedTrueCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (simvarVm != null && simvarVm.SpeedSeries != null)
            {
                foreach (GLineSeries series in simvarVm.SpeedSeries)
                {
                    if (series.Title == "True Airspeed")
                    {
                        series.Visibility = simvarVm.bShowAirspeedTrue ? Visibility.Visible : Visibility.Hidden;
                    }
                }
            }
        }

        private void ShowAirspeedIndicatedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (simvarVm != null && simvarVm.SpeedSeries != null)
            {
                foreach (GLineSeries series in simvarVm.SpeedSeries)
                {
                    if (series.Title == "Indicated Airspeed")
                    {
                        series.Visibility = simvarVm.bShowAirspeedIndicated ? Visibility.Visible : Visibility.Hidden;
                    }
                }
            }
        }

        private void ShowGroundSpeedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (simvarVm != null && simvarVm.SpeedSeries != null)
            {
                foreach (GLineSeries series in simvarVm.SpeedSeries)
                {
                    if (series.Title == "Ground Speed")
                    {
                        series.Visibility = simvarVm.bShowGroundSpeed ? Visibility.Visible : Visibility.Hidden;
                    }
                }
            }
        }

        private void LightThemeRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            ThemeManager.Current.ChangeTheme(this, "Light.Olive");
            Settings.Default.UITheme = 0;
            Settings.Default.Save();
        }

        private void DarkThemeRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            ThemeManager.Current.ChangeTheme(this, "Dark.Olive");
            Settings.Default.UITheme = 1;
            Settings.Default.Save();
        }
    }
}
