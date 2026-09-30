using System;
using System.Threading.Tasks;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Community.CreatePost;
using 云湖WP.Token;

namespace 云湖WP
{
    /// <summary>
    /// 发布动态页面 (简化版 Windows 8.1)
    /// </summary>
    public sealed partial class CreatePostPage : Page
    {
        private string _token = "";
        private int _baId = 0;
        private bool _isSubmitting = false;

        public CreatePostPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var args = e.Parameter as CreatePostNavArgs;
            if (args != null)
            {
                _baId = args.BaId;
            }

            _token = await TokenManager.GetTokenAsync();

            if (string.IsNullOrEmpty(_token))
            {
                await ShowToastAsync("请先登录");
                if (Frame.CanGoBack) Frame.GoBack();
            }
        }

        private async void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            if (_isSubmitting || string.IsNullOrEmpty(_token)) return;

            string title = TxtTitle == null || TxtTitle.Text == null ? "" : TxtTitle.Text.Trim();
            string content = "";
            if (RichContentBox != null && RichContentBox.Document != null)
            {
                var range = RichContentBox.Document.GetRange(0, 0);
                content = range.Text ?? "";
            }

            if (string.IsNullOrEmpty(title))
            {
                await ShowToastAsync("请输入标题");
                return;
            }

            if (string.IsNullOrEmpty(content))
            {
                await ShowToastAsync("请输入内容");
                return;
            }

            _isSubmitting = true;
            LoadingProgressBar.Visibility = Visibility.Visible;
            BtnSubmit.IsEnabled = false;

            try
            {
                var res = await CreatePostApi.CreatePostAsync(_token, _baId, title, content);
                if (res.IsSuccess)
                {
                    await ShowToastAsync("发布成功");
                    if (Frame.CanGoBack) Frame.GoBack();
                }
                else
                {
                    await ShowToastAsync(res.Msg ?? "发布失败");
                }
            }
            catch (Exception ex)
            {
                ShowToastAsync("发布失败: " + ex.Message);
            }
            finally
            {
                _isSubmitting = false;
                LoadingProgressBar.Visibility = Visibility.Collapsed;
                BtnSubmit.IsEnabled = true;
            }
        }

        private void RbMarkdown_Checked(object sender, RoutedEventArgs e)
        {
        }

        private void RbPlainText_Checked(object sender, RoutedEventArgs e)
        {
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame != null && Frame.CanGoBack) Frame.GoBack();
        }

        private async Task ShowToastAsync(string message)
        {
            var dialog = new MessageDialog(message, "云湖");
            await dialog.ShowAsync();
        }
    }
}
