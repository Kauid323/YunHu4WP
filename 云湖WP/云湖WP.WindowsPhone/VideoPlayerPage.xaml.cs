using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Phone.UI.Input;
using Windows.Security.Cryptography.Certificates;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Web.Http;
using Windows.Web.Http.Filters;
using 云湖WP.Api.Common;
using 云湖WP.Utils;

namespace 云湖WP
{
    public class VideoPlayerNavArgs
    {
        public string VideoUrl { get; set; }
        public string Title { get; set; }
    }

    /// <summary>
    /// 云湖WP 原生视频播放器页面 (支持防盗链 Referer 下载与流式缓冲、Metro 全屏触控与进度拖拽)
    /// </summary>
    public sealed partial class VideoPlayerPage : Page
    {
        private const string RefererUrlHttps = "https://myapp.jwznb.com";
        private const string RefererUrlHttp = "http://myapp.jwznb.com";
        private const string UserAgent = "Mozilla/5.0 (Windows Phone 8.1; ARM; Trident/7.0; Touch; rv:11.0; IEMobile/11.0; NOKIA; Lumia 930) like Gecko";

        private string _videoUrl = "";
        private string _videoTitle = "视频播放";
        private StorageFile _tempVideoFile = null;
        private IRandomAccessStream _videoStream = null;
        private CancellationTokenSource _downloadCts = null;

        private DispatcherTimer _positionTimer = null;
        private DispatcherTimer _overlayHideTimer = null;
        private Windows.System.Display.DisplayRequest _displayRequest = null;
        private bool _isDisplayRequested = false;
        private bool _isUserDraggingSlider = false;
        private bool _isOverlayVisible = true;
        private bool _isSaving = false;

        public VideoPlayerPage()
        {
            this.InitializeComponent();

            _positionTimer = new DispatcherTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(300);
            _positionTimer.Tick += PositionTimer_Tick;

            _overlayHideTimer = new DispatcherTimer();
            _overlayHideTimer.Interval = TimeSpan.FromSeconds(3.5);
            _overlayHideTimer.Tick += OverlayHideTimer_Tick;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            if (e.Parameter is VideoPlayerNavArgs)
            {
                var args = (VideoPlayerNavArgs)e.Parameter;
                _videoUrl = args.VideoUrl ?? "";
                if (!string.IsNullOrEmpty(args.Title))
                {
                    _videoTitle = args.Title;
                }
            }
            else if (e.Parameter is string)
            {
                _videoUrl = (string)e.Parameter;
            }

            TxtVideoTitle.Text = !string.IsNullOrEmpty(_videoTitle) ? _videoTitle : "视频播放";
            StartVideoLoadAndPlay();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;

            StopAndCleanupPlayback();
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            e.Handled = true;
            StopAndCleanupPlayback();
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void StopAndCleanupPlayback()
        {
            try
            {
                if (_downloadCts != null)
                {
                    _downloadCts.Cancel();
                    _downloadCts = null;
                }

                if (_positionTimer != null)
                {
                    _positionTimer.Stop();
                }

                if (_overlayHideTimer != null)
                {
                    _overlayHideTimer.Stop();
                }

                if (PlayerMediaElement != null)
                {
                    PlayerMediaElement.Stop();
                    PlayerMediaElement.Source = null;
                }

                if (_videoStream != null)
                {
                    _videoStream.Dispose();
                    _videoStream = null;
                }

                ReleaseDisplay();
            }
            catch { }
        }

        private void ActivateDisplay()
        {
            try
            {
                if (!_isDisplayRequested)
                {
                    if (_displayRequest == null)
                    {
                        _displayRequest = new Windows.System.Display.DisplayRequest();
                    }
                    _displayRequest.RequestActive();
                    _isDisplayRequested = true;
                }
            }
            catch { }
        }

        private void ReleaseDisplay()
        {
            try
            {
                if (_isDisplayRequested && _displayRequest != null)
                {
                    _displayRequest.RequestRelease();
                    _isDisplayRequested = false;
                }
            }
            catch { }
        }

        private async void StartVideoLoadAndPlay()
        {
            if (string.IsNullOrEmpty(_videoUrl))
            {
                PanelBuffering.Visibility = Visibility.Collapsed;
                PanelError.Visibility = Visibility.Visible;
                TxtError.Text = "视频播放地址为空";
                return;
            }

            PanelBuffering.Visibility = Visibility.Visible;
            PanelError.Visibility = Visibility.Collapsed;
            ControlsOverlay.Visibility = Visibility.Visible;
            _isOverlayVisible = true;
            DownloadProgressBar.Value = 0;
            TxtProgress.Text = "正在连接视频服务器...";

            _downloadCts = new CancellationTokenSource();
            var token = _downloadCts.Token;

            try
            {
                // 创建临时视频文件
                var tempFolder = ApplicationData.Current.TemporaryFolder;
                string tempName = "play_video_" + Guid.NewGuid().ToString("N") + ".mp4";
                _tempVideoFile = await tempFolder.CreateFileAsync(tempName, CreationCollisionOption.ReplaceExisting);

                // 使用携带 Referer 的 HttpClient 进行分段下载
                var filter = new HttpBaseProtocolFilter();
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.InvalidName);
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);
                filter.AllowAutoRedirect = true;

                using (var client = new HttpClient(filter))
                {
                    client.DefaultRequestHeaders.TryAppendWithoutValidation("Referer", RefererUrlHttps);
                    client.DefaultRequestHeaders.TryAppendWithoutValidation("User-Agent", UserAgent);

                    Uri uri = new Uri(_videoUrl);
                    using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new Exception("服务器返回 HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase);
                        }

                        ulong totalBytes = response.Content.Headers.ContentLength.HasValue ? response.Content.Headers.ContentLength.Value : 0;
                        ulong downloadedBytes = 0;

                        using (var inputStream = await response.Content.ReadAsInputStreamAsync())
                        using (var fileStream = await _tempVideoFile.OpenAsync(FileAccessMode.ReadWrite))
                        using (var outputStream = fileStream.GetOutputStreamAt(0))
                        {
                            byte[] buffer = new byte[64 * 1024];
                            var winrtBuffer = buffer.AsBuffer();

                            while (true)
                            {
                                if (token.IsCancellationRequested) return;

                                var readBuffer = await inputStream.ReadAsync(winrtBuffer, (uint)buffer.Length, InputStreamOptions.None);
                                if (readBuffer.Length == 0) break;

                                await outputStream.WriteAsync(readBuffer);
                                downloadedBytes += readBuffer.Length;

                                if (totalBytes > 0)
                                {
                                    double pct = (double)downloadedBytes / totalBytes * 100.0;
                                    DownloadProgressBar.Value = Math.Min(100, pct);
                                    double downMB = downloadedBytes / (1024.0 * 1024.0);
                                    double totalMB = totalBytes / (1024.0 * 1024.0);
                                    TxtProgress.Text = string.Format("正在下载视频 {0:F0}% ({1:F1} MB / {2:F1} MB)", pct, downMB, totalMB);
                                }
                                else
                                {
                                    DownloadProgressBar.IsIndeterminate = true;
                                    double downMB = downloadedBytes / (1024.0 * 1024.0);
                                    TxtProgress.Text = string.Format("正在接收视频流 ({0:F1} MB)...", downMB);
                                }
                            }

                            await outputStream.FlushAsync();
                        }
                    }
                }

                // 下载完成，交由 MediaElement 进行硬件加速播放
                TxtProgress.Text = "正在启动播放器...";
                _videoStream = await _tempVideoFile.OpenAsync(FileAccessMode.Read);
                PlayerMediaElement.SetSource(_videoStream, "video/mp4");
                PlayerMediaElement.Play();

                PanelBuffering.Visibility = Visibility.Collapsed;
                _positionTimer.Start();
                _overlayHideTimer.Start();
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) return;

                AppLogger.Log("VideoPlayerPage", "Video download failed: " + ex.Message);
                PanelBuffering.Visibility = Visibility.Collapsed;
                PanelError.Visibility = Visibility.Visible;
                TxtError.Text = "视频加载失败: " + ex.Message;
            }
        }

        private void PlayerMediaElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            PanelBuffering.Visibility = Visibility.Collapsed;
            IconPlayPause.Symbol = Symbol.Pause;
            IconCenterPlay.Symbol = Symbol.Pause;
            CenterPlayBtn.Visibility = Visibility.Collapsed;

            if (PlayerMediaElement.NaturalDuration.HasTimeSpan)
            {
                TimelineSlider.Maximum = PlayerMediaElement.NaturalDuration.TimeSpan.TotalSeconds;
            }
        }

        private void PlayerMediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            IconPlayPause.Symbol = Symbol.Play;
            IconCenterPlay.Symbol = Symbol.RepeatAll;
            CenterPlayBtn.Visibility = Visibility.Visible;
            ControlsOverlay.Visibility = Visibility.Visible;
            _isOverlayVisible = true;
            _overlayHideTimer.Stop();
        }

        private void PlayerMediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            PanelBuffering.Visibility = Visibility.Collapsed;
            PanelError.Visibility = Visibility.Visible;
            TxtError.Text = "视频解码失败: " + e.ErrorMessage;
        }

        private void PlayerMediaElement_CurrentStateChanged(object sender, RoutedEventArgs e)
        {
            switch (PlayerMediaElement.CurrentState)
            {
                case MediaElementState.Playing:
                    IconPlayPause.Symbol = Symbol.Pause;
                    IconCenterPlay.Symbol = Symbol.Pause;
                    CenterPlayBtn.Visibility = Visibility.Collapsed;
                    ActivateDisplay();
                    break;
                case MediaElementState.Paused:
                    IconPlayPause.Symbol = Symbol.Play;
                    IconCenterPlay.Symbol = Symbol.Play;
                    CenterPlayBtn.Visibility = Visibility.Visible;
                    ReleaseDisplay();
                    break;
                case MediaElementState.Stopped:
                    IconPlayPause.Symbol = Symbol.Play;
                    IconCenterPlay.Symbol = Symbol.Play;
                    CenterPlayBtn.Visibility = Visibility.Visible;
                    ReleaseDisplay();
                    break;
                case MediaElementState.Buffering:
                    PanelBuffering.Visibility = Visibility.Visible;
                    TxtProgress.Text = "正在缓冲...";
                    break;
            }
        }

        private void PositionTimer_Tick(object sender, object e)
        {
            if (!_isUserDraggingSlider && PlayerMediaElement.CurrentState == MediaElementState.Playing)
            {
                double currentSec = PlayerMediaElement.Position.TotalSeconds;
                TimelineSlider.Value = currentSec;

                string currStr = FormatTimeSpan(PlayerMediaElement.Position);
                string totalStr = PlayerMediaElement.NaturalDuration.HasTimeSpan ? FormatTimeSpan(PlayerMediaElement.NaturalDuration.TimeSpan) : "--:--";
                TxtTime.Text = string.Format("{0} / {1}", currStr, totalStr);
            }
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
            {
                return string.Format("{0:D2}:{1:D2}:{2:D2}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
            }
            return string.Format("{0:D2}:{1:D2}", ts.Minutes, ts.Seconds);
        }

        private void OverlayHideTimer_Tick(object sender, object e)
        {
            if (PlayerMediaElement.CurrentState == MediaElementState.Playing && !_isUserDraggingSlider)
            {
                ControlsOverlay.Visibility = Visibility.Collapsed;
                CenterPlayBtn.Visibility = Visibility.Collapsed;
                _isOverlayVisible = false;
                _overlayHideTimer.Stop();
            }
        }

        private void MainGrid_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _isOverlayVisible = !_isOverlayVisible;
            ControlsOverlay.Visibility = _isOverlayVisible ? Visibility.Visible : Visibility.Collapsed;

            if (_isOverlayVisible)
            {
                if (PlayerMediaElement.CurrentState == MediaElementState.Paused)
                {
                    CenterPlayBtn.Visibility = Visibility.Visible;
                }
                _overlayHideTimer.Start();
            }
            else
            {
                CenterPlayBtn.Visibility = Visibility.Collapsed;
                _overlayHideTimer.Stop();
            }
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            TogglePlayPause();
        }

        private void CenterPlayBtn_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            TogglePlayPause();
        }

        private void TogglePlayPause()
        {
            if (PlayerMediaElement.CurrentState == MediaElementState.Playing)
            {
                PlayerMediaElement.Pause();
                IconPlayPause.Symbol = Symbol.Play;
                IconCenterPlay.Symbol = Symbol.Play;
                CenterPlayBtn.Visibility = Visibility.Visible;
                _overlayHideTimer.Stop();
            }
            else
            {
                PlayerMediaElement.Play();
                IconPlayPause.Symbol = Symbol.Pause;
                IconCenterPlay.Symbol = Symbol.Pause;
                CenterPlayBtn.Visibility = Visibility.Collapsed;
                _overlayHideTimer.Start();
            }
        }

        private void BtnReplay_Click(object sender, RoutedEventArgs e)
        {
            PlayerMediaElement.Position = TimeSpan.Zero;
            PlayerMediaElement.Play();
            IconPlayPause.Symbol = Symbol.Pause;
            IconCenterPlay.Symbol = Symbol.Pause;
            CenterPlayBtn.Visibility = Visibility.Collapsed;
            _overlayHideTimer.Start();
        }

        private void TimelineSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isUserDraggingSlider)
            {
                TimeSpan target = TimeSpan.FromSeconds(e.NewValue);
                string currStr = FormatTimeSpan(target);
                string totalStr = PlayerMediaElement.NaturalDuration.HasTimeSpan ? FormatTimeSpan(PlayerMediaElement.NaturalDuration.TimeSpan) : "--:--";
                TxtTime.Text = string.Format("{0} / {1}", currStr, totalStr);
            }
        }

        private void TimelineSlider_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _isUserDraggingSlider = true;
            _overlayHideTimer.Stop();
        }

        private void TimelineSlider_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            _isUserDraggingSlider = false;
            PlayerMediaElement.Position = TimeSpan.FromSeconds(TimelineSlider.Value);
            _overlayHideTimer.Start();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            StopAndCleanupPlayback();
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private async void BtnSaveVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_isSaving) return;
            if (_tempVideoFile == null)
            {
                await ShowToastAsync("视频尚未下载完成，请稍候");
                return;
            }

            _isSaving = true;
            string saveResultMsg = null;
            try
            {
                string fileName = string.Format("yunhu_video_{0}.mp4", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                var targetFolder = KnownFolders.VideosLibrary;
                var targetFile = await targetFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);

                using (var sourceStream = await _tempVideoFile.OpenReadAsync())
                using (var destStream = await targetFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    await RandomAccessStream.CopyAsync(sourceStream, destStream);
                }

                saveResultMsg = "视频已成功保存至手机视频库";
            }
            catch (Exception ex)
            {
                saveResultMsg = "保存视频失败: " + ex.Message;
            }
            finally
            {
                _isSaving = false;
            }

            if (saveResultMsg != null)
            {
                await ShowToastAsync(saveResultMsg);
            }
        }

        private void BtnRetry_Click(object sender, RoutedEventArgs e)
        {
            StartVideoLoadAndPlay();
        }

        private void BtnCopyUrl_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_videoUrl))
            {
                Frame.Navigate(typeof(TextViewerPage), _videoUrl);
            }
        }

        private async Task ShowToastAsync(string message)
        {
            try
            {
                var dialog = new MessageDialog(message);
                await dialog.ShowAsync();
            }
            catch { }
        }
    }
}
