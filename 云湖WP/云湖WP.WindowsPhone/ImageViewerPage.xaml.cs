using System;
using System.Threading.Tasks;
using Windows.Phone.UI.Input;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
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
        private byte[] _imageBytes = null;
        private bool _isSaving = false;

        public ImageViewerPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
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
            }
            else if (e.Parameter is string)
            {
                _imageUrl = (string)e.Parameter;
            }

            LoadFullImageAsync();
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

        private async void LoadFullImageAsync()
        {
            if (string.IsNullOrEmpty(_imageUrl))
            {
                PanelError.Visibility = Visibility.Visible;
                BtnSave.Visibility = Visibility.Collapsed;
                return;
            }

            ImgProgressBar.Visibility = Visibility.Visible;
            PanelError.Visibility = Visibility.Collapsed;
            BtnSave.Visibility = Visibility.Collapsed;

            string finalUrl = _imageUrl;
            bool success = false;

            try
            {
                byte[] bytes = await ImageLoader.GetImageBytesAsync(finalUrl, force: true);
                if (bytes != null && bytes.Length > 0)
                {
                    _imageBytes = bytes;
                    var bmp = await ImageLoader.BytesToBitmapImageAsync(bytes, 0, 0);
                    if (bmp != null)
                    {
                        ImgFull.Source = bmp;
                        success = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadFullImage failed: " + ex.Message);
            }
            finally
            {
                ImgProgressBar.Visibility = Visibility.Collapsed;
            }

            if (success)
            {
                BtnSave.Visibility = Visibility.Visible;
            }
            else
            {
                PanelError.Visibility = Visibility.Visible;
                BtnSave.Visibility = Visibility.Collapsed;
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

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            LoadFullImageAsync();
        }

        private async Task ShowToastAsync(string message)
        {
            var dialog = new MessageDialog(message, "云湖");
            await dialog.ShowAsync();
        }
    }
}
