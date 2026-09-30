using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Common;
using 云湖WP.Api.Community;
using 云湖WP.Api.Community.Board;
using 云湖WP.Api.Community.PostDetail;
using 云湖WP.Token;
using 云湖WP.Utils;
using Windows.UI;
using Windows.Storage.Streams;

namespace 云湖WP
{
    /// <summary>
    /// 社区动态详情页面 (简化版 Windows 8.1)
    /// </summary>
    public sealed partial class PostDetailPage : Page
    {
        private string _token = "";
        private long _postId = 0;
        private CommunityPostItem _currentPost;
        private BoardInfoItem _currentBoard;
        private ObservableCollection<CommunityCommentItem> _commentList = new ObservableCollection<CommunityCommentItem>();
        private bool _isLoading = false;

        public PostDetailPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            CommentListView.ItemsSource = _commentList;

            var args = e.Parameter as PostDetailNavigationArgs;
            if (args != null)
            {
                _postId = args.PostId;
                _token = args.Token;
                if (args.InitialPost != null)
                {
                    _currentPost = args.InitialPost;
                    RenderPost(_currentPost);
                }
            }
            else if (e.Parameter is long)
            {
                _postId = (long)e.Parameter;
            }

            if (string.IsNullOrEmpty(_token))
            {
                _token = await TokenManager.GetTokenAsync();
            }

            await LoadDetailAsync();
        }

        private async Task LoadDetailAsync()
        {
            if (_postId <= 0 || _isLoading || string.IsNullOrEmpty(_token)) return;
            _isLoading = true;
            LoadingProgressBar.Visibility = Visibility.Visible;

            try
            {
                var res = await PostDetailApi.GetPostDetailAsync(_token, _postId);
                if (res.IsSuccess && res.Post != null)
                {
                    _currentPost = res.Post;
                    _currentBoard = res.Board;
                    RenderPost(_currentPost);
                }
                else
                {
                    await ShowToastAsync(res.Msg ?? "加载失败");
                }
            }
            catch (Exception ex)
            {
                ShowToastAsync("加载失败: " + ex.Message);
            }
            finally
            {
                LoadingProgressBar.Visibility = Visibility.Collapsed;
                _isLoading = false;
            }
        }

        private void RenderPost(CommunityPostItem post)
        {
            DetailPanel.Visibility = Visibility.Visible;

            if (TxtTitle != null) TxtTitle.Text = post.Title;
            if (TxtContent != null) TxtContent.Text = post.Content;
            if (TxtAuthor != null) TxtAuthor.Text = post.SenderNickname;
            if (TxtTime != null) TxtTime.Text = post.CreateTimeText;
            if (TxtBoard != null && _currentBoard != null) TxtBoard.Text = "板块: " + _currentBoard.Name;
            if (TxtLikeNum != null) TxtLikeNum.Text = post.LikeNum.ToString();
            if (TxtCommentNum != null) TxtCommentNum.Text = post.CommentNum.ToString();
            if (TxtAmountNum != null) TxtAmountNum.Text = post.AmountText;

            // 加载头像
            if (ImgAvatar != null && !string.IsNullOrEmpty(post.SenderAvatar))
            {
                LoadAvatarAsync(ImgAvatar, post.SenderAvatar);
            }
        }

        private async Task LoadCommentsAsync()
        {
            if (_postId <= 0 || _isLoading || string.IsNullOrEmpty(_token)) return;

            try
            {
                var res = await PostDetailApi.GetCommentListAsync(_token, _postId);
                if (res.IsSuccess && res.Comments != null)
                {
                    _commentList.Clear();
                    foreach (var comment in res.Comments)
                    {
                        _commentList.Add(comment);
                    }
                }
            }
            catch { }
        }

        private async void LoadAvatarAsync(Image img, string avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl)) return;

            try
            {
                var bytes = await ImageLoader.GetImageBytesAsync(ImageHelper.FormatQiniuUrl(avatarUrl, 96, 96));
                if (bytes != null && bytes.Length > 0)
                {
                    var bmp = new BitmapImage();
                    using (var stream = new InMemoryRandomAccessStream())
                    {
                        await stream.WriteAsync(bytes.AsBuffer());
                        stream.Seek(0);
                        await bmp.SetSourceAsync(stream);
                    }
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        img.Source = bmp;
                    });
                }
            }
            catch { }
        }

        private async void SendComment_Click(object sender, RoutedEventArgs e)
        {
            if (TxtCommentInput == null || string.IsNullOrWhiteSpace(TxtCommentInput.Text)) return;
            if (_isLoading || string.IsNullOrEmpty(_token)) return;

            _isLoading = true;
            try
            {
                var res = await PostDetailApi.SendCommentAsync(_token, _postId, TxtCommentInput.Text);
                if (res.IsSuccess)
                {
                    TxtCommentInput.Text = "";
                    await LoadCommentsAsync();
                    await ShowToastAsync("评论成功");
                }
                else
                {
                    await ShowToastAsync(res.Msg ?? "评论失败");
                }
            }
            catch (Exception ex)
            {
                            var msg = "评论失败: " + ex.Message;
                            ShowToastAsync(msg);
                        }
                        finally
                        {
                            _isLoading = false;
                        }
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
