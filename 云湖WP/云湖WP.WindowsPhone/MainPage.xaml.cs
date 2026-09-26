using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Phone.UI.Input;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Common;
using 云湖WP.Api.Community;
using 云湖WP.Api.Community.PostDetail;
using 云湖WP.Api.Conversation;
using 云湖WP.Api.Friend;
using 云湖WP.Api.Message;
using 云湖WP.Api.Sticky;
using 云湖WP.Api.User;
using 云湖WP.Api.WebSocket;
using 云湖WP.Token;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 云湖WP 主页面 (原生 Windows Phone 8.1 Metro Pivot 架构，纯几何直角设计)
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private const string SettingKeyToken = "UserToken";
        private const string SettingKeyEmail = "SavedEmail";
        private const string SettingKeyPhone = "SavedPhone";

        private string _userAccount = "";
        private string _userToken = "";

        private ScrollViewer _convScrollViewer;
        private ScrollViewer _communityScrollViewer;
        private DispatcherTimer _scrollDebounceTimer;

        // 会话列表数据源 (支持 DiffUtil 增量原地刷新)
        private ObservableCollection<ConversationItem> _conversationList = new ObservableCollection<ConversationItem>();

        // 社区动态数据源与筛选状态
        private ObservableCollection<CommunityPostItem> _communityPostList = new ObservableCollection<CommunityPostItem>();
        private string _currentCommunityFilter = "latest"; // "latest" (最新) 或 "hot" (热门)
        private int _communityPage = 1;
        private bool _isLoadingCommunity = false;
        private bool _hasMoreCommunity = true;

        // 置顶会话数据源
        private ObservableCollection<StickyItem> _stickyList = new ObservableCollection<StickyItem>();

        public MainPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            if (ConvListView != null)
            {
                ConvListView.ItemsSource = _conversationList;
            }
            if (StickyItemsControl != null)
            {
                StickyItemsControl.ItemsSource = _stickyList;
            }
            YunhuWebSocketService.Instance.OnNewMessageReceived += WebSocket_OnNewMessageReceived;
            InitScrollDebounceTimer();
        }

        private void InitScrollDebounceTimer()
        {
            if (_scrollDebounceTimer == null)
            {
                _scrollDebounceTimer = new DispatcherTimer();
                _scrollDebounceTimer.Interval = TimeSpan.FromMilliseconds(450);
                _scrollDebounceTimer.Tick += (s, args) =>
                {
                    _scrollDebounceTimer.Stop();
                    RestoreCommandBarOnScrollEnd();
                };
            }
        }

        private void HideCommandBarOnScroll()
        {
            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.IsOpen = false;
                this.BottomAppBar.ClosedDisplayMode = AppBarClosedDisplayMode.Minimal;
            }
            if (_scrollDebounceTimer != null)
            {
                _scrollDebounceTimer.Stop();
                _scrollDebounceTimer.Start();
            }
        }

        private void RestoreCommandBarOnScrollEnd()
        {
            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.ClosedDisplayMode = AppBarClosedDisplayMode.Compact;
            }
        }

        private void OnScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (e != null && e.IsIntermediate)
            {
                HideCommandBarOnScroll();
            }
            else
            {
                if (_scrollDebounceTimer != null)
                {
                    _scrollDebounceTimer.Stop();
                    _scrollDebounceTimer.Start();
                }
            }
        }

        private async void OnCommunityScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            OnScrollViewerViewChanged(sender, e);

            if (_communityScrollViewer == null || _isLoadingCommunity || !_hasMoreCommunity || string.IsNullOrEmpty(_userToken))
            {
                return;
            }

            // 滚动接近底部（距底 250px）时自动无感加载下一页，无需手动刷新或多余文字
            if (_communityScrollViewer.ScrollableHeight > 0 &&
                _communityScrollViewer.VerticalOffset >= _communityScrollViewer.ScrollableHeight - 250)
            {
                await LoadCommunityPostsAsync(isRefresh: false);
            }
        }

        private async void MainPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HideCommandBarOnScroll();

            if (MainPivot == null) return;

            if (MainPivot.SelectedIndex == 2) // 动态 Tab
            {
                if (AppBarBtnFilter != null) AppBarBtnFilter.Visibility = Visibility.Visible;
                if (AppBarBtnNewChat != null) AppBarBtnNewChat.Visibility = Visibility.Collapsed;
                if (AppBarBtnSearch != null) AppBarBtnSearch.Visibility = Visibility.Collapsed;

                if (_communityPostList.Count == 0 && !string.IsNullOrEmpty(_userToken))
                {
                    await LoadCommunityPostsAsync(isRefresh: true);
                }
            }
            else
            {
                if (AppBarBtnFilter != null) AppBarBtnFilter.Visibility = Visibility.Collapsed;
                if (AppBarBtnNewChat != null) AppBarBtnNewChat.Visibility = Visibility.Visible;
                if (AppBarBtnSearch != null) AppBarBtnSearch.Visibility = Visibility.Visible;
            }
        }

        private void ConvListView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_convScrollViewer == null && ConvListView != null)
            {
                _convScrollViewer = FindVisualChild<ScrollViewer>(ConvListView);
                if (_convScrollViewer != null)
                {
                    _convScrollViewer.ViewChanged -= OnScrollViewerViewChanged;
                    _convScrollViewer.ViewChanged += OnScrollViewerViewChanged;
                }
            }
        }

        private void CommunityListView_Loaded(object sender, RoutedEventArgs e)
        {
            if (CommunityListView != null)
            {
                CommunityListView.ItemsSource = _communityPostList;
                if (_communityScrollViewer == null)
                {
                    _communityScrollViewer = FindVisualChild<ScrollViewer>(CommunityListView);
                    if (_communityScrollViewer != null)
                    {
                        _communityScrollViewer.ViewChanged -= OnCommunityScrollViewerViewChanged;
                        _communityScrollViewer.ViewChanged += OnCommunityScrollViewerViewChanged;
                    }
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

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // 恢复底栏默认 Compact 显示模式
            RestoreCommandBarOnScrollEnd();

            // 注册硬件返回按键事件
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            // 如果是从子页面（如聊天界面）返回，且会话列表已有数据，直接保持原状态，跳过重复拉取与刷新
            if (e.NavigationMode == NavigationMode.Back && _conversationList.Count > 0)
            {
                return;
            }

            string initError = null;
            try
            {
                // 优先从加密 TokenManager 读取 Token 与账号
                _userToken = await TokenManager.GetTokenAsync();
                _userAccount = TokenManager.GetSavedAccount();

                string passedAccount = e.Parameter as string;
                if (!string.IsNullOrEmpty(passedAccount))
                {
                    _userAccount = passedAccount;
                }

                if (string.IsNullOrEmpty(_userToken))
                {
                    var localSettings = ApplicationData.Current.LocalSettings;
                    if (localSettings.Values.ContainsKey(SettingKeyToken))
                    {
                        _userToken = localSettings.Values[SettingKeyToken] as string;
                    }
                }

                if (string.IsNullOrEmpty(_userAccount))
                {
                    var localSettings = ApplicationData.Current.LocalSettings;
                    if (localSettings.Values.ContainsKey(SettingKeyEmail))
                    {
                        _userAccount = localSettings.Values[SettingKeyEmail] as string;
                    }
                    else if (localSettings.Values.ContainsKey(SettingKeyPhone))
                    {
                        _userAccount = localSettings.Values[SettingKeyPhone] as string;
                    }
                }

                if (string.IsNullOrEmpty(_userToken))
                {
                    Frame.Navigate(typeof(LoginPage));
                    return;
                }

                UpdateAccountDisplay();
                await LoadUserProfileAsync();
                await LoadConversationsAsync();
                await LoadStickyListAsync();
                PreloadAddressBookTitlesAsync();
            }
            catch (Exception ex)
            {
                initError = "页面加载异常: " + ex.Message;
            }

            if (initError != null)
            {
                await ShowToastAsync(initError);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;

            if (_scrollDebounceTimer != null)
            {
                _scrollDebounceTimer.Stop();
            }

            // 进入子页面（如聊天界面）时自动收起底部 CommandBar 菜单并复位
            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.IsOpen = false;
                this.BottomAppBar.ClosedDisplayMode = AppBarClosedDisplayMode.Compact;
            }
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            // 如果 Pivot 不是第一项，则返回到第一项“消息”
            if (MainPivot.SelectedIndex > 0)
            {
                e.Handled = true;
                MainPivot.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 更新基本账号显示
        /// </summary>
        private void UpdateAccountDisplay()
        {
            if (!string.IsNullOrEmpty(_userAccount))
            {
                TxtAccount.Text = string.Format("账号: {0}", _userAccount);
                TxtDisplayName.Text = _userAccount;
                if (_userAccount.Length > 0)
                {
                    TxtAvatarInitial.Text = _userAccount.Substring(0, 1).ToUpper();
                }
            }
        }

        /// <summary>
        /// 从服务器拉取最新个人信息 (GET /v1/user/info)
        /// </summary>
        private async Task LoadUserProfileAsync()
        {
            if (string.IsNullOrEmpty(_userToken)) return;

            try
            {
                AppLogger.Log("UserProfile", "开始加载个人资料...");
                var selfRes = await UserApi.GetSelfInfoAsync(_userToken);
                if (selfRes != null && selfRes.IsSuccess && selfRes.Data != null)
                {
                    var data = selfRes.Data;
                    if (!string.IsNullOrEmpty(data.Name))
                    {
                        TxtDisplayName.Text = data.Name;
                        TxtAvatarInitial.Text = data.AvatarLetter;
                    }

                    if (!string.IsNullOrEmpty(data.Id))
                    {
                        TxtAccount.Text = string.Format("UID: {0}", data.Id);
                    }

                    TxtVipStatus.Text = data.IsVip ? "VIP 会员" : "普通用户";
                    TxtCoinCount.Text = string.Format("金币: {0}", data.Coin);

                    // 异步加载用户个人头像
                    if (!string.IsNullOrEmpty(data.AvatarUrl))
                    {
                        LoadUserAvatarAsync(data.AvatarUrl);
                    }
                    AppLogger.Log("UserProfile", "个人资料加载成功: " + data.DisplayName);

                    // 启动 WebSocket 实时长连接
                    if (!YunhuWebSocketService.Instance.IsConnected)
                    {
                        await YunhuWebSocketService.Instance.StartAsync(data.Id, _userToken);
                    }
                }
                else
                {
                    AppLogger.Log("UserProfile", "获取个人资料返回未成功: " + (selfRes != null ? selfRes.Msg : "null"));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("UserProfile", "LoadUserProfile exception: " + ex.Message);
            }
        }

        private void LoadUserAvatarAsync(string avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl) || ImageLoader.DisableAllImages) return;
            string finalUrl = ImageHelper.FormatQiniuUrl(avatarUrl, 120, 120);

            Task.Run(async () =>
            {
                try
                {
                    byte[] bytes = await ImageLoader.GetImageBytesAsync(finalUrl);
                    if (bytes != null && bytes.Length > 0)
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
                        {
                            var bmp = await ImageLoader.BytesToBitmapImageAsync(bytes, 120, 120);
                            if (bmp != null)
                            {
                                ImgUserAvatar.Source = bmp;
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("LoadUserAvatarAsync failed: " + ex.Message);
                }
            });
        }

        /// <summary>
        /// 通过 Protobuf 协议从服务器拉取最新会话列表并执行增量比对 (POST /v1/conversation/list)
        /// </summary>
        private async Task LoadConversationsAsync()
        {
            if (string.IsNullOrEmpty(_userToken)) return;

            string convError = null;
            try
            {
                ConvProgressBar.Visibility = Visibility.Visible;
                var res = await ConversationApi.GetConversationListAsync(_userToken);
                ConvProgressBar.Visibility = Visibility.Collapsed;

                if (res.IsSuccess && res.Conversations != null)
                {
                    // 使用 DiffUtil 原地增量同步，保留滚动条位置与已解码头像，消除列表全量重建与闪烁
                    ConversationDiffUtil.ApplyDiff(_conversationList, res.Conversations);
                    EmptyConvPanel.Visibility = _conversationList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                    // 启动后台头像并发平滑预加载 (受控于 ImageLoader 线程数设置及 Referer: http://myapp.jwznb.com)
                    PreloadAvatars(_conversationList);

                    // 同步更新系统磁贴未读数字角标
                    UpdateTotalBadge();
                }
                else
                {
                    if (_conversationList.Count == 0)
                    {
                        EmptyConvPanel.Visibility = Visibility.Visible;
                    }
                    if (!res.IsSuccess && !string.IsNullOrEmpty(res.Msg))
                    {
                        convError = "会话列表: " + res.Msg;
                    }
                }
            }
            catch (Exception ex)
            {
                ConvProgressBar.Visibility = Visibility.Collapsed;
                convError = "加载会话错误: " + ex.Message;
            }

            if (convError != null)
            {
                await ShowToastAsync(convError);
            }
        }

        /// <summary>
        /// WebSocket 实时推送新消息时的增量刷新处理
        /// </summary>
        private void WebSocket_OnNewMessageReceived(ChatMessageItem msg)
        {
            if (msg == null) return;

            // 使用 DiffUtil 高性能增量置顶并更新未读数与最新消息摘要
            ConversationDiffUtil.ApplyPushMessage(_conversationList, msg, YunhuWebSocketService.Instance.CurrentActiveChatId);
            EmptyConvPanel.Visibility = _conversationList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // 预加载可能新增会话项的头像
            PreloadAvatars(_conversationList);

            // 同步更新系统磁贴未读数字角标
            UpdateTotalBadge();
        }

        /// <summary>
        /// 计算所有会话未读数总和并更新系统 Badge 角标
        /// </summary>
        private void UpdateTotalBadge()
        {
            try
            {
                int total = 0;
                if (_conversationList != null)
                {
                    foreach (var conv in _conversationList)
                    {
                        if (conv != null && conv.UnreadCount > 0)
                        {
                            total += conv.UnreadCount;
                        }
                    }
                }
                NotificationHelper.UpdateBadge(total);
            }
            catch { }
        }

        /// <summary>
        /// 批量预加载会话头像 (受控于并发信号量，带 96x96 七牛云参数和 Referer 头，平滑调度)
        /// </summary>
        private void PreloadAvatars(IEnumerable<ConversationItem> items)
        {
            if (items == null || ImageLoader.DisableAllImages) return;

            var list = new List<ConversationItem>(items);

            Task.Run(async () =>
            {
                // 短暂延迟 150ms，优先保证页面 Pivot 滑动与列表渲染 60 FPS 流畅呈现
                await Task.Delay(150);

                foreach (var item in list)
                {
                    if (ImageLoader.DisableAllImages) break;
                    if (item == null || string.IsNullOrEmpty(item.AvatarUrl) || item.AvatarBitmap != null) continue;

                    var currentItem = item;
                    string finalUrl = ImageHelper.FormatQiniuUrl(currentItem.AvatarUrl, 96, 96);

                    try
                    {
                        byte[] bytes = await ImageLoader.GetImageBytesAsync(finalUrl);
                        if (bytes != null && bytes.Length > 0)
                        {
                            // 使用 Low 优先级派发到 UI 线程解码与更新，绝不阻塞用户触控与滚动
                            await Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
                            {
                                var bmp = await ImageLoader.BytesToBitmapImageAsync(bytes, 96, 96);
                                if (bmp != null)
                                {
                                    currentItem.AvatarBitmap = bmp;
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Preload avatar failed for " + currentItem.DisplayTitle + ": " + ex.Message);
                    }

                    // 每次微小休眠 20ms，平滑 CPU 占用与网络带宽
                    await Task.Delay(20);
                }
            });
        }

        /// <summary>
        /// 从服务器拉取社区动态列表 (POST /v1/community/posts/post-list 或 post-list-recommend)
        /// 支持首屏刷新与滑到底部自动增量分页加载
        /// </summary>
        private async Task LoadCommunityPostsAsync(bool isRefresh = false)
        {
            if (string.IsNullOrEmpty(_userToken) || _isLoadingCommunity) return;
            if (!isRefresh && !_hasMoreCommunity) return;

            _isLoadingCommunity = true;
            if (CommunityProgressBar != null) CommunityProgressBar.Visibility = Visibility.Visible;

            int targetPage = isRefresh ? 1 : (_communityPage + 1);

            string errMsg = null;
            try
            {
                CommunityPostListResult res;
                if (_currentCommunityFilter == "hot")
                {
                    res = await CommunityApi.GetRecommendPostListAsync(_userToken, targetPage, 20);
                }
                else
                {
                    res = await CommunityApi.GetPostListAsync(_userToken, typ: 4, baId: 0, page: targetPage, size: 20);
                }

                if (res.IsSuccess && res.Posts != null)
                {
                    if (isRefresh)
                    {
                        _communityPage = 1;
                        _communityPostList.Clear();
                    }
                    else
                    {
                        _communityPage = targetPage;
                    }

                    if (res.Posts.Count < 20)
                    {
                        _hasMoreCommunity = false;
                    }
                    else
                    {
                        _hasMoreCommunity = true;
                    }

                    foreach (var p in res.Posts)
                    {
                        _communityPostList.Add(p);
                    }

                    if (EmptyCommunityPanel != null)
                    {
                        EmptyCommunityPanel.Visibility = (_communityPostList.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
                    }

                    // 预加载动态作者头像
                    PreloadCommunityAvatars(res.Posts);
                }
                else
                {
                    if (!isRefresh)
                    {
                        _hasMoreCommunity = false;
                    }
                    if (_communityPostList.Count == 0 && EmptyCommunityPanel != null)
                    {
                        EmptyCommunityPanel.Visibility = Visibility.Visible;
                    }
                    if (!res.IsSuccess && !string.IsNullOrEmpty(res.Msg))
                    {
                        errMsg = "加载动态失败: " + res.Msg;
                    }
                }
            }
            catch (Exception ex)
            {
                errMsg = "获取社区动态异常: " + ex.Message;
            }
            finally
            {
                if (CommunityProgressBar != null) CommunityProgressBar.Visibility = Visibility.Collapsed;
                _isLoadingCommunity = false;
            }

            if (errMsg != null && isRefresh)
            {
                await ShowToastAsync(errMsg);
            }
        }

        private void PreloadCommunityAvatars(IEnumerable<CommunityPostItem> posts)
        {
            if (posts == null || ImageLoader.DisableAllImages) return;

            var list = new List<CommunityPostItem>(posts);

            Task.Run(async () =>
            {
                await Task.Delay(100);

                foreach (var p in list)
                {
                    if (ImageLoader.DisableAllImages) break;
                    if (p == null || string.IsNullOrEmpty(p.SenderAvatar) || p.AvatarBitmap != null) continue;

                    var cur = p;
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

        private async void FilterLatest_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCommunityFilter == "latest") return;
            _currentCommunityFilter = "latest";
            if (FlyoutItemLatest != null) FlyoutItemLatest.Text = "最新文章 (当前)";
            if (FlyoutItemHot != null) FlyoutItemHot.Text = "热门推荐";
            await LoadCommunityPostsAsync(isRefresh: true);
        }

        private async void FilterHot_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCommunityFilter == "hot") return;
            _currentCommunityFilter = "hot";
            if (FlyoutItemLatest != null) FlyoutItemLatest.Text = "最新文章";
            if (FlyoutItemHot != null) FlyoutItemHot.Text = "热门推荐 (当前)";
            await LoadCommunityPostsAsync(isRefresh: true);
        }

        private void CommunityListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var post = e.ClickedItem as CommunityPostItem;
            if (post != null)
            {
                ShowPostDetails(post);
            }
        }

        private void ShowPostDetails(CommunityPostItem post)
        {
            if (post == null) return;
            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.IsOpen = false;
            }

            var args = new PostDetailNavigationArgs
            {
                PostId = post.Id,
                InitialPost = post,
                Token = _userToken
            };
            Frame.Navigate(typeof(PostDetailPage), args);
        }

        private async Task ShowToastAsync(string message)
        {
            var dialog = new MessageDialog(message, "云湖");
            await dialog.ShowAsync();
        }

        #region 底部 AppBar 按钮点击事件

        private async void AppBarBtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (MainPivot != null && MainPivot.SelectedIndex == 2)
            {
                await LoadCommunityPostsAsync(isRefresh: true);
                await ShowToastAsync("动态已刷新");
            }
            else
            {
                await LoadUserProfileAsync();
                await LoadConversationsAsync();
                await LoadStickyListAsync();
                await ShowToastAsync("数据已刷新");
            }
        }

        private async void AppBarBtnNewChat_Click(object sender, RoutedEventArgs e)
        {
            await ShowToastAsync("发起新会话功能正在适配中...");
        }

        private async void AppBarBtnSearch_Click(object sender, RoutedEventArgs e)
        {
            await ShowToastAsync("搜索功能正在适配中...");
        }

        private void AppBarBtnSettings_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SettingsPage));
        }

        private async void AppBarBtnLogout_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new MessageDialog("确定要退出当前登录的云湖账号吗？", "退出登录");
            dialog.Commands.Add(new UICommand("退出", cmd =>
            {
                TokenManager.ClearToken();
                var localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values.Remove(SettingKeyToken);

                Frame.Navigate(typeof(LoginPage));
            }));
            dialog.Commands.Add(new UICommand("取消"));
            await dialog.ShowAsync();
        }

        #endregion

        #region 列表项与交互事件

        private void OpenConversation(ConversationItem conv)
        {
            if (conv == null) return;

            // 自动收起底部 CommandBar
            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.IsOpen = false;
            }

            // 清空本地未读计数
            if (conv.UnreadCount > 0)
            {
                conv.UnreadCount = 0;
                conv.NotifyAllChanged();
            }

            var args = new ChatNavigationArgs
            {
                ChatId = conv.ChatId,
                ChatType = conv.ChatType,
                Title = conv.DisplayTitle,
                AvatarUrl = conv.AvatarUrl,
                Token = _userToken
            };
            Frame.Navigate(typeof(ChatPage), args);
        }

        private void ConvListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var conv = e.ClickedItem as ConversationItem;
            if (conv != null)
            {
                OpenConversation(conv);
            }
        }

        private void ChatItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element != null && element.DataContext is ConversationItem)
            {
                var item = element.DataContext as ConversationItem;
                OpenConversation(item);
            }
        }

        private void ContactCategory_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element == null) return;

            string tag = element.Tag as string;
            if (string.Equals(tag, "Friends", StringComparison.OrdinalIgnoreCase))
            {
                Frame.Navigate(typeof(FriendsPage));
            }
            else if (string.Equals(tag, "Groups", StringComparison.OrdinalIgnoreCase))
            {
                Frame.Navigate(typeof(GroupsPage));
            }
            else if (string.Equals(tag, "Bots", StringComparison.OrdinalIgnoreCase))
            {
                Frame.Navigate(typeof(BotsPage));
            }
        }

        private void StickyItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element != null && element.DataContext is StickyItem)
            {
                var item = element.DataContext as StickyItem;
                if (item != null)
                {
                    if (this.BottomAppBar != null)
                    {
                        this.BottomAppBar.IsOpen = false;
                    }

                    var args = new ChatNavigationArgs
                    {
                        ChatId = item.ChatId,
                        ChatType = item.ChatType,
                        Title = item.DisplayName,
                        AvatarUrl = item.AvatarUrl,
                        Token = _userToken
                    };
                    Frame.Navigate(typeof(ChatPage), args);
                }
            }
        }

        private async Task LoadStickyListAsync()
        {
            if (string.IsNullOrEmpty(_userToken)) return;
            try
            {
                var res = await StickyApi.GetStickyListAsync(_userToken);
                if (res.IsSuccess && res.StickyList != null)
                {
                    _stickyList.Clear();
                    foreach (var item in res.StickyList)
                    {
                        _stickyList.Add(item);
                        if (item != null && !string.IsNullOrEmpty(item.ChatId) && !string.IsNullOrEmpty(item.ChatName))
                        {
                            NotificationHelper.RegisterChatTitle(item.ChatId, item.ChatName);
                        }
                    }

                    if (EmptyStickyText != null)
                    {
                        EmptyStickyText.Visibility = _stickyList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    }

                    PreloadStickyAvatars(res.StickyList);
                }
                else
                {
                    if (EmptyStickyText != null)
                    {
                        EmptyStickyText.Visibility = _stickyList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }
            catch
            {
                if (EmptyStickyText != null)
                {
                    EmptyStickyText.Visibility = _stickyList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// 后台静默预加载群聊与好友通讯录名称，确保系统通知与会话列表始终能显示真实群聊名称
        /// </summary>
        private void PreloadAddressBookTitlesAsync()
        {
            if (string.IsNullOrEmpty(_userToken)) return;

            Task.Run(async () =>
            {
                try
                {
                    var book = await FriendApi.GetAddressBookListAsync(_userToken);
                    if (book != null && book.IsSuccess)
                    {
                        if (book.Groups != null)
                        {
                            foreach (var g in book.Groups)
                            {
                                if (g != null && !string.IsNullOrEmpty(g.ChatId) && !string.IsNullOrEmpty(g.DisplayName))
                                {
                                    NotificationHelper.RegisterChatTitle(g.ChatId, g.DisplayName);
                                }
                            }
                        }
                        if (book.Friends != null)
                        {
                            foreach (var f in book.Friends)
                            {
                                if (f != null && !string.IsNullOrEmpty(f.ChatId) && !string.IsNullOrEmpty(f.DisplayName))
                                {
                                    NotificationHelper.RegisterChatTitle(f.ChatId, f.DisplayName);
                                }
                            }
                        }
                        if (book.Bots != null)
                        {
                            foreach (var b in book.Bots)
                            {
                                if (b != null && !string.IsNullOrEmpty(b.ChatId) && !string.IsNullOrEmpty(b.DisplayName))
                                {
                                    NotificationHelper.RegisterChatTitle(b.ChatId, b.DisplayName);
                                }
                            }
                        }
                    }
                }
                catch { }
            });
        }

        private void PreloadStickyAvatars(IEnumerable<StickyItem> items)
        {
            if (ImageLoader.DisableAllImages || items == null) return;
            var list = new List<StickyItem>(items);

            Task.Run(async () =>
            {
                await Task.Delay(50);
                foreach (var item in list)
                {
                    if (item == null || string.IsNullOrEmpty(item.AvatarUrl) || item.AvatarBitmap != null) continue;
                    var cur = item;
                    string finalUrl = ImageHelper.FormatQiniuUrl(cur.AvatarUrl, 96, 96);

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

        private async void ContactItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await ShowToastAsync("联系人详情正在开发中...");
        }

        private void UserProfileCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Frame.Navigate(typeof(MyProfilePage));
        }

        private async void ProfileMenu_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await ShowToastAsync("该模块正在适配中...");
        }

        private void SettingsMenu_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Frame.Navigate(typeof(SettingsPage));
        }

        #endregion
    }
}
