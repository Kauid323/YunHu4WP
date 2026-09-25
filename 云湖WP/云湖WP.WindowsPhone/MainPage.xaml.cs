using System;
using System.Collections.Generic;
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
using 云湖WP.Api.Conversation;
using 云湖WP.Api.Message;
using 云湖WP.Api.User;
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
        private DispatcherTimer _scrollDebounceTimer;

        public MainPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
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

        private void MainPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HideCommandBarOnScroll();
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
            if (e.NavigationMode == NavigationMode.Back && ConvListView.ItemsSource != null)
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

        private async void LoadUserAvatarAsync(string avatarUrl)
        {
            try
            {
                var bmp = await ImageLoader.LoadAvatarAsync(avatarUrl, 120, 120);
                if (bmp != null)
                {
                    ImgUserAvatar.Source = bmp;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadUserAvatarAsync failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 通过 Protobuf 协议从服务器拉取最新会话列表 (POST /v1/conversation/list)
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

                if (res.IsSuccess && res.Conversations != null && res.Conversations.Count > 0)
                {
                    ConvListView.ItemsSource = res.Conversations;
                    EmptyConvPanel.Visibility = Visibility.Collapsed;

                    // 启动后台头像并发平滑预加载 (受控于 ImageLoader 线程数设置及 Referer: http://myapp.jwznb.com)
                    PreloadAvatars(res.Conversations);
                }
                else
                {
                    ConvListView.ItemsSource = null;
                    EmptyConvPanel.Visibility = Visibility.Visible;
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

        private async Task ShowToastAsync(string message)
        {
            var dialog = new MessageDialog(message, "云湖");
            await dialog.ShowAsync();
        }

        #region 底部 AppBar 按钮点击事件

        private async void AppBarBtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadUserProfileAsync();
            await LoadConversationsAsync();
            await ShowToastAsync("数据已刷新");
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

        private async void ContactCategory_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await ShowToastAsync("分类列表功能即将上线");
        }

        private async void ContactItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await ShowToastAsync("联系人详情正在开发中...");
        }

        private void UserProfileCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Frame.Navigate(typeof(UserDetailPage), new 云湖WP.Api.User.Info.UserDetailNavArgs
            {
                UserId = "",
                Name = TxtDisplayName.Text,
                AvatarUrl = ""
            });
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
