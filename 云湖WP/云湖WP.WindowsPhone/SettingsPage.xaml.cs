using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Phone.UI.Input;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 设置页面 (Windows Phone 8.1 Metro 风格)
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            // 动态读取 Package.appxmanifest 中的 Identity Version
            LoadAppVersion();

            // 初始化当前设置状态
            ToggleDisableImages.IsOn = ImageLoader.DisableAllImages;
            SliderThreads.Value = ImageLoader.MaxConcurrentLoads;
            TxtThreadCount.Text = string.Format("{0} 线程", ImageLoader.MaxConcurrentLoads);
        }

        private void LoadAppVersion()
        {
            try
            {
                var version = Package.Current.Id.Version;
                TxtAppVersion.Text = string.Format("版本 {0}.{1}.{2}.{3} (WinRT Universal)", version.Major, version.Minor, version.Build, version.Revision);
            }
            catch
            {
                TxtAppVersion.Text = "版本 1.0.0.0 (WinRT Universal)";
            }
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

        private void ToggleDisableImages_Toggled(object sender, RoutedEventArgs e)
        {
            if (ToggleDisableImages == null) return;
            ImageLoader.DisableAllImages = ToggleDisableImages.IsOn;
        }

        private void SliderThreads_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (TxtThreadCount == null) return;

            int val = (int)Math.Round(e.NewValue);
            ImageLoader.MaxConcurrentLoads = val;
            TxtThreadCount.Text = string.Format("{0} 线程", val);
        }

        private async void BtnClearCache_Click(object sender, RoutedEventArgs e)
        {
            string errorMsg = null;
            try
            {
                await ImageLoader.ClearCacheAsync();
            }
            catch (Exception ex)
            {
                errorMsg = "清理缓存失败: " + ex.Message;
            }

            if (errorMsg != null)
            {
                await ShowToastAsync(errorMsg);
            }
            else
            {
                await ShowToastAsync("图片与头像本地缓存已成功清理！");
            }
        }

        private async Task ShowToastAsync(string message)
        {
            var dialog = new MessageDialog(message, "云湖");
            await dialog.ShowAsync();
        }
    }
}
