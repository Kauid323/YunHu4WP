using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Phone.UI.Input;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Common;
using 云湖WP.Api.Community;
using 云湖WP.Api.Community.Board;
using 云湖WP.Api.Community.CreatePost;
using 云湖WP.Api.Community.EditBoard;
using 云湖WP.Api.Community.PostDetail;
using 云湖WP.Api.User.Info;
using 云湖WP.Api.WebApi.User;
using 云湖WP.Token;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 社区板块页面 (原生 Windows Phone Metro 双分栏 Pivot：动态与详情)
    /// </summary>
    public sealed partial class BoardDetailPage : Page
    {
        private string _token = "";
        private int _baId = 0;
        private string _selfUserId = "";
        private BoardInfoItem _currentBoard;
        private int _currentFilterTyp = 2; // 2-最热/热门, 1-最新
        private ObservableCollection<CommunityPostItem> _posts = new ObservableCollection<CommunityPostItem>();
        private int _postsPage = 1;
        private bool _hasMorePosts = true;
        private bool _isLoadingMorePosts = false;
        private ScrollViewer _postsScrollViewer;
        private BoardFollowerItem _boardOwner;
        private ObservableCollection<BoardFollowerItem> _admins = new ObservableCollection<BoardFollowerItem>();
        private bool _isLoadingBoard = false;
        private bool _isLoadingPosts = false;
        private bool _isLoadingManagement = false;
        private bool _isTogglingFollow = false;

        public BoardDetailPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            if (PostsListView != null)
            {
                PostsListView.ItemsSource = _posts;
            }

            if (AdminsItemsControl != null)
            {
                AdminsItemsControl.ItemsSource = _admins;
            }

            var args = e.Parameter as BoardDetailNavArgs;
            if (args != null)
            {
                _baId = args.BaId;
                _token = args.Token;
                if (args.InitialBoard != null)
                {
                    _currentBoard = args.InitialBoard;
                    RenderBoardInfo(_currentBoard);
                }
                else if (!string.IsNullOrEmpty(args.BoardName))
                {
                    _currentBoard = new BoardInfoItem { Id = _baId, Name = args.BoardName, Avatar = args.BoardAvatar };
                    RenderBoardInfo(_currentBoard);
                }
            }
            else if (e.Parameter is int)
            {
                _baId = (int)e.Parameter;
            }

            if (string.IsNullOrEmpty(_token))
            {
                _token = await TokenManager.GetTokenAsync();
            }

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

            if (_baId > 0)
            {
                await RefreshAllAsync();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;

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

        private async Task RefreshAllAsync()
        {
            await LoadBoardInfoAsync();
            await LoadPostsAsync();
            await LoadBoardManagementAsync();
        }

        private async void AppBarBtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAllAsync();
        }

        /// <summary>
        /// 加载板块基本信息 (POST /v1/community/ba/info)
        /// </summary>
        private async Task LoadBoardInfoAsync()
        {
            if (_baId <= 0 || string.IsNullOrEmpty(_token) || _isLoadingBoard) return;

            _isLoadingBoard = true;
            if (DetailsProgressBar != null) DetailsProgressBar.Visibility = Visibility.Visible;

            string err = null;
            try
            {
                var res = await BoardApi.GetBoardInfoAsync(_token, _baId);
                if (res.IsSuccess && res.Board != null)
                {
                    _currentBoard = res.Board;
                    RenderBoardInfo(_currentBoard);
                }
                else if (!string.IsNullOrEmpty(res.Msg))
                {
                    err = "获取板块详情失败: " + res.Msg;
                }
            }
            catch (Exception ex)
            {
                err = "加载板块异常: " + ex.Message;
            }
            finally
            {
                _isLoadingBoard = false;
                if (DetailsProgressBar != null)
                {
                    DetailsProgressBar.Visibility = Visibility.Collapsed;
                }
            }

            if (err != null)
            {
                await ShowToastAsync(err);
            }
        }

        /// <summary>
        /// 加载板块动态列表 (POST /v1/community/posts/post-list)
        /// </summary>
        private async Task LoadPostsAsync(bool isRefresh = true)
        {
            if (_baId <= 0 || string.IsNullOrEmpty(_token) || _isLoadingPosts) return;

            if (isRefresh)
            {
                _postsPage = 1;
                _hasMorePosts = true;
            }

            _isLoadingPosts = true;
            if (PostsProgressBar != null) PostsProgressBar.Visibility = Visibility.Visible;

            try
            {
                var res = await BoardApi.GetBoardPostsAsync(_token, _baId, typ: _currentFilterTyp, page: _postsPage, size: 20);
                if (res.IsSuccess && res.Posts != null)
                {
                    if (isRefresh)
                    {
                        _posts.Clear();
                    }

                    foreach (var post in res.Posts)
                    {
                        _posts.Add(post);
                    }

                    _hasMorePosts = (res.Posts.Count >= 20);

                    if (EmptyPostsPanel != null)
                    {
                        EmptyPostsPanel.Visibility = (_posts.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
                    }

                    PreloadPostAvatars(res.Posts);
                }
            }
            catch { }
            finally
            {
                _isLoadingPosts = false;
                if (PostsProgressBar != null)
                {
                    PostsProgressBar.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// 自动加载下一页动态 (无感触底加载)
        /// </summary>
        private async Task LoadMorePostsAsync()
        {
            if (_baId <= 0 || string.IsNullOrEmpty(_token) || _isLoadingPosts || _isLoadingMorePosts || !_hasMorePosts) return;

            _isLoadingMorePosts = true;
            if (PostsProgressBar != null) PostsProgressBar.Visibility = Visibility.Visible;

            try
            {
                int nextPage = _postsPage + 1;
                var res = await BoardApi.GetBoardPostsAsync(_token, _baId, typ: _currentFilterTyp, page: nextPage, size: 20);
                if (res.IsSuccess && res.Posts != null && res.Posts.Count > 0)
                {
                    _postsPage = nextPage;
                    foreach (var post in res.Posts)
                    {
                        _posts.Add(post);
                    }

                    _hasMorePosts = (res.Posts.Count >= 20);
                    PreloadPostAvatars(res.Posts);
                }
                else
                {
                    _hasMorePosts = false;
                }
            }
            catch { }
            finally
            {
                _isLoadingMorePosts = false;
                if (PostsProgressBar != null)
                {
                    PostsProgressBar.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// 渲染板块信息到 UI 控件
        /// </summary>
        private void RenderBoardInfo(BoardInfoItem board)
        {
            if (board == null) return;

            // 1. 头像与首字母
            TxtBoardLetter.Text = board.DisplayAvatarLetter;
            TxtBoardHeading.Text = board.DisplayName;
            TxtBoardSubId.Text = "板块 ID: " + (board.Id > 0 ? board.Id.ToString() : "-");

            if (board.AvatarBitmap != null)
            {
                ImgBoardAvatar.Source = board.AvatarBitmap;
            }
            else if (!string.IsNullOrEmpty(board.Avatar))
            {
                PreloadBoardAvatar(board.Avatar);
            }

            // 2. 关注按钮状态
            UpdateFollowButtonState(board.IsFollowed);

            // 3. 统计数字
            TxtMemberCount.Text = board.MemberNum.ToString();
            TxtPostCount.Text = board.PostNum.ToString();
            TxtGroupCount.Text = board.GroupNum.ToString();

            // 4. 详细档案列表
            TxtDetailId.Text = board.Id > 0 ? board.Id.ToString() : "-";
            TxtDetailName.Text = board.DisplayName;
            TxtDetailCreateTime.Text = board.DisplayCreateTime;
            TxtDetailLastActive.Text = board.DisplayLastActive;

            UpdateEditBoardButtonVisibility();
        }

        private void UpdateFollowButtonState(bool isFollowed)
        {
            if (BtnFollowAction != null)
            {
                BtnFollowAction.Content = isFollowed ? "已关注" : "+ 关注板块";
                if (isFollowed)
                {
                    BtnFollowAction.Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60));
                }
                else
                {
                    BtnFollowAction.Background = (Brush)Application.Current.Resources["PhoneAccentBrush"];
                }
            }

            if (AppBarBtnFollowCmd != null)
            {
                AppBarBtnFollowCmd.Label = isFollowed ? "已关注" : "关注";
                AppBarBtnFollowCmd.Icon = isFollowed ? new SymbolIcon(Symbol.Accept) : new SymbolIcon(Symbol.Add);
            }
        }

        private void PreloadBoardAvatar(string avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl) || ImageLoader.DisableAllImages) return;

            string finalUrl = ImageHelper.FormatQiniuUrl(avatarUrl, 140, 140);
            Task.Run(async () =>
            {
                try
                {
                    byte[] bytes = await ImageLoader.GetImageBytesAsync(finalUrl);
                    if (bytes != null && bytes.Length > 0)
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
                        {
                            var bmp = await ImageLoader.BytesToBitmapImageAsync(bytes, 140, 140);
                            if (bmp != null)
                            {
                                ImgBoardAvatar.Source = bmp;
                                if (_currentBoard != null)
                                {
                                    _currentBoard.AvatarBitmap = bmp;
                                }
                            }
                        });
                    }
                }
                catch { }
            });
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

        #region 筛选逻辑 (最热 vs 最新)

        private async void FilterHot_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFilterTyp == 2) return;
            _currentFilterTyp = 2;
            FlyoutItemHot.Text = "最热动态 (当前)";
            FlyoutItemLatest.Text = "最新动态";
            await LoadPostsAsync();
        }

        private async void FilterLatest_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFilterTyp == 1) return;
            _currentFilterTyp = 1;
            FlyoutItemHot.Text = "最热动态";
            FlyoutItemLatest.Text = "最新动态 (当前)";
            await LoadPostsAsync();
        }

        #endregion

        #region 板块管理组 (板块主与管理员解析)

        /// <summary>
        /// 加载板块管理组信息 (POST /v1/community/ba/follower-list)
        /// 解析板块主 (userLevel=1 或 createBy) 及 管理员列表 (userLevel=2)
        /// </summary>
        private async Task LoadBoardManagementAsync()
        {
            if (_baId <= 0 || string.IsNullOrEmpty(_token) || _isLoadingManagement) return;

            _isLoadingManagement = true;
            try
            {
                var res = await BoardApi.GetBoardFollowersAsync(_token, _baId, page: 1, size: 100);
                if (res.IsSuccess && res.Followers != null)
                {
                    BoardFollowerItem owner = null;
                    var adminList = new List<BoardFollowerItem>();

                    string creatorId = _currentBoard != null ? _currentBoard.CreateBy : "";

                    foreach (var f in res.Followers)
                    {
                        if (f.UserLevel == 1 || (!string.IsNullOrEmpty(creatorId) && f.UserId == creatorId))
                        {
                            if (owner == null) owner = f;
                        }

                        if (f.UserLevel == 2)
                        {
                            adminList.Add(f);
                        }
                    }

                    if (owner == null)
                    {
                        if (!string.IsNullOrEmpty(creatorId))
                        {
                            owner = new BoardFollowerItem
                            {
                                UserId = creatorId,
                                Nickname = "创建者 " + creatorId,
                                UserLevel = 1
                            };
                        }
                    }

                    _boardOwner = owner;
                    _admins.Clear();
                    foreach (var adm in adminList)
                    {
                        _admins.Add(adm);
                    }

                    RenderManagementInfo();

                    // 使用 Web API 补充/校验板块主信息 (头像与真实昵称)
                    if (_boardOwner != null && !string.IsNullOrEmpty(_boardOwner.UserId))
                    {
                        await FetchOwnerProfileAsync(_boardOwner);
                    }

                    if (_boardOwner != null && !string.IsNullOrEmpty(_boardOwner.AvatarUrl))
                    {
                        PreloadOwnerAvatar(_boardOwner.AvatarUrl);
                    }
                    PreloadAdminAvatars(adminList);
                }
                else
                {
                    // 若接口未返回全部，但存在板块创建者ID，则显示创建者信息并通过 Web API 获取详情
                    if (_currentBoard != null && !string.IsNullOrEmpty(_currentBoard.CreateBy) && _boardOwner == null)
                    {
                        _boardOwner = new BoardFollowerItem
                        {
                            UserId = _currentBoard.CreateBy,
                            Nickname = "创建者 " + _currentBoard.CreateBy,
                            UserLevel = 1
                        };
                        RenderManagementInfo();
                        await FetchOwnerProfileAsync(_boardOwner);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("LoadBoardManagement", ex.Message);
            }
            finally
            {
                _isLoadingManagement = false;
            }
        }

        private async Task FetchOwnerProfileAsync(BoardFollowerItem curOwner)
        {
            if (curOwner == null || string.IsNullOrEmpty(curOwner.UserId)) return;
            try
            {
                var uRes = await UserWebApi.GetUserHomepageAsync(curOwner.UserId, _token);
                if (uRes != null && uRes.IsSuccess && uRes.User != null)
                {
                    if (!string.IsNullOrEmpty(uRes.User.Nickname))
                    {
                        curOwner.Nickname = uRes.User.Nickname;
                    }
                    if (!string.IsNullOrEmpty(uRes.User.AvatarUrl))
                    {
                        curOwner.AvatarUrl = uRes.User.AvatarUrl;
                    }
                    RenderManagementInfo();

                    if (!string.IsNullOrEmpty(curOwner.AvatarUrl))
                    {
                        PreloadOwnerAvatar(curOwner.AvatarUrl);
                    }
                }
            }
            catch { }
        }

        private void RenderManagementInfo()
        {
            // 1. 板块主卡片
            if (_boardOwner != null)
            {
                TxtOwnerNickname.Text = _boardOwner.DisplayName;
                TxtOwnerId.Text = !string.IsNullOrEmpty(_boardOwner.UserId) ? ("ID: " + _boardOwner.UserId) : "ID: -";
                TxtOwnerLetter.Text = _boardOwner.DisplayAvatarLetter;
                if (_boardOwner.AvatarBitmap != null)
                {
                    ImgOwnerAvatar.Source = _boardOwner.AvatarBitmap;
                }
            }
            else
            {
                TxtOwnerNickname.Text = "暂无板块主信息";
                TxtOwnerId.Text = "ID: -";
                TxtOwnerLetter.Text = "主";
                ImgOwnerAvatar.Source = null;
            }

            // 2. 管理员列表与空状态
            if (TxtNoAdmins != null)
            {
                TxtNoAdmins.Visibility = (_admins.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdateEditBoardButtonVisibility();
        }

        private void PreloadOwnerAvatar(string avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl) || ImageLoader.DisableAllImages) return;
            string finalUrl = ImageHelper.FormatQiniuUrl(avatarUrl, 72, 72);

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
                                ImgOwnerAvatar.Source = bmp;
                                if (_boardOwner != null)
                                {
                                    _boardOwner.AvatarBitmap = bmp;
                                }
                            }
                        });
                    }
                }
                catch { }
            });
        }

        private void PreloadAdminAvatars(IEnumerable<BoardFollowerItem> admins)
        {
            if (admins == null || ImageLoader.DisableAllImages) return;
            var list = new List<BoardFollowerItem>(admins);

            Task.Run(async () =>
            {
                await Task.Delay(50);
                foreach (var adm in list)
                {
                    if (adm == null || string.IsNullOrEmpty(adm.AvatarUrl) || adm.AvatarBitmap != null) continue;
                    var cur = adm;
                    string finalUrl = ImageHelper.FormatQiniuUrl(cur.AvatarUrl, 72, 72);

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

        #endregion

        #region 关注/取关板块逻辑

        private async void BtnFollowAction_Click(object sender, RoutedEventArgs e)
        {
            await ToggleFollowBoardAsync();
        }

        private async void AppBarBtnFollowCmd_Click(object sender, RoutedEventArgs e)
        {
            await ToggleFollowBoardAsync();
        }

        private async Task ToggleFollowBoardAsync()
        {
            if (_baId <= 0 || string.IsNullOrEmpty(_token) || _isTogglingFollow) return;

            _isTogglingFollow = true;
            bool targetFollow = _currentBoard == null || !_currentBoard.IsFollowed;
            string toastMsg = null;

            try
            {
                ApiResult res;
                if (targetFollow)
                {
                    res = await BoardApi.FollowBoardAsync(_token, _baId);
                }
                else
                {
                    res = await BoardApi.UnfollowBoardAsync(_token, _baId);
                }

                if (res.IsSuccess)
                {
                    if (_currentBoard != null)
                    {
                        _currentBoard.IsFollowed = targetFollow;
                        _currentBoard.MemberNum += targetFollow ? 1 : -1;
                        if (_currentBoard.MemberNum < 0) _currentBoard.MemberNum = 0;
                        RenderBoardInfo(_currentBoard);
                    }
                    else
                    {
                        UpdateFollowButtonState(targetFollow);
                    }
                    toastMsg = targetFollow ? "关注板块成功" : "已取消关注板块";
                }
                else
                {
                    toastMsg = "操作失败: " + res.Msg;
                }
            }
            catch (Exception ex)
            {
                toastMsg = "操作异常: " + ex.Message;
            }
            finally
            {
                _isTogglingFollow = false;
            }

            if (toastMsg != null)
            {
                await ShowToastAsync(toastMsg);
            }
        }

        #endregion

        #region 事件与导航

        private async void PostsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var post = e.ClickedItem as CommunityPostItem;
            if (post != null && post.Id > 0)
            {
                var navArgs = new PostDetailNavigationArgs
                {
                    PostId = post.Id,
                    InitialPost = post,
                    Token = _token
                };

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Frame.Navigate(typeof(PostDetailPage), navArgs);
                });
            }
        }

        private async void BoardAvatar_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_currentBoard != null && !string.IsNullOrEmpty(_currentBoard.Avatar))
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Frame.Navigate(typeof(ImageViewerPage), _currentBoard.Avatar);
                });
            }
        }

        private async void OwnerCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_boardOwner != null && !string.IsNullOrEmpty(_boardOwner.UserId))
            {
                var navArgs = new UserDetailNavArgs
                {
                    UserId = _boardOwner.UserId,
                    Name = _boardOwner.DisplayName,
                    AvatarUrl = _boardOwner.AvatarUrl ?? ""
                };

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Frame.Navigate(typeof(UserDetailPage), navArgs);
                });
            }
            else
            {
                await ShowToastAsync("未获取到板块主用户 ID");
            }
        }

        private async void AdminItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            var admin = element != null ? (element.DataContext as BoardFollowerItem) : null;
            if (admin != null && !string.IsNullOrEmpty(admin.UserId))
            {
                var navArgs = new UserDetailNavArgs
                {
                    UserId = admin.UserId,
                    Name = admin.DisplayName,
                    AvatarUrl = admin.AvatarUrl ?? ""
                };

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Frame.Navigate(typeof(UserDetailPage), navArgs);
                });
            }
        }

        private void PostsListView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_postsScrollViewer == null)
            {
                _postsScrollViewer = FindVisualChild<ScrollViewer>(PostsListView);
                if (_postsScrollViewer != null)
                {
                    _postsScrollViewer.ViewChanged -= PostsScrollViewer_ViewChanged;
                    _postsScrollViewer.ViewChanged += PostsScrollViewer_ViewChanged;
                }
            }
        }

        private async void PostsScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_postsScrollViewer == null || _isLoadingPosts || _isLoadingMorePosts || !_hasMorePosts || string.IsNullOrEmpty(_token))
            {
                return;
            }

            // 滚动接近底部（距底 250px）时自动无感加载下一页
            if (_postsScrollViewer.ScrollableHeight > 0 &&
                _postsScrollViewer.VerticalOffset >= _postsScrollViewer.ScrollableHeight - 250)
            {
                await LoadMorePostsAsync();
            }
        }

        private async void AppBarBtnCreatePost_Click(object sender, RoutedEventArgs e)
        {
            var navArgs = new CreatePostNavArgs
            {
                BaId = _baId,
                BoardName = _currentBoard != null ? _currentBoard.DisplayName : ("板块 " + _baId),
                InitialContentType = 2
            };

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Frame.Navigate(typeof(CreatePostPage), navArgs);
            });
        }

        private T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            if (obj == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is T) return (T)child;
                T childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null) return childOfChild;
            }
            return null;
        }

        private void AppBarBtnCopyId_Click(object sender, RoutedEventArgs e)
        {
            if (_baId > 0)
            {
                Frame.Navigate(typeof(TextViewerPage), _baId.ToString());
            }
        }

        private async void AppBarBtnShare_Click(object sender, RoutedEventArgs e)
        {
            string bName = _currentBoard != null ? _currentBoard.DisplayName : ("板块 " + _baId);
            await ShowToastAsync(string.Format("【云湖板块】{0} (ID: {1})", bName, _baId));
        }

        private void BoardPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.IsOpen = false;
            }
        }

        #endregion

        private void UpdateEditBoardButtonVisibility()
        {
            bool isOwner = !string.IsNullOrEmpty(_selfUserId) &&
                           ((_currentBoard != null && !string.IsNullOrEmpty(_currentBoard.CreateBy) && _currentBoard.CreateBy == _selfUserId) ||
                            (_boardOwner != null && !string.IsNullOrEmpty(_boardOwner.UserId) && _boardOwner.UserId == _selfUserId));
            if (AppBarBtnEditBoard != null)
            {
                AppBarBtnEditBoard.Visibility = isOwner ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void AppBarBtnEditBoard_Click(object sender, RoutedEventArgs e)
        {
            if (_baId <= 0) return;
            var args = new EditBoardNavArgs
            {
                BaId = _baId,
                CurrentName = _currentBoard != null ? _currentBoard.DisplayName : "",
                CurrentAvatar = _currentBoard != null ? _currentBoard.Avatar : ""
            };
            Frame.Navigate(typeof(EditBoardPage), args);
        }

        private async Task ShowToastAsync(string message)
        {
            try
            {
                var dialog = new MessageDialog(message, "云湖板块");
                await dialog.ShowAsync();
            }
            catch { }
        }
    }
}
