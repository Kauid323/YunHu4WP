using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Phone.UI.Input;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Message;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 图片预览器页面 (纯粹全屏无顶栏，支持手势缩放与保存至手机相册)
    /// </summary>
    public sealed partial class ImageViewerPage : Page
    {
        private string _imageUrl = "";
        private string _title = "图片预览";
        private string _fallbackLetter = "";
        private byte[] _imageBytes = null;
        private bool _isSaving = false;

        public ImageViewerPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            if (e.Parameter is ImageViewerNavArgs)
            {
                var args = (ImageViewerNavArgs)e.Parameter;
                _imageUrl = args.ImageUrl ?? "";
                if (!string.IsNullOrEmpty(args.Title))
                {
                    _title = args.Title;
                }
                if (!string.IsNullOrEmpty(args.FallbackLetter))
                {
                    _fallbackLetter = args.FallbackLetter;
                }
            }
            else if (e.Parameter is string)
            {
                _imageUrl = (string)e.Parameter;
            }

            await LoadFullImageAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            e.Handled = true;
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private async Task LoadFullImageAsync(bool isRetry = false)
        {
            if (string.IsNullOrEmpty(_imageUrl))
            {
                ImgProgressBar.Visibility = Visibility.Collapsed;
                if (ImgProgressRing != null) ImgProgressRing.IsActive = false;
                if (ImgProgressRing != null) ImgProgressRing.Visibility = Visibility.Collapsed;
                PanelError.Visibility = Visibility.Collapsed;
                BtnSave.Visibility = Visibility.Collapsed;
                ImgFull.Source = null;

                if (!string.IsNullOrEmpty(_fallbackLetter))
                {
                    TxtAvatarLetter.Text = _fallbackLetter;
                    BorderAvatarLetter.Visibility = Visibility.Visible;
                }
                else
                {
                    if (TxtErrorReason != null) TxtErrorReason.Text = "图片地址为空";
                    PanelError.Visibility = Visibility.Visible;
                }
                return;
            }

            // 1. 显示加载中动画 (顶部进度条与中心旋转圈)
            ImgProgressBar.Visibility = Visibility.Visible;
            if (ImgProgressRing != null)
            {
                ImgProgressRing.IsActive = true;
                ImgProgressRing.Visibility = Visibility.Visible;
            }
            PanelError.Visibility = Visibility.Collapsed;
            BtnSave.Visibility = Visibility.Collapsed;
            BorderAvatarLetter.Visibility = Visibility.Collapsed;

            // 2. 如果是重试，清除历史失败/损坏的内存和磁盘缓存
            if (isRetry)
            {
                try
                {
                    await ImageLoader.RemoveFromCacheAsync(_imageUrl);
                    string urlCandidate0 = ImageHelper.FormatFullImageUrl(_imageUrl);
                    if (!string.IsNullOrEmpty(urlCandidate0)) await ImageLoader.RemoveFromCacheAsync(urlCandidate0);
                    string urlCandidate1 = ImageHelper.FormatQiniuUrl(_imageUrl, 0, 0);
                    if (!string.IsNullOrEmpty(urlCandidate1)) await ImageLoader.RemoveFromCacheAsync(urlCandidate1);
                }
                catch { }
            }

            bool success = false;
            string lastError = "无法获取图片数据";

            // 3. 构建候选下载 URL 列表 (依次尝试：高质转码 JPEG、原七牛直链、原始输入直链)
            var candidateUrls = new List<string>();
            string formattedFull = ImageHelper.FormatFullImageUrl(_imageUrl);
            if (!string.IsNullOrEmpty(formattedFull) && !candidateUrls.Contains(formattedFull))
            {
                candidateUrls.Add(formattedFull);
            }
            string formattedRaw = ImageHelper.FormatQiniuUrl(_imageUrl, 0, 0);
            if (!string.IsNullOrEmpty(formattedRaw) && !candidateUrls.Contains(formattedRaw))
            {
                candidateUrls.Add(formattedRaw);
            }
            if (!string.IsNullOrEmpty(_imageUrl) && !candidateUrls.Contains(_imageUrl))
            {
                candidateUrls.Add(_imageUrl);
            }

            foreach (var testUrl in candidateUrls)
            {
                try
                {
                    AppLogger.Log("ImageViewer", "正在加载大图: " + testUrl);
                    byte[] bytes = await ImageLoader.GetImageBytesAsync(testUrl, force: true);
                    if (bytes != null && bytes.Length > 0)
                    {
                        var bmp = await ImageLoader.BytesToBitmapImageAsync(bytes, 0, 0);
                        if (bmp != null)
                        {
                            _imageBytes = bytes;
                            ImgFull.Source = bmp;
                            success = true;
                            AppLogger.Log("ImageViewer", string.Format("大图加载成功: {0} 字节", bytes.Length));
                            break;
                        }
                        else
                        {
                            lastError = "图片格式暂不支持或解码失败";
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    AppLogger.Log("ImageViewer", "尝试加载失败: " + testUrl + ", 错误: " + ex.Message);
                }
            }

            // 4. 隐藏加载器
            ImgProgressBar.Visibility = Visibility.Collapsed;
            if (ImgProgressRing != null)
            {
                ImgProgressRing.IsActive = false;
                ImgProgressRing.Visibility = Visibility.Collapsed;
            }

            if (success)
            {
                BorderAvatarLetter.Visibility = Visibility.Collapsed;
                BtnSave.Visibility = Visibility.Visible;
                PanelError.Visibility = Visibility.Collapsed;
                UpdateImageContainerSize();
                if (ImgScrollViewer != null)
                {
                    ImgScrollViewer.ChangeView(0, 0, 1.0f);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(_fallbackLetter))
                {
                    TxtAvatarLetter.Text = _fallbackLetter;
                    BorderAvatarLetter.Visibility = Visibility.Visible;
                    PanelError.Visibility = Visibility.Collapsed;
                }
                else
                {
                    if (TxtErrorReason != null)
                    {
                        TxtErrorReason.Text = "图片加载失败 (" + lastError + ")，请点击重试";
                    }
                    PanelError.Visibility = Visibility.Visible;
                }
                BtnSave.Visibility = Visibility.Collapsed;
            }
        }

        private void ImgScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateImageContainerSize();
        }

        private void UpdateImageContainerSize()
        {
            if (ImgScrollViewer == null || ImageContainer == null || ImgFull == null) return;

            double vw = ImgScrollViewer.ActualWidth > 0 ? ImgScrollViewer.ActualWidth : Window.Current.Bounds.Width;
            double vh = ImgScrollViewer.ActualHeight > 0 ? ImgScrollViewer.ActualHeight : Window.Current.Bounds.Height;

            if (vw > 0 && vh > 0)
            {
                ImageContainer.Width = vw;
                ImageContainer.Height = vh;
                ImgFull.Width = vw;
                ImgFull.Height = vh;
            }
        }

        private void ImgScrollViewer_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (ImgScrollViewer == null) return;

            if (ImgScrollViewer.ZoomFactor > 1.05f)
            {
                // 双击缩小恢复至全屏自适应
                ImgScrollViewer.ChangeView(null, null, 1.0f);
            }
            else
            {
                // 双击放大 2.5 倍并居中到双击位置
                var pos = e.GetPosition(ImageContainer);
                ImgScrollViewer.ChangeView(pos.X, pos.Y, 2.5f);
            }
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_imageBytes == null || _imageBytes.Length == 0 || _isSaving)
            {
                return;
            }

            _isSaving = true;
            BtnSave.IsEnabled = false;
            string notifyMsg = null;

            try
            {
                // 检测文件格式后缀
                string ext = ".jpg";
                if (_imageBytes.Length > 3)
                {
                    if (_imageBytes[0] == 0x89 && _imageBytes[1] == 0x50 && _imageBytes[2] == 0x4E && _imageBytes[3] == 0x47)
                    {
                        ext = ".png";
                    }
                    else if (_imageBytes[0] == 0x47 && _imageBytes[1] == 0x49 && _imageBytes[2] == 0x46)
                    {
                        ext = ".gif";
                    }
                }

                string fileName = string.Format("Yunhu_{0:yyyyMMdd_HHmmss}_{1}{2}", DateTime.Now, new Random().Next(100, 999), ext);

                StorageFolder targetFolder = KnownFolders.SavedPictures;
                if (targetFolder == null)
                {
                    targetFolder = KnownFolders.PicturesLibrary;
                }

                var file = await targetFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                await FileIO.WriteBytesAsync(file, _imageBytes);

                notifyMsg = "图片已保存至手机相册 (Saved Pictures)";
            }
            catch (Exception ex)
            {
                notifyMsg = "保存失败: " + ex.Message;
            }
            finally
            {
                _isSaving = false;
                BtnSave.IsEnabled = true;
            }

            if (notifyMsg != null)
            {
                await ShowToastAsync(notifyMsg);
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private async void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            if (BtnReload != null)
            {
                BtnReload.IsEnabled = false;
                BtnReload.Content = "正在重试加载...";
            }

            await LoadFullImageAsync(isRetry: true);

            if (BtnReload != null)
            {
                BtnReload.IsEnabled = true;
                BtnReload.Content = "重新加载图片";
            }
        }

        private async Task ShowToastAsync(string message)
        {
            var dialog = new MessageDialog(message, "云湖");
            await dialog.ShowAsync();
        }
    }
}
