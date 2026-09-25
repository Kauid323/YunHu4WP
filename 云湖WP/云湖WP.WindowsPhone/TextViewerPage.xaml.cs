using System;
using Windows.Phone.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace 云湖WP
{
    /// <summary>
    /// 独立全屏文本查看与复制界面 (解决 WP8.1 小输入框不好选中复制、Toast 无法复制的痛点)
    /// </summary>
    public sealed partial class TextViewerPage : Page
    {
        public TextViewerPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            string content = e.Parameter as string;
            if (content != null)
            {
                TxtMainContent.Text = content;
            }
            else
            {
                TxtMainContent.Text = "";
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

        private void AppBarBtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            TxtMainContent.Focus(FocusState.Programmatic);
            TxtMainContent.SelectAll();
        }

        private void AppBarBtnClear_Click(object sender, RoutedEventArgs e)
        {
            TxtMainContent.Text = "";
        }
    }
}
