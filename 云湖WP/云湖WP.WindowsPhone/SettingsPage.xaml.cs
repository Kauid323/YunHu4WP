using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Phone.UI.Input;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
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
            ToggleNotification.IsOn = NotificationHelper.IsNotificationEnabled;
            ToggleForegroundNotification.IsOn = NotificationHelper.NotifyInForeground;
            ToggleDisableImages.IsOn = ImageLoader.DisableAllImages;
            SliderThreads.Value = ImageLoader.MaxConcurrentLoads;
            TxtThreadCount.Text = string.Format("{0} 线程", ImageLoader.MaxConcurrentLoads);

            int defaultPageIdx = AppSettings.DefaultStartupPageIndex;
            if (defaultPageIdx >= 0 && defaultPageIdx <= 3)
            {
                CmbDefaultStartupPage.SelectedIndex = defaultPageIdx;
            }
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

        private void CmbDefaultStartupPage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbDefaultStartupPage == null) return;
            int idx = CmbDefaultStartupPage.SelectedIndex;
            if (idx >= 0 && idx <= 3)
            {
                AppSettings.DefaultStartupPageIndex = idx;
            }
        }

        private async void BtnCanaryGroup_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await ShareHelper.HandleShareLinkAsync(
                this.Frame,
                "https://yhfx.jwznb.com/share?key=HVG4F1K2K3W3&ts=1790420280",
                "325134750",
                "云湖金丝雀最新构建(go8发电频道）",
                2
            );
        }

        private async void BtnFavoriteGroup_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await ShareHelper.HandleShareLinkAsync(
                this.Frame,
                "https://yhfx.jwznb.com/share?key=klUt2IRmeLck&ts=1790420295",
                "979377289",
                "假的全员群",
                2
            );
        }

        private void BtnAuthorRecommend_Tapped(object sender, TappedRoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(AuthorRecommendPage));
        }

        private async void BtnGithub_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/Kauid323/Yunhu4WP"));
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

        private void ToggleNotification_Toggled(object sender, RoutedEventArgs e)
        {
            if (ToggleNotification == null) return;
            NotificationHelper.IsNotificationEnabled = ToggleNotification.IsOn;
        }

        private void ToggleForegroundNotification_Toggled(object sender, RoutedEventArgs e)
        {
            if (ToggleForegroundNotification == null) return;
            NotificationHelper.NotifyInForeground = ToggleForegroundNotification.IsOn;
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
