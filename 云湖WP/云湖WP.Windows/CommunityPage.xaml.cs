using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Common;
using 云湖WP.Api.Community;
using 云湖WP.Api.Community.PostDetail;
using 云湖WP.Token;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 云湖WP 社区页面 (适配 Windows 8.1 风格，复用 Shared 层 API)
    /// </summary>
    public sealed partial class CommunityPage : Page
    {
        private string _token = "";
        private ObservableCollection<CommunityPostItem> _postList = new ObservableCollection<CommunityPostItem>();
        private string _currentFilter = "latest"; // "latest" 或 "hot"
        private int _page = 1;
        private bool _hasMore = true;
        private bool _isLoading = false;
        private ScrollViewer _scrollViewer;

        public CommunityPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _token = await TokenManager.GetTokenAsync();
            PostsListView.ItemsSource = _postList;

            if (string.IsNullOrEmpty(_token))
            {
                await ShowToastAsync("请先登录");
                return;
            }

            await LoadPostsAsync(isRefresh: true);
        }

        private void PostsListView_Loaded(object sender, RoutedEventArgs e)
        {
            if (PostsListView != null && _scrollViewer == null)
            {
                _scrollViewer = FindVisualChild<ScrollViewer>(PostsListView);
                if (_scrollViewer != null)
                {
                    _scrollViewer.ViewChanged += ScrollViewer_ViewChanged;
                }
            }
        }

        private async void ScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (e != null && e.IsIntermediate) return;
            if (_scrollViewer == null || _isLoading || !_hasMore || string.IsNullOrEmpty(_token)) return;

            if (_scrollViewer.ScrollableHeight > 0 &&
                _scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - 250)
            {
                await LoadPostsAsync(isRefresh: false);
            }
        }

        private T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            if (obj == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child is T)
                {
                    return (T)child;
                }
                T childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }

        private async Task LoadPostsAsync(bool isRefresh)
        {
            if (string.IsNullOrEmpty(_token) || _isLoading) return;
            if (!isRefresh && !_hasMore) return;

            _isLoading = true;
            if (LoadingProgressBar != null) LoadingProgressBar.Visibility = Visibility.Visible;

            int targetPage = isRefresh ? 1 : (_page + 1);
            string errMsg = null;

            try
            {
                CommunityPostListResult res;
                if (_currentFilter == "hot")
                {
                    res = await CommunityApi.GetRecommendPostListAsync(_token, targetPage, 20);
                }
                else
                {
                    res = await CommunityApi.GetPostListAsync(_token, typ: 4, baId: 0, page: targetPage, size: 20);
                }

                if (res.IsSuccess && res.Posts != null)
                {
                    if (isRefresh)
                    {
                        _page = 1;
                        _postList.Clear();
                    }
                    else
                    {
                        _page = targetPage;
                    }

                    _hasMore = res.Posts.Count >= 20;

                    foreach (var post in res.Posts)
                    {
                        _postList.Add(post);
                    }

                    if (EmptyPanel != null)
                    {
                        EmptyPanel.Visibility = _postList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    }

                    PreloadAvatars(res.Posts);
                }
                else
                {
                    if (!isRefresh)
                    {
                        _hasMore = false;
                    }
                    if (_postList.Count == 0 && EmptyPanel != null)
                    {
                        EmptyPanel.Visibility = Visibility.Visible;
                    }
                    if (!res.IsSuccess && !string.IsNullOrEmpty(res.Msg))
                    {
                        errMsg = res.Msg;
                    }
                }
            }
            catch (Exception ex)
            {
                errMsg = "加载动态失败: " + ex.Message;
            }
            finally
            {
                if (LoadingProgressBar != null) LoadingProgressBar.Visibility = Visibility.Collapsed;
                _isLoading = false;
            }

            if (!isRefresh && errMsg != null)
            {
                await ShowToastAsync(errMsg);
            }
        }

        private void PreloadAvatars(IEnumerable<CommunityPostItem> posts)
        {
            if (posts == null || ImageLoader.DisableAllImages) return;

            var list = new List<CommunityPostItem>(posts);

            Task.Run(async () =>
            {
                await Task.Delay(100);

                foreach (var post in list)
                {
                    if (ImageLoader.DisableAllImages) break;
                    if (post == null || string.IsNullOrEmpty(post.SenderAvatar) || post.AvatarBitmap != null) continue;

                    var cur = post;
                    string finalUrl = ImageHelper.FormatQiniuUrl(cur.SenderAvatar, 96, 96);

                    try
                    {
                        byte[] bytes = await ImageLoader.GetImageBytesAsync(finalUrl);
                        if (bytes != null && bytes.Length > 0)
                        {
                            await Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
                            {
                                var bmp = await ImageLoader.BytesToBitmapImageAsync(bytes, 96, 96);
                                if (bmp != null)
                                {
                                    cur.AvatarBitmap = bmp;
                                }
                            });
                        }
                    }
                    catch { }

                    await Task.Delay(20);
                }
            });
        }

        private void PostsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var post = e.ClickedItem as CommunityPostItem;
            if (post == null) return;

            var args = new PostDetailNavigationArgs
            {
                PostId = post.Id,
                Token = _token
            };
            Frame.Navigate(typeof(PostDetailPage), args);
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadPostsAsync(isRefresh: true);
            await ShowToastAsync("动态已刷新");
        }

        private void FilterLatest_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFilter == "latest") return;
            _currentFilter = "latest";
                    if (FlyoutItemLatest != null) FlyoutItemLatest.Label = "最新文章 (当前)";
                    if (FlyoutItemHot != null) FlyoutItemHot.Label = "热门推荐";
            LoadPostsAsync(isRefresh: true);
        }

        private void FilterHot_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFilter == "hot") return;
            _currentFilter = "hot";
                    if (FlyoutItemLatest != null) FlyoutItemLatest.Label = "最新文章";
                    if (FlyoutItemHot != null) FlyoutItemHot.Label = "热门推荐 (当前)";
            LoadPostsAsync(isRefresh: true);
        }

        private void AppBarBtnNewPost_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(CreatePostPage));
        }

        private async Task ShowToastAsync(string message)
        {
            var dialog = new MessageDialog(message, "云湖");
            await dialog.ShowAsync();
        }
    }
}
