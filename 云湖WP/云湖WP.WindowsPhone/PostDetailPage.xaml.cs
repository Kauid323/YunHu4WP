using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Phone.UI.Input;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Common;
using 云湖WP.Api.Community;
using 云湖WP.Api.Community.Board;
using 云湖WP.Api.Community.CreatePost;
using 云湖WP.Api.Community.EditPost;
using 云湖WP.Api.Community.PostDetail;
using 云湖WP.Api.User.Info;
using 云湖WP.Token;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 社区动态详情页面 (纯正 Windows Phone Metro 大标题与扁平排版，CommandBar 原生交互)
    /// </summary>
    public sealed partial class PostDetailPage : Page
    {
        private string _token = "";
        private long _postId = 0;
        private int _baId = 0;
        private string _selfUserId = ""; // 当前登录用户 ID
        private CommunityPostItem _currentPost;
        private BoardInfoItem _currentBoard;
        private ObservableCollection<CommunityCommentItem> _commentList = new ObservableCollection<CommunityCommentItem>();
        private ObservableCollection<CommunityPostItem> _boardHotPosts = new ObservableCollection<CommunityPostItem>();
        private bool _isLoadingDetail = false;
        private bool _isLoadingComments = false;
        private bool _isLoadingBoardPosts = false;
        private bool _isSendingComment = false;
        private DispatcherTimer _scrollTimer;
        private bool _isKeyboardOpen = false;
        private ScrollViewer _boardScrollViewer;

        public PostDetailPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            InitScrollTimer();
        }

        private void InitScrollTimer()
        {
            if (_scrollTimer == null)
            {
                _scrollTimer = new DispatcherTimer();
                _scrollTimer.Interval = TimeSpan.FromSeconds(2);
                _scrollTimer.Tick += (s, args) =>
                {
                    _scrollTimer.Stop();
                    RestoreBottomElementsOnScrollEnd();
                };
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // 注册硬件返回按键与软键盘监听
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;
            InputPane.GetForCurrentView().Showing += InputPane_Showing;
            InputPane.GetForCurrentView().Hiding += InputPane_Hiding;

            if (CommentListView != null)
            {
                CommentListView.ItemsSource = _commentList;
            }

            if (BoardHotListView != null)
            {
                BoardHotListView.ItemsSource = _boardHotPosts;
            }

            // 读取导航参数
            var args = e.Parameter as PostDetailNavigationArgs;
            if (args != null)
            {
                _postId = args.PostId;
                _token = args.Token;
                if (args.InitialPost != null)
                {
                    _currentPost = args.InitialPost;
                    if (_currentPost.BaId > 0) _baId = _currentPost.BaId;
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

            // 获取当前用户 ID（用于判断是否显示编辑/删除/置顶按钮）
            if (string.IsNullOrEmpty(_selfUserId) && !string.IsNullOrEmpty(_token))
            {
                try
                {
                    var selfRes = await 云湖WP.Api.User.UserApi.GetSelfInfoAsync(_token);
                    if (selfRes != null && selfRes.IsSuccess)
                    {
                        _selfUserId = selfRes.Id ?? "";
                    }
                }
                catch { }
            }

            if (_postId > 0)
            {
                await RefreshAllDataAsync();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
            InputPane.GetForCurrentView().Showing -= InputPane_Showing;
            InputPane.GetForCurrentView().Hiding -= InputPane_Hiding;

            if (_scrollTimer != null)
            {
                _scrollTimer.Stop();
            }

            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.IsOpen = false;
            }
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            e.Handled = true;
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private async void AppBarBtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAllDataAsync();
        }

        private async Task RefreshAllDataAsync()
        {
            await LoadPostDetailAsync();
            await LoadCommentsAsync();
            await LoadBoardHotPostsAsync();
        }

        /// <summary>
        /// 从服务器拉取动态详情数据 (POST /v1/community/posts/post-detail)
        /// </summary>
        private async Task LoadPostDetailAsync()
        {
            if (_postId <= 0 || string.IsNullOrEmpty(_token) || _isLoadingDetail) return;

            _isLoadingDetail = true;
            if (PostProgressBar != null) PostProgressBar.Visibility = Visibility.Visible;

            string err = null;
            try
            {
                var res = await PostDetailApi.GetPostDetailAsync(_token, _postId);
                if (res.IsSuccess && res.Post != null)
                {
                    _currentPost = res.Post;
                    if (_currentPost.BaId > 0) _baId = _currentPost.BaId;
                    RenderPost(_currentPost);
                }
                else if (!string.IsNullOrEmpty(res.Msg))
                {
                    err = "获取动态详情失败: " + res.Msg;
                }

                if (res.Board != null)
                {
                    _currentBoard = res.Board;
                    if (_currentBoard.Id > 0) _baId = _currentBoard.Id;
                    UpdateBoardHeader(_currentBoard.Name);
                }
            }
            catch (Exception ex)
            {
                err = "加载详情异常: " + ex.Message;
            }
            finally
            {
                _isLoadingDetail = false;
                if (!_isLoadingComments && PostProgressBar != null)
                {
                    PostProgressBar.Visibility = Visibility.Collapsed;
                }
            }

            if (_baId > 0 && _boardHotPosts.Count == 0)
            {
                var t = LoadBoardHotPostsAsync();
            }

            if (err != null)
            {
                await ShowToastAsync(err);
            }
        }

        private void UpdateBoardHeader(string boardName)
        {
            if (!string.IsNullOrWhiteSpace(boardName) && PivotItemBoardHot != null)
            {
                PivotItemBoardHot.Header = boardName;
            }
        }

        /// <summary>
        /// 从服务器拉取板块热门动态列表 (POST /v1/community/posts/post-list)
        /// </summary>
        private async Task LoadBoardHotPostsAsync()
        {
            if (_baId <= 0 || string.IsNullOrEmpty(_token) || _isLoadingBoardPosts) return;

            _isLoadingBoardPosts = true;
            if (BoardHotProgressBar != null) BoardHotProgressBar.Visibility = Visibility.Visible;

            try
            {
                var res = await BoardApi.GetBoardPostsAsync(_token, _baId, typ: 2, page: 1, size: 20);
                if (res.IsSuccess && res.Posts != null)
                {
                    _boardHotPosts.Clear();
                    foreach (var post in res.Posts)
                    {
                        _boardHotPosts.Add(post);
                    }

                    if (EmptyBoardHotPanel != null)
                    {
                        EmptyBoardHotPanel.Visibility = (_boardHotPosts.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
                    }

                    PreloadPostAvatars(res.Posts);
                }
            }
            catch { }
            finally
            {
                _isLoadingBoardPosts = false;
                if (BoardHotProgressBar != null)
                {
                    BoardHotProgressBar.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// 从服务器拉取动态评论列表 (POST /v1/community/comment/comment-list)
        /// </summary>
        private async Task LoadCommentsAsync()
        {
            if (_postId <= 0 || string.IsNullOrEmpty(_token) || _isLoadingComments) return;

            _isLoadingComments = true;
            if (PostProgressBar != null) PostProgressBar.Visibility = Visibility.Visible;

            try
            {
                var res = await PostDetailApi.GetCommentListAsync(_token, _postId, page: 1, size: 50);
                if (res.IsSuccess && res.Comments != null)
                {
                    _commentList.Clear();
                    foreach (var c in res.Comments)
                    {
                        _commentList.Add(c);
                    }

                    if (PivotItemComments != null)
                    {
                        int count = res.Total > 0 ? res.Total : _commentList.Count;
                        PivotItemComments.Header = string.Format("评论 ({0})", count);
                    }

                    if (EmptyCommentPanel != null)
                    {
                        EmptyCommentPanel.Visibility = (_commentList.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
                    }

                    // 预加载评论者头像
                    PreloadCommentAvatars(res.Comments);
                }
            }
            catch { }
            finally
            {
                _isLoadingComments = false;
                if (!_isLoadingDetail && PostProgressBar != null)
                {
                    PostProgressBar.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// 渲染动态文章基本信息与状态
        /// </summary>
        private void RenderPost(CommunityPostItem post)
        {
            if (post == null) return;

            // 1. 作者信息
            TxtAuthorLetter.Text = post.DisplayAvatarLetter;
            TxtAuthorName.Text = post.DisplayAuthor;
            TxtPostTime.Text = !string.IsNullOrEmpty(post.DisplayTime) ? post.DisplayTime : "刚刚";

            if (post.AvatarBitmap != null)
            {
                ImgAuthorAvatar.Source = post.AvatarBitmap;
            }
            else if (!string.IsNullOrEmpty(post.SenderAvatar))
            {
                PreloadAuthorAvatar(post.SenderAvatar);
            }

            // 2. 标题与正文
            if (!string.IsNullOrWhiteSpace(post.Title))
            {
                TxtPostTitle.Text = post.Title;
                TxtPostTitle.Visibility = Visibility.Visible;
            }
            else
            {
                TxtPostTitle.Visibility = Visibility.Collapsed;
            }

            TxtPostContent.Text = !string.IsNullOrEmpty(post.Content) ? post.Content : "(无正文内容)";

            if (PivotItemComments != null && post.CommentNum > 0)
            {
                PivotItemComments.Header = string.Format("评论 ({0})", post.CommentNum);
            }

            // 3. 更新底栏描述文本与数据
            UpdateCommandBarStates(post);
        }

        private void UpdateCommandBarStates(CommunityPostItem post)
        {
            if (post == null) return;

            if (AppBarBtnLike != null)
            {
                AppBarBtnLike.Label = string.Format("{0} 点赞", post.LikeNum);
            }

            if (AppBarBtnCollect != null)
            {
                AppBarBtnCollect.Label = string.Format("{0} 收藏", post.CollectNum);
            }

            if (AppBarBtnReward != null)
            {
                AppBarBtnReward.Label = string.Format("{0:0.#} 投币", post.AmountNum);
            }

            // 判断是否为自己的动态
            bool isMine = !string.IsNullOrEmpty(_selfUserId) &&
                          !string.IsNullOrEmpty(post.SenderId) &&
                          _selfUserId == post.SenderId;

            if (AppBarBtnEditPost != null)
                AppBarBtnEditPost.Visibility = isMine ? Visibility.Visible : Visibility.Collapsed;
            if (AppBarBtnDeletePost != null)
                AppBarBtnDeletePost.Visibility = isMine ? Visibility.Visible : Visibility.Collapsed;
            if (AppBarBtnStickyPost != null)
            {
                AppBarBtnStickyPost.Visibility = isMine ? Visibility.Visible : Visibility.Collapsed;
                // 置顶状态：IsSticky=0 未置顶 → 显示"置顶"；非0已置顶 → 显示"取消置顶"
                AppBarBtnStickyPost.Label = (post.IsSticky != 0) ? "取消置顶" : "置顶";
            }
        }

        // ─── 编辑动态 ─────────────────────────────────────────
        private void AppBarBtnEditPost_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPost == null) return;
            var navArgs = new CreatePostNavArgs
            {
                PostId = _currentPost.Id,
                BaId = _currentPost.BaId,
                BoardName = _currentBoard != null ? _currentBoard.Name : "",
                InitialTitle = _currentPost.Title,
                InitialContent = _currentPost.Content,
                InitialContentType = _currentPost.ContentType
            };
            Frame.Navigate(typeof(CreatePostPage), navArgs);
        }

        // ─── 置顶/取消置顶 ────────────────────────────────────
        private async void AppBarBtnStickyPost_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPost == null || _postId <= 0) return;
            string err = null;
            string successMsg = null;
            try
            {
                var res = await EditPostApi.ToggleStickyAsync(_token, _postId);
                if (res != null && res.IsSuccess)
                {
                    _currentPost.IsSticky = (_currentPost.IsSticky != 0) ? 0 : 1;
                    UpdateCommandBarStates(_currentPost);
                    successMsg = (_currentPost.IsSticky != 0) ? "已置顶" : "已取消置顶";
                }
                else
                {
                    err = (res != null && !string.IsNullOrEmpty(res.Msg)) ? res.Msg : "操作失败";
                }
            }
            catch (Exception ex)
            {
                err = "操作异常: " + ex.Message;
            }

            if (successMsg != null)
            {
                await ShowToastAsync(successMsg);
            }
            else if (err != null)
            {
                await ShowToastAsync(err);
            }
        }

        // ─── 删除动态 ─────────────────────────────────────────
        private async void AppBarBtnDeletePost_Click(object sender, RoutedEventArgs e)
        {
            if (_postId <= 0) return;
            string err = null;
            bool deleteSuccess = false;
            try
            {
                var dialog = new MessageDialog("确定要删除这条动态吗？删除后不可恢复。", "删除动态");
                dialog.Commands.Add(new UICommand("确定删除"));
                dialog.Commands.Add(new UICommand("取消"));
                dialog.DefaultCommandIndex = 1;
                dialog.CancelCommandIndex = 1;
                var chosen = await dialog.ShowAsync();
                if (chosen == null || chosen.Label != "确定删除") return;

                var res = await EditPostApi.DeletePostAsync(_token, _postId);
                if (res != null && res.IsSuccess)
                {
                    deleteSuccess = true;
                }
                else
                {
                    err = (res != null && !string.IsNullOrEmpty(res.Msg)) ? res.Msg : "删除失败";
                }
            }
            catch (Exception ex)
            {
                err = "删除异常: " + ex.Message;
            }

            if (deleteSuccess)
            {
                await ShowToastAsync("动态已删除");
                if (Frame.CanGoBack) Frame.GoBack();
            }
            else if (err != null)
            {
                await ShowToastAsync("删除失败: " + err);
            }
        }

        private void PreloadAuthorAvatar(string avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl) || ImageLoader.DisableAllImages) return;

            string finalUrl = ImageHelper.FormatQiniuUrl(avatarUrl, 96, 96);
            Task.Run(async () =>
            {
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
                                ImgAuthorAvatar.Source = bmp;
                            }
                        });
                    }
                }
                catch { }
            });
        }

        private void PreloadCommentAvatars(IEnumerable<CommunityCommentItem> comments)
        {
            if (comments == null || ImageLoader.DisableAllImages) return;
            var list = new List<CommunityCommentItem>(comments);

            Task.Run(async () =>
            {
                await Task.Delay(100);
                foreach (var c in list)
                {
                    if (c == null || string.IsNullOrEmpty(c.SenderAvatar) || c.AvatarBitmap != null) continue;
                    var cur = c;
                    string finalUrl = ImageHelper.FormatQiniuUrl(cur.SenderAvatar, 72, 72);

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
                                    cur.AvatarBitmap = bmp;
                                }
                            });
                        }
                    }
                    catch { }

                    await Task.Delay(15);
                }
            });
        }

        private async void AppBarBtnLike_Click(object sender, RoutedEventArgs e)
        {
            if (_postId <= 0 || string.IsNullOrEmpty(_token)) return;

            // 乐观切换点赞状态
            if (_currentPost != null)
            {
                _currentPost.IsLiked = !_currentPost.IsLiked;
                _currentPost.LikeNum += _currentPost.IsLiked ? 1 : -1;
                if (_currentPost.LikeNum < 0) _currentPost.LikeNum = 0;
                UpdateCommandBarStates(_currentPost);
            }

            try
            {
                var res = await PostDetailApi.TogglePostLikeAsync(_token, _postId);
                if (!res.IsSuccess && !string.IsNullOrEmpty(res.Msg))
                {
                    await ShowToastAsync(res.Msg);
                }
            }
            catch { }
        }

        private async void AppBarBtnCollect_Click(object sender, RoutedEventArgs e)
        {
            if (_postId <= 0 || string.IsNullOrEmpty(_token)) return;

            // 乐观切换收藏状态
            if (_currentPost != null)
            {
                _currentPost.IsCollected = !_currentPost.IsCollected;
                _currentPost.CollectNum += _currentPost.IsCollected ? 1 : -1;
                if (_currentPost.CollectNum < 0) _currentPost.CollectNum = 0;
                UpdateCommandBarStates(_currentPost);
            }

            try
            {
                var res = await PostDetailApi.TogglePostCollectAsync(_token, _postId);
                if (!res.IsSuccess && !string.IsNullOrEmpty(res.Msg))
                {
                    await ShowToastAsync(res.Msg);
                }
            }
            catch { }
        }

        private async void AppBarBtnReward_Click(object sender, RoutedEventArgs e)
        {
            if (_postId <= 0 || string.IsNullOrEmpty(_token)) return;

            var dialog = new MessageDialog("确定为这篇动态投币打赏 1 金币吗？", "投币打赏");
            dialog.Commands.Add(new UICommand("确定打赏", async cmd =>
            {
                string recvId = _currentPost != null ? _currentPost.SenderId : "";
                string rewardErr = null;
                try
                {
                    var res = await PostDetailApi.RewardPostAsync(_token, _postId, recvId, 1);
                    if (res.IsSuccess)
                    {
                        if (_currentPost != null)
                        {
                            _currentPost.AmountNum += 1;
                            UpdateCommandBarStates(_currentPost);
                        }
                        await ShowToastAsync("投币成功！");
                    }
                    else
                    {
                        rewardErr = "投币失败: " + res.Msg;
                    }
                }
                catch (Exception ex)
                {
                    rewardErr = "投币异常: " + ex.Message;
                }
                if (rewardErr != null)
                {
                    await ShowToastAsync(rewardErr);
                }
            }));
            dialog.Commands.Add(new UICommand("取消"));
            await dialog.ShowAsync();
        }

        private async void BtnSendComment_Click(object sender, RoutedEventArgs e)
        {
            await DoSendCommentAsync();
        }

        private async void TxtCommentInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                await DoSendCommentAsync();
            }
        }

        private async Task DoSendCommentAsync()
        {
            if (_isSendingComment || _postId <= 0 || string.IsNullOrEmpty(_token)) return;

            string content = TxtCommentInput.Text;
            if (string.IsNullOrWhiteSpace(content))
            {
                await ShowToastAsync("请输入评论内容");
                return;
            }

            _isSendingComment = true;
            TxtCommentInput.Text = "";

            string err = null;
            try
            {
                var res = await PostDetailApi.SendCommentAsync(_token, _postId, content.Trim());
                if (res.IsSuccess)
                {
                    await ShowToastAsync("评论发表成功");
                    await LoadCommentsAsync();
                }
                else
                {
                    err = "评论发表失败: " + res.Msg;
                }
            }
            catch (Exception ex)
            {
                err = "发表评论异常: " + ex.Message;
            }
            finally
            {
                _isSendingComment = false;
            }

            if (err != null)
            {
                await ShowToastAsync(err);
            }
        }

        private void Author_Tapped(object sender, TappedRoutedEventArgs e)
        {
            NavigateToAuthor();
        }

        private void AppBarBtnAuthor_Click(object sender, RoutedEventArgs e)
        {

            NavigateToAuthor();
        }

        private async void NavigateToAuthor()
        {
            try
            {
                if (_currentPost == null)
                {
                    await ShowToastAsync("动态正在加载中，请稍候");
                    return;
                }

                string uid = !string.IsNullOrEmpty(_currentPost.SenderId) ? _currentPost.SenderId : "";
                string authorName = !string.IsNullOrEmpty(_currentPost.SenderNickname)
                    ? _currentPost.SenderNickname
                    : (!string.IsNullOrEmpty(_currentPost.DisplayAuthor) && _currentPost.DisplayAuthor != "湖友" ? _currentPost.DisplayAuthor : "");

                if (string.IsNullOrEmpty(uid))
                {
                    await ShowToastAsync("未获取到作者用户ID");
                    return;
                }

                var navArgs = new UserDetailNavArgs
                {
                    UserId = uid,
                    Name = !string.IsNullOrEmpty(authorName) ? authorName : uid,
                    AvatarUrl = _currentPost.SenderAvatar ?? ""
                };

                // 在 Dispatcher 中触发导航，避免二级菜单收起动画与页面路由冲突
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Frame.Navigate(typeof(UserDetailPage), navArgs);
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log("NavigateToAuthor", ex.Message);
            }
        }

        private async void AppBarBtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPost != null && !string.IsNullOrEmpty(_currentPost.Content))
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Frame.Navigate(typeof(TextViewerPage), _currentPost.Content);
                });
            }
        }

        private void CommentItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element == null) return;
            var comment = element.DataContext as CommunityCommentItem;
            if (comment == null || string.IsNullOrEmpty(comment.Content)) return;

            var flyout = new MenuFlyout();
            var itemCopy = new MenuFlyoutItem { Text = "复制/查看评论" };
            itemCopy.Click += async (s, args) =>
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Frame.Navigate(typeof(TextViewerPage), comment.Content);
                });
            };
            flyout.Items.Add(itemCopy);
            flyout.ShowAt(element);
        }

        private ScrollViewer _commentScrollViewer;

        private void CommentListView_Loaded(object sender, RoutedEventArgs e)
        {
            if (CommentListView != null && _commentScrollViewer == null)
            {
                _commentScrollViewer = FindVisualChild<ScrollViewer>(CommentListView);
                if (_commentScrollViewer != null)
                {
                    _commentScrollViewer.ViewChanged -= DetailScrollViewer_ViewChanged;
                    _commentScrollViewer.ViewChanged += DetailScrollViewer_ViewChanged;
                }
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

        private void BoardHotListView_Loaded(object sender, RoutedEventArgs e)
        {
            if (BoardHotListView != null && _boardScrollViewer == null)
            {
                _boardScrollViewer = FindVisualChild<ScrollViewer>(BoardHotListView);
                if (_boardScrollViewer != null)
                {
                    _boardScrollViewer.ViewChanged -= DetailScrollViewer_ViewChanged;
                    _boardScrollViewer.ViewChanged += DetailScrollViewer_ViewChanged;
                }
            }
        }

        private void DetailPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DetailPivot == null) return;

            bool isBoardHot = DetailPivot.SelectedItem == PivotItemBoardHot;

            if (AppBarBtnEnterBoard != null)
            {
                AppBarBtnEnterBoard.Visibility = isBoardHot ? Visibility.Visible : Visibility.Collapsed;
            }

            if (AppBarBtnLike != null)
            {
                AppBarBtnLike.Visibility = isBoardHot ? Visibility.Collapsed : Visibility.Visible;
            }

            if (AppBarBtnCollect != null)
            {
                AppBarBtnCollect.Visibility = isBoardHot ? Visibility.Collapsed : Visibility.Visible;
            }

            if (AppBarBtnReward != null)
            {
                AppBarBtnReward.Visibility = isBoardHot ? Visibility.Collapsed : Visibility.Visible;
            }

            if (isBoardHot && _boardHotPosts.Count == 0 && !_isLoadingBoardPosts && _baId > 0)
            {
                var t = LoadBoardHotPostsAsync();
            }

            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.IsOpen = false;
                this.BottomAppBar.ClosedDisplayMode = AppBarClosedDisplayMode.Compact;
                this.BottomAppBar.Visibility = Visibility.Visible;
            }
        }

        private async void AppBarBtnEnterBoard_Click(object sender, RoutedEventArgs e)
        {
            if (_baId <= 0 && _currentPost != null)
            {
                _baId = _currentPost.BaId;
            }

            if (_baId <= 0)
            {
                await ShowToastAsync("未获取到所属板块信息");
                return;
            }

            string bName = _currentBoard != null ? _currentBoard.Name : (_currentPost != null ? _currentPost.Title : "");
            if (string.IsNullOrEmpty(bName) && PivotItemBoardHot != null && PivotItemBoardHot.Header != null)
            {
                bName = PivotItemBoardHot.Header.ToString();
            }

            var navArgs = new BoardDetailNavArgs
            {
                BaId = _baId,
                BoardName = bName,
                BoardAvatar = _currentBoard != null ? _currentBoard.Avatar : "",
                Token = _token,
                InitialBoard = _currentBoard
            };

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Frame.Navigate(typeof(BoardDetailPage), navArgs);
            });
        }

        private async void BoardHotListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var clickedPost = e.ClickedItem as CommunityPostItem;
            if (clickedPost != null && clickedPost.Id > 0)
            {
                var navArgs = new PostDetailNavigationArgs
                {
                    PostId = clickedPost.Id,
                    InitialPost = clickedPost,
                    Token = _token
                };

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Frame.Navigate(typeof(PostDetailPage), navArgs);
                });
            }
        }

        private void PreloadPostAvatars(IEnumerable<CommunityPostItem> posts)
        {
            if (posts == null || ImageLoader.DisableAllImages) return;
            var list = new List<CommunityPostItem>(posts);

            Task.Run(async () =>
            {
                await Task.Delay(100);
                foreach (var p in list)
                {
                    if (p == null || string.IsNullOrEmpty(p.SenderAvatar) || p.AvatarBitmap != null) continue;
                    var cur = p;
                    string finalUrl = ImageHelper.FormatQiniuUrl(cur.SenderAvatar, 72, 72);

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
                                    cur.AvatarBitmap = bmp;
                                }
                            });
                        }
                    }
                    catch { }

                    await Task.Delay(15);
                }
            });
        }

        #region 页面滑动与底栏/输入框自动显隐交互

        private void DetailScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            // 如果键盘正在弹出输入，不自动收起输入框
            if (_isKeyboardOpen) return;

            // 1. 滑动时：自动收回输入框和 commandbar
            HideBottomElementsOnScroll();

            // 2. 停止滑动后重新计时 2 秒：2s 后自动弹出输入框
            if (_scrollTimer != null)
            {
                _scrollTimer.Stop();
                _scrollTimer.Start();
            }
        }

        private void HideBottomElementsOnScroll()
        {
            // 自动收回输入框
            if (BottomCommentBar != null && BottomCommentBar.Visibility != Visibility.Collapsed)
            {
                BottomCommentBar.Visibility = Visibility.Collapsed;
            }

            // 自动收回 CommandBar
            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.IsOpen = false;
                this.BottomAppBar.ClosedDisplayMode = AppBarClosedDisplayMode.Minimal;
            }
        }

        private void RestoreBottomElementsOnScrollEnd()
        {
            // 停止滑动 2 秒后：仅自动弹出输入框
            if (BottomCommentBar != null && BottomCommentBar.Visibility != Visibility.Visible)
            {
                BottomCommentBar.Visibility = Visibility.Visible;
            }
        }

        #endregion

        #region 软键盘与 CommandBar 交互防冲突处理

        private void InputPane_Showing(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            _isKeyboardOpen = true;
            if (_scrollTimer != null) _scrollTimer.Stop();
            if (BottomCommentBar != null) BottomCommentBar.Visibility = Visibility.Visible;
            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.Visibility = Visibility.Collapsed;
                this.BottomAppBar.IsOpen = false;
            }
        }

        private void InputPane_Hiding(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            _isKeyboardOpen = false;
            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.ClosedDisplayMode = AppBarClosedDisplayMode.Compact;
                this.BottomAppBar.Visibility = Visibility.Visible;
            }
        }

        private void TxtCommentInput_GotFocus(object sender, RoutedEventArgs e)
        {
            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.IsOpen = false;
                this.BottomAppBar.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        private async Task ShowToastAsync(string message)
        {
            try
            {
                var dialog = new MessageDialog(message, "云湖动态");
                await dialog.ShowAsync();
            }
            catch { }
        }
    }
}
