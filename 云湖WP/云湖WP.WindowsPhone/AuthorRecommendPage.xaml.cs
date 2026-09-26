using System;
using Windows.Phone.UI.Input;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 作者推荐页面
    /// </summary>
    public sealed partial class AuthorRecommendPage : Page
    {
        public AuthorRecommendPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;
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

        private async void BtnRecommendGroup1_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await ShareHelper.HandleShareLinkAsync(
                this.Frame,
                "https://yhfx.jwznb.com/share?key=KaSJi2AyHGOg&ts=1790420305",
                "418769995",
                "糖味堂-闲聊茶馆",
                2
            );
        }
    }
}
