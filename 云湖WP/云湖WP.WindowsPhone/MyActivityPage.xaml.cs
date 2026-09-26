using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.Phone.UI.Input;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Community;
using 云湖WP.Api.Community.Board;
using 云湖WP.Api.Community.CreatePost;
using 云湖WP.Api.Community.MyActivity;
using 云湖WP.Api.Community.PostDetail;
using 云湖WP.Token;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 动态管理页（我的动态 / 我的板块 / 我的关注 / 我的收藏）
    /// </summary>
    public sealed partial class MyActivityPage : Page
    {
        private string _token = "";
        private string _userId = "";

        // 我的动态
        private ObservableCollection<CommunityPostItem> _myPosts = new ObservableCollection<CommunityPostItem>();
        private int _myPostsPage = 1;
        private bool _myPostsLoading = false;
        private bool _myPostsHasMore = true;

        // 我的板块
        private ObservableCollection<MyBoardDisplayItem> _myBoards = new ObservableCollection<MyBoardDisplayItem>();

        // 我的关注
        private ObservableCollection<FollowingBoardDisplayItem> _myFollows = new ObservableCollection<FollowingBoardDisplayItem>();
        private int _myFollowsPage = 1;
        private bool _myFollowsLoading = false;
        private bool _myFollowsHasMore = true;

        // 我的收藏
        private ObservableCollection<CommunityPostItem> _myCollects = new ObservableCollection<CommunityPostItem>();
        private int _myCollectsPage = 1;
        private bool _myCollectsLoading = false;
        private bool _myCollectsHasMore = true;

        // 各 Tab 是否已加载过
        private bool _boardsLoaded = false;
        private bool _followsLoaded = false;
        private bool _collectsLoaded = false;

        public MyActivityPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            MyPostsListView.ItemsSource = _myPosts;
            MyBoardsListView.ItemsSource = _myBoards;
            MyFollowListView.ItemsSource = _myFollows;
            MyCollectListView.ItemsSource = _myCollects;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            _token = await TokenManager.GetTokenAsync();
            // 获取当前用户 ID
            var selfInfo = await 云湖WP.Api.User.UserApi.GetSelfInfoAsync(_token);
            if (selfInfo != null && selfInfo.IsSuccess)
            {
                _userId = selfInfo.Id ?? "";
            }

            await LoadMyPostsAsync(reset: true);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            e.Handled = true;
            if (Frame.CanGoBack) Frame.GoBack();
        }

        // ─── Tab 切换 ────────────────────────────────────────
        private async void ActivityPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = ActivityPivot.SelectedIndex;
            if (idx == 1 && !_boardsLoaded)
            {
                _boardsLoaded = true;
                await LoadMyBoardsAsync();
            }
            else if (idx == 2 && !_followsLoaded)
            {
                _followsLoaded = true;
                await LoadMyFollowsAsync(reset: true);
            }
            else if (idx == 3 && !_collectsLoaded)
            {
                _collectsLoaded = true;
                await LoadMyCollectsAsync(reset: true);
            }
        }

        // ─── 我的动态 ────────────────────────────────────────
        private void MyPostsListView_Loaded(object sender, RoutedEventArgs e)
        {
            var sv = MyPostsListView.GetScrollViewer();
            if (sv != null)
            {
                sv.ViewChanged += async (s, args) =>
                {
                    if (args.IsIntermediate) return;
                    if (sv.VerticalOffset >= sv.ScrollableHeight - 200 && _myPostsHasMore && !_myPostsLoading)
                    {
                        await LoadMyPostsAsync();
                    }
                };
            }
        }

        private async Task LoadMyPostsAsync(bool reset = false)
        {
            if (_myPostsLoading) return;
            _myPostsLoading = true;

            if (reset)
            {
                _myPostsPage = 1;
                _myPostsHasMore = true;
                _myPosts.Clear();
                MyPostsProgressBar.Visibility = Visibility.Visible;
            }
            else
            {
                if (MyPostsLoadMoreBar != null) MyPostsLoadMoreBar.Visibility = Visibility.Visible;
            }

            try
            {
                var result = await MyActivityApi.GetMyPostListAsync(_token, _myPostsPage, 20);
                if (result != null && result.IsSuccess)
                {
                    foreach (var post in result.Posts)
                    {
                        _myPosts.Add(post);
                        PreloadPostAvatar(post);
                    }
                    _myPostsHasMore = result.Posts.Count >= 20;
                    _myPostsPage++;
                }
                else
                {
                    _myPostsHasMore = false;
                }
            }
            catch { _myPostsHasMore = false; }
            finally
            {
                _myPostsLoading = false;
                MyPostsProgressBar.Visibility = Visibility.Collapsed;
                if (MyPostsLoadMoreBar != null) MyPostsLoadMoreBar.Visibility = Visibility.Collapsed;
                if (MyPostsNoMoreText != null)
                    MyPostsNoMoreText.Visibility = !_myPostsHasMore && _myPosts.Count > 0
                        ? Visibility.Visible : Visibility.Collapsed;
                EmptyMyPostsPanel.Visibility = _myPosts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void MyPostsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var post = e.ClickedItem as CommunityPostItem;
            if (post == null) return;

            if (post.IsDraft)
            {
                // 草稿 → 进入发动态页（编辑模式）
                var args = new CreatePostNavArgs
                {
                    BaId = post.BaId,
                    BoardName = "",
                    InitialTitle = post.Title,
                    InitialContent = post.Content,
                    InitialContentType = post.ContentType,
                    DraftId = 0
                };
                Frame.Navigate(typeof(CreatePostPage), args);
            }
            else
            {
                // 正式动态 → 进入动态详情
                var navArgs = new PostDetailNavigationArgs
                {
                    PostId = post.Id,
                    InitialPost = post,
                    Token = _token
                };
                Frame.Navigate(typeof(PostDetailPage), navArgs);
            }
        }

        // ─── 我的板块 ────────────────────────────────────────
        private void MyBoardsListView_Loaded(object sender, RoutedEventArgs e) { }

        private async Task LoadMyBoardsAsync()
        {
            if (string.IsNullOrEmpty(_userId)) return;
            MyBoardsProgressBar.Visibility = Visibility.Visible;
            _myBoards.Clear();
            try
            {
                var result = await MyActivityApi.GetMyBoardListAsync(_token, _userId);
                if (result != null && result.IsSuccess)
                {
                    foreach (var b in result.Boards)
                    {
                        var di = new MyBoardDisplayItem { Id = b.Id, Name = b.Name, AvatarUrl = b.Avatar };
                        _myBoards.Add(di);
                        PreloadBoardAvatar(di);
                    }
                }
            }
            catch { }
            finally
            {
                MyBoardsProgressBar.Visibility = Visibility.Collapsed;
                EmptyMyBoardsPanel.Visibility = _myBoards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void MyBoardsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as MyBoardDisplayItem;
            if (item == null) return;
            Frame.Navigate(typeof(BoardDetailPage), item.Id);
        }

        // ─── 我的关注 ────────────────────────────────────────
        private void MyFollowListView_Loaded(object sender, RoutedEventArgs e)
        {
            var sv = MyFollowListView.GetScrollViewer();
            if (sv != null)
            {
                sv.ViewChanged += async (s, args) =>
                {
                    if (args.IsIntermediate) return;
                    if (sv.VerticalOffset >= sv.ScrollableHeight - 200 && _myFollowsHasMore && !_myFollowsLoading)
                    {
                        await LoadMyFollowsAsync();
                    }
                };
            }
        }

        private async Task LoadMyFollowsAsync(bool reset = false)
        {
            if (_myFollowsLoading) return;
            _myFollowsLoading = true;

            if (reset)
            {
                _myFollowsPage = 1;
                _myFollowsHasMore = true;
                _myFollows.Clear();
                MyFollowProgressBar.Visibility = Visibility.Visible;
            }
            else
            {
                if (MyFollowLoadMoreBar != null) MyFollowLoadMoreBar.Visibility = Visibility.Visible;
            }

            try
            {
                var result = await MyActivityApi.GetMyFollowingBoardListAsync(_token, _myFollowsPage, 20);
                if (result != null && result.IsSuccess)
                {
                    foreach (var b in result.Boards)
                    {
                        var di = new FollowingBoardDisplayItem
                        {
                            Id = b.Id,
                            Name = b.Name,
                            AvatarUrl = b.Avatar,
                            MemberNum = b.MemberNum,
                            PostNum = b.PostNum
                        };
                        _myFollows.Add(di);
                        PreloadFollowAvatar(di);
                    }
                    _myFollowsHasMore = result.Boards.Count >= 20;
                    _myFollowsPage++;
                }
                else
                {
                    _myFollowsHasMore = false;
                }
            }
            catch { _myFollowsHasMore = false; }
            finally
            {
                _myFollowsLoading = false;
                MyFollowProgressBar.Visibility = Visibility.Collapsed;
                if (MyFollowLoadMoreBar != null) MyFollowLoadMoreBar.Visibility = Visibility.Collapsed;
                if (MyFollowNoMoreText != null)
                    MyFollowNoMoreText.Visibility = !_myFollowsHasMore && _myFollows.Count > 0
                        ? Visibility.Visible : Visibility.Collapsed;
                EmptyMyFollowPanel.Visibility = _myFollows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void MyFollowListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as FollowingBoardDisplayItem;
            if (item == null) return;
            Frame.Navigate(typeof(BoardDetailPage), item.Id);
        }

        // ─── 我的收藏 ────────────────────────────────────────
        private void MyCollectListView_Loaded(object sender, RoutedEventArgs e)
        {
            var sv = MyCollectListView.GetScrollViewer();
            if (sv != null)
            {
                sv.ViewChanged += async (s, args) =>
                {
                    if (args.IsIntermediate) return;
                    if (sv.VerticalOffset >= sv.ScrollableHeight - 200 && _myCollectsHasMore && !_myCollectsLoading)
                    {
                        await LoadMyCollectsAsync();
                    }
                };
            }
        }

        private async Task LoadMyCollectsAsync(bool reset = false)
        {
            if (_myCollectsLoading) return;
            _myCollectsLoading = true;

            if (reset)
            {
                _myCollectsPage = 1;
                _myCollectsHasMore = true;
                _myCollects.Clear();
                MyCollectProgressBar.Visibility = Visibility.Visible;
            }
            else
            {
                if (MyCollectLoadMoreBar != null) MyCollectLoadMoreBar.Visibility = Visibility.Visible;
            }

            try
            {
                var result = await MyActivityApi.GetMyCollectListAsync(_token, _myCollectsPage, 20);
                if (result != null && result.IsSuccess)
                {
                    foreach (var post in result.Posts)
                    {
                        _myCollects.Add(post);
                        PreloadPostAvatar(post);
                    }
                    _myCollectsHasMore = result.Posts.Count >= 20;
                    _myCollectsPage++;
                }
                else
                {
                    _myCollectsHasMore = false;
                }
            }
            catch { _myCollectsHasMore = false; }
            finally
            {
                _myCollectsLoading = false;
                MyCollectProgressBar.Visibility = Visibility.Collapsed;
                if (MyCollectLoadMoreBar != null) MyCollectLoadMoreBar.Visibility = Visibility.Collapsed;
                if (MyCollectNoMoreText != null)
                    MyCollectNoMoreText.Visibility = !_myCollectsHasMore && _myCollects.Count > 0
                        ? Visibility.Visible : Visibility.Collapsed;
                EmptyMyCollectPanel.Visibility = _myCollects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void MyCollectListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var post = e.ClickedItem as CommunityPostItem;
            if (post == null) return;
            var navArgs = new PostDetailNavigationArgs
            {
                PostId = post.Id,
                InitialPost = post,
                Token = _token
            };
            Frame.Navigate(typeof(PostDetailPage), navArgs);
        }

        // ─── CommandBar ──────────────────────────────────────
        private async void AppBarBtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            int idx = ActivityPivot.SelectedIndex;
            if (idx == 0) await LoadMyPostsAsync(reset: true);
            else if (idx == 1) await LoadMyBoardsAsync();
            else if (idx == 2) await LoadMyFollowsAsync(reset: true);
            else if (idx == 3) await LoadMyCollectsAsync(reset: true);
        }

        // ─── 头像加载辅助 ─────────────────────────────────────
        private void PreloadPostAvatar(CommunityPostItem post)
        {
            if (post == null || string.IsNullOrEmpty(post.SenderAvatar) || ImageLoader.DisableAllImages) return;
            string finalUrl = ImageHelper.FormatQiniuUrl(post.SenderAvatar, 72, 72);
            Task.Run(async () =>
            {
                try
                {
                    byte[] bytes = await ImageLoader.GetImageBytesAsync(finalUrl);
                    if (bytes != null && bytes.Length > 0)
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
                        {
                            var bmp = await ImageLoader.BytesToBitmapImageAsync(bytes, 72, 72);
                            if (bmp != null)
                            {
                                post.AvatarBitmap = bmp;
                            }
                        });
                    }
                }
                catch { }
            });
        }

        private void PreloadBoardAvatar(MyBoardDisplayItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.AvatarUrl) || ImageLoader.DisableAllImages) return;
            string finalUrl = ImageHelper.FormatQiniuUrl(item.AvatarUrl, 72, 72);
            Task.Run(async () =>
            {
                try
                {
                    byte[] bytes = await ImageLoader.GetImageBytesAsync(finalUrl);
                    if (bytes != null && bytes.Length > 0)
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
                        {
                            var bmp = await ImageLoader.BytesToBitmapImageAsync(bytes, 72, 72);
                            if (bmp != null)
                            {
                                item.AvatarBitmap = bmp;
                            }
                        });
                    }
                }
                catch { }
            });
        }

        private void PreloadFollowAvatar(FollowingBoardDisplayItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.AvatarUrl) || ImageLoader.DisableAllImages) return;
            string finalUrl = ImageHelper.FormatQiniuUrl(item.AvatarUrl, 72, 72);
            Task.Run(async () =>
            {
                try
                {
                    byte[] bytes = await ImageLoader.GetImageBytesAsync(finalUrl);
                    if (bytes != null && bytes.Length > 0)
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
                        {
                            var bmp = await ImageLoader.BytesToBitmapImageAsync(bytes, 72, 72);
                            if (bmp != null)
                            {
                                item.AvatarBitmap = bmp;
                            }
                        });
                    }
                }
                catch { }
            });
        }
    }

    // ─── 显示用辅助类 ────────────────────────────────────────
    public class MyBoardDisplayItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPC(string n) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(n)); }

        public int Id { get; set; }
        public string Name { get; set; }
        public string AvatarUrl { get; set; }
        public string NameLetter { get { return string.IsNullOrEmpty(Name) ? "板" : Name.Substring(0, 1).ToUpper(); } }
        public string IdText { get { return string.Format("ID: {0}", Id); } }

        private BitmapImage _ab;
        public BitmapImage AvatarBitmap { get { return _ab; } set { if (_ab != value) { _ab = value; OnPC("AvatarBitmap"); } } }
    }

    public class FollowingBoardDisplayItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPC(string n) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(n)); }

        public int Id { get; set; }
        public string Name { get; set; }
        public string AvatarUrl { get; set; }
        public int MemberNum { get; set; }
        public int PostNum { get; set; }

        public string NameLetter { get { return string.IsNullOrEmpty(Name) ? "板" : Name.Substring(0, 1).ToUpper(); } }
        public string StatsText { get { return string.Format("{0} 成员  {1} 动态", MemberNum, PostNum); } }

        private BitmapImage _ab;
        public BitmapImage AvatarBitmap { get { return _ab; } set { if (_ab != value) { _ab = value; OnPC("AvatarBitmap"); } } }
    }

    // ─── ListView 滚动扩展 ────────────────────────────────────
    internal static class ListViewExtensions
    {
        public static ScrollViewer GetScrollViewer(this ListView lv)
        {
            if (lv == null) return null;
            return FindChild<ScrollViewer>(lv);
        }

        private static T FindChild<T>(Windows.UI.Xaml.DependencyObject parent) where T : Windows.UI.Xaml.DependencyObject
        {
            int count = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T) return (T)child;
                var result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
