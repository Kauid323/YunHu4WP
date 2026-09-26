using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Phone.UI.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Common;
using 云湖WP.Api.Community.PostDetail;
using 云湖WP.Api.Message;
using 云湖WP.Api.User;
using 云湖WP.Api.User.Info;
using 云湖WP.Api.WebSocket;
using 云湖WP.Token;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 云湖WP 聊天详情页面 (纯正 Metro 现代直角几何风格，支持抽拉式 CommandBar 与本地相册直传)
    /// </summary>
    public sealed partial class ChatPage : Page
#if WINDOWS_PHONE_APP
        , IFileOpenPickerContinuable
#endif
    {
        private string _token = "";
        private string _chatId = "";
        private int _chatType = 1;
        private string _title = "聊天";
        private string _avatarUrl = "";

        private ObservableCollection<ChatMessageItem> _messageList = new ObservableCollection<ChatMessageItem>();
        private ScrollViewer _chatScrollViewer;
        private bool _isLoadingHistory = false;
        private bool _hasMoreHistory = true;
        private bool _isSending = false;
        private bool _isInitialLoadDone = false;
        private DateTime _lastLoadMoreTime = DateTime.MinValue;
        private CancellationTokenSource _uploadCts = null;

        // 全局本机用户头像与发送者头像缓存（跨会话复用，进入聊天无需重复拉取，发消息/加载历史即时显示）
        private static BitmapImage _selfAvatarBitmap = null;
        private static string _selfAvatarUrl = "";
        private static readonly Dictionary<string, BitmapImage> _senderAvatarBitmapCache = new Dictionary<string, BitmapImage>();

        public ChatPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // 注册硬件返回按键事件与软键盘面板监听
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;
            InputPane.GetForCurrentView().Showing += InputPane_Showing;
            InputPane.GetForCurrentView().Hiding += InputPane_Hiding;

            // 无论进入还是返回，始终确保 CommandBar 收起
            if (ChatCommandBar != null) ChatCommandBar.IsOpen = false;

            // 如果是从子页面（如用户详情页、大图查看器）返回，且已有消息列表，直接保持原状态，不重复请求或刷新
            if (e.NavigationMode == NavigationMode.Back && _messageList.Count > 0)
            {
                return;
            }

            // 每次新进入会话，先清空可能残留的消息列表并重置状态
            _messageList.Clear();
            if (EmptyMsgPanel != null)
            {
                EmptyMsgPanel.Visibility = Visibility.Collapsed;
            }

            // 读取导航参数
            var args = e.Parameter as ChatNavigationArgs;
            if (args != null)
            {
                _chatId = args.ChatId ?? "";
                _chatType = args.ChatType;
                _title = args.Title ?? "";
                _avatarUrl = args.AvatarUrl ?? "";
                _token = args.Token ?? "";
            }

            if (string.IsNullOrEmpty(_token))
            {
                _token = await TokenManager.GetTokenAsync();
            }

            if (string.IsNullOrEmpty(_title) || _title == "群聊消息" || _title == "云湖好友" || _title == "云湖会话")
            {
                _title = NotificationHelper.GetChatTitle(_chatId, _chatType);
            }
            else
            {
                NotificationHelper.RegisterChatTitle(_chatId, _title);
            }

            TxtChatTitle.Text = !string.IsNullOrEmpty(_title) ? _title : "云湖会话";

            ChatListView.ItemsSource = _messageList;

            _hasMoreHistory = true;
            _isLoadingHistory = false;
            _isInitialLoadDone = false;

            // 设置当前活跃会话 ID 并注册 WebSocket 实时消息监听
            YunhuWebSocketService.Instance.CurrentActiveChatId = _chatId ?? "";
            YunhuWebSocketService.Instance.OnNewMessageReceived -= OnWebSocketNewMessageReceived;
            YunhuWebSocketService.Instance.OnNewMessageReceived += OnWebSocketNewMessageReceived;
            YunhuWebSocketService.Instance.OnMessageEdited -= OnWebSocketMessageEdited;
            YunhuWebSocketService.Instance.OnMessageEdited += OnWebSocketMessageEdited;

            AppLogger.Log("ChatPage", string.Format("OnNavigatedTo: ChatId={0}, Title={1}", _chatId, _title));
            await LoadHistoryMessagesAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
            InputPane.GetForCurrentView().Showing -= InputPane_Showing;
            InputPane.GetForCurrentView().Hiding -= InputPane_Hiding;

            // 解绑 WebSocket 实时消息监听并复位当前活跃会话 ID
            YunhuWebSocketService.Instance.OnNewMessageReceived -= OnWebSocketNewMessageReceived;
            YunhuWebSocketService.Instance.OnMessageEdited -= OnWebSocketMessageEdited;
            if (YunhuWebSocketService.Instance.CurrentActiveChatId == _chatId)
            {
                YunhuWebSocketService.Instance.CurrentActiveChatId = "";
            }

            if (_chatScrollViewer != null)
            {
                _chatScrollViewer.ViewChanged -= ChatScrollViewer_ViewChanged;
                _chatScrollViewer = null;
            }

            // 退出聊天界面返回主界面 (MainPage) 时清空消息列表与会话状态
            // 确保下次进入其他会话时不会闪烁或显示上一会话的历史消息
            // 注意：如果是跳转到用户详情 (UserDetailPage) 或大图查看器 (ImageViewerPage)，则保留消息不执行清空
            if (e.SourcePageType == typeof(MainPage))
            {
                _messageList.Clear();
                _chatId = "";
                _chatType = 1;
                _title = "";
                _avatarUrl = "";
                _hasMoreHistory = true;
                _isLoadingHistory = false;
                _isInitialLoadDone = false;
                if (TxtChatTitle != null)
                {
                    TxtChatTitle.Text = "云湖聊天";
                }
                if (EmptyMsgPanel != null)
                {
                    EmptyMsgPanel.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// 收到 WebSocket 实时推送的新聊天消息 (带全类型强力去重机制，防止自发附件/消息与推送产生重复)
        /// </summary>
        private void OnWebSocketNewMessageReceived(ChatMessageItem msg)
        {
            if (msg == null) return;
            if (msg.ChatId != _chatId || msg.ChatType != _chatType) return;

            // 1. 全局精确查重：按 MsgId 匹配
            if (!string.IsNullOrEmpty(msg.MsgId))
            {
                for (int i = 0; i < _messageList.Count; i++)
                {
                    var existing = _messageList[i];
                    if (existing != null && existing.MsgId == msg.MsgId)
                    {
                        if (msg.MsgSeq > 0) existing.MsgSeq = msg.MsgSeq;
                        if (msg.SendTime > 0) existing.SendTime = msg.SendTime;
                        if (msg.IsVideoMsg && !string.IsNullOrEmpty(msg.ExtractedVideoUrl) && string.IsNullOrEmpty(existing.VideoUrl))
                        {
                            existing.VideoUrl = msg.VideoUrl;
                            existing.InitParsedData();
                        }
                        return;
                    }
                }
            }

            // 2. 全局精确查重：按 MsgSeq 匹配（MsgSeq > 0 且在会话内唯一）
            if (msg.MsgSeq > 0)
            {
                for (int i = 0; i < _messageList.Count; i++)
                {
                    var existing = _messageList[i];
                    if (existing != null && existing.MsgSeq > 0 && existing.MsgSeq == msg.MsgSeq)
                    {
                        if (!string.IsNullOrEmpty(msg.MsgId)) existing.MsgId = msg.MsgId;
                        if (msg.SendTime > 0) existing.SendTime = msg.SendTime;
                        return;
                    }
                }
            }

            // 3. 我方自发消息/附件智能匹配去重（处理本地乐观插入导致 MsgId 未对齐或推送稍后到达的情况）
            if (msg.IsSelf)
            {
                int searchStart = _messageList.Count - 1;
                int searchEnd = Math.Max(0, _messageList.Count - 20);
                for (int i = searchStart; i >= searchEnd; i--)
                {
                    var existing = _messageList[i];
                    if (existing == null || !existing.IsSelf) continue;

                    bool isMatch = false;

                    // 视频消息匹配 (ContentType == 10 或 IsVideoMsg)
                    if ((msg.IsVideoMsg || msg.ContentType == 10) && (existing.IsVideoMsg || existing.ContentType == 10))
                    {
                        string msgVideo = !string.IsNullOrEmpty(msg.ExtractedVideoUrl) ? msg.ExtractedVideoUrl : msg.VideoUrl;
                        string exVideo = !string.IsNullOrEmpty(existing.ExtractedVideoUrl) ? existing.ExtractedVideoUrl : existing.VideoUrl;
                        if (!string.IsNullOrEmpty(msgVideo) && !string.IsNullOrEmpty(exVideo))
                        {
                            if (msgVideo == exVideo || msgVideo.Contains(exVideo) || exVideo.Contains(msgVideo))
                            {
                                isMatch = true;
                            }
                        }
                        if (!isMatch && !string.IsNullOrEmpty(msg.FileName) && !string.IsNullOrEmpty(existing.FileName) && msg.FileName == existing.FileName)
                        {
                            isMatch = true;
                        }
                    }
                    // 图片消息匹配 (ContentType == 2 或 IsImageMsg)
                    else if ((msg.IsImageMsg || msg.ContentType == 2) && (existing.IsImageMsg || existing.ContentType == 2))
                    {
                        string msgImg = !string.IsNullOrEmpty(msg.ExtractedImageUrl) ? msg.ExtractedImageUrl : msg.ImageUrl;
                        string exImg = !string.IsNullOrEmpty(existing.ExtractedImageUrl) ? existing.ExtractedImageUrl : existing.ImageUrl;
                        if (!string.IsNullOrEmpty(msgImg) && !string.IsNullOrEmpty(exImg))
                        {
                            if (msgImg == exImg || msgImg.Contains(exImg) || exImg.Contains(msgImg))
                            {
                                isMatch = true;
                            }
                        }
                        if (!isMatch && !string.IsNullOrEmpty(msg.Text) && !string.IsNullOrEmpty(existing.Text) && msg.Text == existing.Text)
                        {
                            isMatch = true;
                        }
                    }
                    // 文件消息匹配 (ContentType == 3/4/5 或 IsFileMsg)
                    else if ((msg.IsFileMsg || msg.ContentType == 3 || msg.ContentType == 4 || msg.ContentType == 5) && 
                             (existing.IsFileMsg || existing.ContentType == 3 || existing.ContentType == 4 || existing.ContentType == 5))
                    {
                        if (!string.IsNullOrEmpty(msg.FileUrl) && !string.IsNullOrEmpty(existing.FileUrl) && msg.FileUrl == existing.FileUrl)
                        {
                            isMatch = true;
                        }
                        else if (!string.IsNullOrEmpty(msg.FileName) && !string.IsNullOrEmpty(existing.FileName) && msg.FileName == existing.FileName)
                        {
                            isMatch = true;
                        }
                    }
                    // 普通文本消息匹配
                    else if (existing.ContentType == msg.ContentType && !string.IsNullOrEmpty(existing.Text) && existing.Text == msg.Text)
                    {
                        long timeDiff = Math.Abs(existing.SendTime - msg.SendTime);
                        if (timeDiff < 120000 || existing.SendTime == 0 || msg.SendTime == 0)
                        {
                            isMatch = true;
                        }
                    }

                    if (isMatch)
                    {
                        // 命中重复，合并更新服务器元数据并直接返回
                        if (!string.IsNullOrEmpty(msg.MsgId)) existing.MsgId = msg.MsgId;
                        if (msg.MsgSeq > 0) existing.MsgSeq = msg.MsgSeq;
                        if (msg.SendTime > 0) existing.SendTime = msg.SendTime;
                        if (!string.IsNullOrEmpty(msg.VideoUrl) && string.IsNullOrEmpty(existing.VideoUrl)) existing.VideoUrl = msg.VideoUrl;
                        if (!string.IsNullOrEmpty(msg.ImageUrl) && string.IsNullOrEmpty(existing.ImageUrl)) existing.ImageUrl = msg.ImageUrl;
                        if (!string.IsNullOrEmpty(msg.FileUrl) && string.IsNullOrEmpty(existing.FileUrl)) existing.FileUrl = msg.FileUrl;
                        existing.InitParsedData();
                        return;
                    }
                }
            }

            // 绑定或预拉取发送者头像
            if (msg.IsSelf && _selfAvatarBitmap != null)
            {
                msg.SenderAvatarBitmap = _selfAvatarBitmap;
                if (string.IsNullOrEmpty(msg.SenderAvatarUrl)) msg.SenderAvatarUrl = _selfAvatarUrl;
            }
            else if (!string.IsNullOrEmpty(msg.SenderAvatarUrl))
            {
                string finalUrl = ImageHelper.FormatQiniuUrl(msg.SenderAvatarUrl, 72, 72);
                BitmapImage cachedBmp;
                if (_senderAvatarBitmapCache.TryGetValue(finalUrl, out cachedBmp))
                {
                    msg.SenderAvatarBitmap = cachedBmp;
                }
                else
                {
                    PreloadSenderAvatars(new List<ChatMessageItem> { msg });
                }
            }

            _messageList.Add(msg);
            if (EmptyMsgPanel != null)
            {
                EmptyMsgPanel.Visibility = Visibility.Collapsed;
            }

            ScrollToBottom(true);
        }

        /// <summary>
        /// 收到 WebSocket 消息编辑实时推送
        /// </summary>
        private void OnWebSocketMessageEdited(ChatMessageItem msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.MsgId)) return;
            if (msg.ChatId != _chatId || msg.ChatType != _chatType) return;

            for (int i = 0; i < _messageList.Count; i++)
            {
                var existing = _messageList[i];
                if (existing != null && existing.MsgId == msg.MsgId)
                {
                    existing.Text = msg.Text;
                    break;
                }
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

        private void ChatListView_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureScrollViewerAttached();
        }

        private void EnsureScrollViewerAttached()
        {
            if (_chatScrollViewer == null && ChatListView != null)
            {
                _chatScrollViewer = FindVisualChild<ScrollViewer>(ChatListView);
                if (_chatScrollViewer != null)
                {
                    _chatScrollViewer.ViewChanged -= ChatScrollViewer_ViewChanged;
                    _chatScrollViewer.ViewChanged += ChatScrollViewer_ViewChanged;
                }
            }
        }

        private async void ChatScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            // 只在惯性/拖拽完全停止后的最终帧（IsIntermediate==false）判断，彻底避免惯性期间多次触发
            if (e.IsIntermediate) return;

            var sv = sender as ScrollViewer;
            if (sv == null) return;

            // 严格防护：初次加载完成、当前无加载任务、有更多历史数据、有内容
            if (!_isInitialLoadDone || _isLoadingHistory || !_hasMoreHistory || _messageList.Count == 0) return;

            // 到达顶部：VerticalOffset == 0 即触发（消除之前的 >15 二次条件）
            if (sv.VerticalOffset <= 1.0 && sv.ScrollableHeight > 50)
            {
                // 节流：距上次加载超过 1500ms 才再次触发
                if ((DateTime.Now - _lastLoadMoreTime).TotalMilliseconds > 1500)
                {
                    AppLogger.Log("ChatPage", "Auto-trigger LoadMoreHistory by scroll top.");
                    await LoadMoreHistoryMessagesAsync();
                }
            }
        }

        // 保留此方法防止旧版 XAML 编译缓存残留引用（XAML 中已去掉 Tapped 绑定）
        private async void TopLoadMoreBorder_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await LoadMoreHistoryMessagesAsync();
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

        /// <summary>
        /// 从服务器拉取历史聊天消息 (POST /v1/msg/list-message-by-seq)
        /// </summary>
        private async Task LoadHistoryMessagesAsync()
        {
            if (string.IsNullOrEmpty(_token))
            {
                _token = await TokenManager.GetTokenAsync();
            }

            if (string.IsNullOrEmpty(_token) || string.IsNullOrEmpty(_chatId))
            {
                AppLogger.Log("ChatPage", string.Format("LoadHistoryMessagesAsync 终止: token为空={0}, chatId为空={1}", string.IsNullOrEmpty(_token), string.IsNullOrEmpty(_chatId)));
                return;
            }

            _isLoadingHistory = true;
            _hasMoreHistory = true;
            _isInitialLoadDone = false;
            string errMsg = null;
            AppLogger.Log("ChatPage", string.Format("LoadHistoryMessagesAsync 开始: ChatId={0}, ChatType={1}", _chatId, _chatType));
            try
            {
                MsgProgressBar.Visibility = Visibility.Visible;
                var res = await MessageApi.GetMessageListBySeqAsync(_token, _chatId, _chatType, 0);
                MsgProgressBar.Visibility = Visibility.Collapsed;

                if (res.IsSuccess && res.Messages != null && res.Messages.Count > 0)
                {
                    _messageList.Clear();

                    // 检查消息排序（确保列表按时间升序排列，底部为最新消息）
                    var list = new List<ChatMessageItem>(res.Messages);
                    list.Sort((a, b) => a.SendTime.CompareTo(b.SendTime));

                    foreach (var m in list)
                    {
                        if (m != null)
                        {
                            if (m.IsSelf && _selfAvatarBitmap != null)
                            {
                                m.SenderAvatarBitmap = _selfAvatarBitmap;
                                if (string.IsNullOrEmpty(m.SenderAvatarUrl)) m.SenderAvatarUrl = _selfAvatarUrl;
                            }
                            else if (!string.IsNullOrEmpty(m.SenderAvatarUrl))
                            {
                                string finalUrl = ImageHelper.FormatQiniuUrl(m.SenderAvatarUrl, 72, 72);
                                BitmapImage cachedBmp;
                                if (_senderAvatarBitmapCache.TryGetValue(finalUrl, out cachedBmp))
                                {
                                    m.SenderAvatarBitmap = cachedBmp;
                                }
                            }
                        }
                        _messageList.Add(m);
                    }

                    EmptyMsgPanel.Visibility = Visibility.Collapsed;
                    _hasMoreHistory = (list.Count >= 10);
                    if (TopLoadMoreBorder != null)
                    {
                        TopLoadMoreBorder.Visibility = _hasMoreHistory ? Visibility.Visible : Visibility.Collapsed;
                    }

                    AppLogger.Log("ChatPage", string.Format("Initial messages loaded: {0}", list.Count));

                    // 平滑预加载发送者头像与本机用户自身头像
                    PreloadSenderAvatars(list);
                    EnsureSelfAvatarPreloaded();

                    // 确保绑定内部 ScrollViewer 并一次性对齐到底部
                    EnsureScrollViewerAttached();
                    ScrollToBottom(true);
                }
                else
                {
                    if (_messageList.Count == 0)
                    {
                        EmptyMsgPanel.Visibility = Visibility.Visible;
                    }

                    _hasMoreHistory = false;
                    if (TopLoadMoreBorder != null)
                    {
                        TopLoadMoreBorder.Visibility = Visibility.Collapsed;
                    }

                    if (!res.IsSuccess && !string.IsNullOrEmpty(res.Msg))
                    {
                        errMsg = "获取消息失败: " + res.Msg;
                    }
                }
            }
            catch (Exception ex)
            {
                MsgProgressBar.Visibility = Visibility.Collapsed;
                errMsg = "网络异常: " + ex.Message;
                AppLogger.Log("ChatPage", "LoadHistoryMessagesAsync error: " + ex.Message);
            }

            // 确保绑定内部 ScrollViewer
            EnsureScrollViewerAttached();

            // 延迟完成初始状态，避免进入页面时误触发分页
            await Task.Delay(300);
            _isInitialLoadDone = true;
            _isLoadingHistory = false;

            if (errMsg != null)
            {
                await ShowToastAsync(errMsg);
            }
        }

        /// <summary>
        /// 划到顶部时自动分页拉取更早的历史消息
        /// 核心：切换 ItemsUpdatingScrollMode + ChangeView 恢复像素级锚点，彻底消除跳动
        /// </summary>
        private async Task LoadMoreHistoryMessagesAsync()
        {
            if (_isLoadingHistory || !_hasMoreHistory || string.IsNullOrEmpty(_token) || string.IsNullOrEmpty(_chatId) || _messageList.Count == 0)
            {
                return;
            }

            _isLoadingHistory = true;
            _lastLoadMoreTime = DateTime.Now;
            AppLogger.Log("ChatPage", "LoadMoreHistoryMessagesAsync starting...");

            // 显示加载指示器
            if (TopLoadMoreBorder != null) TopLoadMoreBorder.Visibility = Visibility.Visible;
            if (TopLoadingRing != null) TopLoadingRing.IsActive = true;
            if (TxtTopLoadMore != null) TxtTopLoadMore.Text = "正在加载历史消息...";

            try
            {
                // 查找当前列表头部最早一条有效消息
                ChatMessageItem oldestMsg = null;
                for (int i = 0; i < _messageList.Count; i++)
                {
                    var m = _messageList[i];
                    if (m != null && !string.IsNullOrEmpty(m.MsgId))
                    {
                        if (oldestMsg == null || m.SendTime < oldestMsg.SendTime)
                        {
                            oldestMsg = m;
                        }
                    }
                }

                if (oldestMsg == null)
                {
                    _isLoadingHistory = false;
                    return;
                }

                MsgProgressBar.Visibility = Visibility.Visible;

                // 优先调用 /v1/msg/list-message (获取该 msg_id 之前的更早消息)
                var res = await MessageApi.GetMessageListAsync(_token, _chatId, _chatType, oldestMsg.MsgId, 30);

                // 若 list-message 返回空且存在 msg_seq，降级尝试 /v1/msg/list-message-by-seq
                if ((res == null || !res.IsSuccess || res.Messages == null || res.Messages.Count == 0) && oldestMsg.MsgSeq > 1)
                {
                    res = await MessageApi.GetMessageListBySeqAsync(_token, _chatId, _chatType, oldestMsg.MsgSeq);
                }

                MsgProgressBar.Visibility = Visibility.Collapsed;

                if (res != null && res.IsSuccess && res.Messages != null && res.Messages.Count > 0)
                {
                    // 过滤已存在的消息，避免重复
                    var existingIds = new HashSet<string>();
                    var existingSeqs = new HashSet<long>();
                    foreach (var m in _messageList)
                    {
                        if (!string.IsNullOrEmpty(m.MsgId)) existingIds.Add(m.MsgId);
                        if (m.MsgSeq > 0) existingSeqs.Add(m.MsgSeq);
                    }

                    var newOlderList = new List<ChatMessageItem>();
                    foreach (var m in res.Messages)
                    {
                        if (m == null) continue;
                        if (!string.IsNullOrEmpty(m.MsgId) && existingIds.Contains(m.MsgId)) continue;
                        if (m.MsgSeq > 0 && existingSeqs.Contains(m.MsgSeq)) continue;
                        newOlderList.Add(m);
                    }

                    AppLogger.Log("ChatPage", string.Format("Fetched {0} history items, new older items: {1}", res.Messages.Count, newOlderList.Count));

                    if (newOlderList.Count > 0)
                    {
                        newOlderList.Sort((a, b) => a.SendTime.CompareTo(b.SendTime));

                        // KeepScrollOffset 模式下，ListView 在头部插入时会自动保持当前像素偏移不跳动
                        // 无需手动切换 ScrollMode 或调用 ScrollIntoView
                        for (int i = newOlderList.Count - 1; i >= 0; i--)
                        {
                            var m = newOlderList[i];
                            if (m != null)
                            {
                                if (m.IsSelf && _selfAvatarBitmap != null)
                                {
                                    m.SenderAvatarBitmap = _selfAvatarBitmap;
                                    if (string.IsNullOrEmpty(m.SenderAvatarUrl)) m.SenderAvatarUrl = _selfAvatarUrl;
                                }
                                else if (!string.IsNullOrEmpty(m.SenderAvatarUrl))
                                {
                                    string finalUrl = ImageHelper.FormatQiniuUrl(m.SenderAvatarUrl, 72, 72);
                                    BitmapImage cachedBmp;
                                    if (_senderAvatarBitmapCache.TryGetValue(finalUrl, out cachedBmp))
                                    {
                                        m.SenderAvatarBitmap = cachedBmp;
                                    }
                                }
                            }
                            _messageList.Insert(0, m);
                        }

                        // 等待一帧让布局测量完成
                        await Task.Delay(32);

                        AppLogger.Log("ChatPage", string.Format("Inserted {0} items, total: {1}", newOlderList.Count, _messageList.Count));

                        _hasMoreHistory = true;
                        if (TopLoadMoreBorder != null) TopLoadMoreBorder.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // 若返回的消息全部已在本地列表中且返回条数较少，说明已达历史最起始处
                        if (res.Messages.Count < 5)
                        {
                            _hasMoreHistory = false;
                            if (TopLoadMoreBorder != null) TopLoadMoreBorder.Visibility = Visibility.Collapsed;
                        }
                    }
                }
                else if (res != null && res.IsSuccess && (res.Messages == null || res.Messages.Count == 0))
                {
                    // 服务器明确返回 0 条，已到达历史消息最顶部
                    _hasMoreHistory = false;
                    if (TopLoadMoreBorder != null) TopLoadMoreBorder.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                MsgProgressBar.Visibility = Visibility.Collapsed;
                AppLogger.Log("ChatPage", "LoadMoreHistoryMessages error: " + ex.Message + "\n" + ex.StackTrace);
            }
            finally
            {
                if (TopLoadingRing != null) TopLoadingRing.IsActive = false;
                if (TxtTopLoadMore != null)
                {
                    TxtTopLoadMore.Text = _hasMoreHistory ? "向上滑动加载更早消息" : "已加载全部历史消息";
                }
                AppLogger.Log("ChatPage", "LoadMoreHistoryMessages finished.");
            }

            // 加载完毕后延迟 1200ms 再解锁，防止高频惯性滚动连环触发
            await Task.Delay(1200);
            _lastLoadMoreTime = DateTime.Now;
            _isLoadingHistory = false;
            AppLogger.Log("ChatPage", "LoadMoreHistory lock released.");
        }

        /// <summary>
        /// 后台异步低优先级预加载消息发送者头像（含自身头像缓存）
        /// </summary>
        private void PreloadSenderAvatars(IEnumerable<ChatMessageItem> items)
        {
            if (ImageLoader.DisableAllImages) return;
            if (items == null) return;
            var list = new List<ChatMessageItem>(items);

            Task.Run(async () =>
            {
                await Task.Delay(100);

                foreach (var item in list)
                {
                    if (ImageLoader.DisableAllImages) break;
                    if (item == null || string.IsNullOrEmpty(item.SenderAvatarUrl) || item.SenderAvatarBitmap != null) continue;

                    var currentItem = item;
                    string finalUrl = ImageHelper.FormatQiniuUrl(currentItem.SenderAvatarUrl, 72, 72);

                    // 优先从内存已解码缓存直接复用
                    BitmapImage cachedBmp;
                    if (_senderAvatarBitmapCache.TryGetValue(finalUrl, out cachedBmp))
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                        {
                            try
                            {
                                currentItem.SenderAvatarBitmap = cachedBmp;
                            }
                            catch { }
                        });
                        continue;
                    }

                    try
                    {
                        byte[] bytes = await ImageLoader.GetImageBytesAsync(finalUrl);
                        if (bytes != null && bytes.Length > 0)
                        {
                            await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                            {
                                try
                                {
                                    var bmp = new BitmapImage();
                                    bmp.DecodePixelWidth = 72;
                                    bmp.DecodePixelHeight = 72;
                                    bmp.DecodePixelType = DecodePixelType.Logical;
                                    using (var stream = new InMemoryRandomAccessStream())
                                    {
                                        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                                        {
                                            writer.WriteBytes(bytes);
                                            writer.StoreAsync().AsTask().Wait();
                                        }
                                        stream.Seek(0);
                                        bmp.SetSource(stream);
                                    }
                                    _senderAvatarBitmapCache[finalUrl] = bmp;
                                    currentItem.SenderAvatarBitmap = bmp;

                                    // 顺便缓存自身头像，下次发消息直接复用
                                    if (currentItem.IsSelf && _selfAvatarBitmap == null)
                                    {
                                        _selfAvatarBitmap = bmp;
                                        _selfAvatarUrl = currentItem.SenderAvatarUrl ?? "";
                                    }
                                }
                                catch { }
                            });
                        }
                    }
                    catch { }

                    await Task.Delay(25);
                }
            });
        }

        /// <summary>
        /// 预拉取本机用户头像并缓存，自动刷给消息列表中所有我方消息
        /// </summary>
        private void EnsureSelfAvatarPreloaded()
        {
            if (ImageLoader.DisableAllImages) return;

            // 如果已有全局内存缓存，立即刷给当前列表所有自己的消息
            if (_selfAvatarBitmap != null)
            {
                ApplySelfAvatarToMessageList();
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    string avatarUrl = _selfAvatarUrl;

                    // 1. 如果尚未获取过自身头像 URL，从用户资料接口拉取
                    if (string.IsNullOrEmpty(avatarUrl) && !string.IsNullOrEmpty(_token))
                    {
                        var userInfo = await UserApi.GetUserInfoAsync(_token);
                        if (userInfo != null && !string.IsNullOrEmpty(userInfo.AvatarUrl))
                        {
                            avatarUrl = userInfo.AvatarUrl;
                        }
                    }

                    // 2. 如果接口拉取失败，尝试从已有消息中找
                    if (string.IsNullOrEmpty(avatarUrl))
                    {
                        for (int i = _messageList.Count - 1; i >= 0; i--)
                        {
                            var m = _messageList[i];
                            if (m != null && m.IsSelf && !string.IsNullOrEmpty(m.SenderAvatarUrl))
                            {
                                avatarUrl = m.SenderAvatarUrl;
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(avatarUrl)) return;

                    _selfAvatarUrl = avatarUrl;
                    string finalUrl = ImageHelper.FormatQiniuUrl(avatarUrl, 72, 72);
                    byte[] bytes = await ImageLoader.GetImageBytesAsync(finalUrl);
                    if (bytes != null && bytes.Length > 0)
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            try
                            {
                                var bmp = new BitmapImage();
                                bmp.DecodePixelWidth = 72;
                                bmp.DecodePixelHeight = 72;
                                bmp.DecodePixelType = DecodePixelType.Logical;
                                using (var stream = new InMemoryRandomAccessStream())
                                {
                                    using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                                    {
                                        writer.WriteBytes(bytes);
                                        writer.StoreAsync().AsTask().Wait();
                                    }
                                    stream.Seek(0);
                                    bmp.SetSource(stream);
                                }
                                _selfAvatarBitmap = bmp;
                                ApplySelfAvatarToMessageList();
                            }
                            catch { }
                        });
                    }
                }
                catch { }
            });
        }

        /// <summary>
        /// 将当前已缓存的自身头像立即应用到消息列表中所有我方消息
        /// </summary>
        private void ApplySelfAvatarToMessageList()
        {
            if (_selfAvatarBitmap == null) return;
            for (int i = 0; i < _messageList.Count; i++)
            {
                var m = _messageList[i];
                if (m != null && m.IsSelf && m.SenderAvatarBitmap == null)
                {
                    m.SenderAvatarBitmap = _selfAvatarBitmap;
                    if (string.IsNullOrEmpty(m.SenderAvatarUrl)) m.SenderAvatarUrl = _selfAvatarUrl;
                }
            }
        }

        private void ScrollToBottom(bool disableAnimation = true)
        {
            EnsureScrollViewerAttached();

            if (_chatScrollViewer != null)
            {
                try
                {
                    _chatScrollViewer.ChangeView(null, double.MaxValue, null, disableAnimation);
                    return;
                }
                catch { }
            }

            if (_messageList.Count > 0)
            {
                try
                {
                    ChatListView.ScrollIntoView(_messageList[_messageList.Count - 1]);
                }
                catch { }
            }
        }

        /// <summary>
        /// 取消正在进行的图片/视频上传
        /// </summary>
        private void BtnCancelUpload_Click(object sender, RoutedEventArgs e)
        {
            if (_uploadCts != null)
            {
                try
                {
                    _uploadCts.Cancel();
                }
                catch { }
            }
            if (UploadProgressPanel != null)
            {
                UploadProgressPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void AppBarBtnPickImage_Click(object sender, RoutedEventArgs e)
        {
#if WINDOWS_PHONE_APP
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".gif");
                picker.FileTypeFilter.Add(".bmp");
                picker.ContinuationData["Action"] = "PickImage";
                picker.PickSingleFileAndContinue();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("PickImage failed: " + ex.Message);
            }
#endif
        }

        private void AppBarBtnSendVideo_Click(object sender, RoutedEventArgs e)
        {
#if WINDOWS_PHONE_APP
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary;
                picker.FileTypeFilter.Add(".mp4");
                picker.FileTypeFilter.Add(".mov");
                picker.FileTypeFilter.Add(".wmv");
                picker.FileTypeFilter.Add(".avi");
                picker.FileTypeFilter.Add(".3gp");
                picker.FileTypeFilter.Add(".mkv");
                picker.FileTypeFilter.Add(".webm");
                picker.ContinuationData["Action"] = "PickVideo";
                picker.PickSingleFileAndContinue();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("PickVideo failed: " + ex.Message);
            }
#endif
        }

        private void AppBarBtnSendFile_Click(object sender, RoutedEventArgs e)
        {
#if WINDOWS_PHONE_APP
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add("*");
                picker.ContinuationData["Action"] = "PickFile";
                picker.PickSingleFileAndContinue();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("PickFile failed: " + ex.Message);
            }
#endif
        }

#if WINDOWS_PHONE_APP
        /// <summary>
        /// 接收系统相册/视频库/文件库选择回调并直传七牛云发送媒体与文件
        /// </summary>
        public async void ContinueFileOpenPicker(Windows.ApplicationModel.Activation.FileOpenPickerContinuationEventArgs args)
        {
            try
            {
                if (args != null && args.Files != null && args.Files.Count > 0)
                {
                    var file = args.Files[0];
                    string action = (args.ContinuationData != null && args.ContinuationData.ContainsKey("Action"))
                        ? (args.ContinuationData["Action"] as string)
                        : null;

                    if (action == "PickFile")
                    {
                        await UploadAndSendLocalFileAsync(file);
                    }
                    else if (action == "PickVideo")
                    {
                        await UploadAndSendLocalVideoAsync(file);
                    }
                    else
                    {
                        bool isVideo = false;
                        if (file != null)
                        {
                            string ext = file.FileType.ToLowerInvariant();
                            if (ext == ".mp4" || ext == ".mov" || ext == ".wmv" || ext == ".avi" || ext == ".3gp" || ext == ".mkv" || ext == ".webm")
                            {
                                isVideo = true;
                            }
                        }

                        if (isVideo)
                        {
                            await UploadAndSendLocalVideoAsync(file);
                        }
                        else
                        {
                            await UploadAndSendLocalImageAsync(file);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("ChatPage", "ContinueFileOpenPicker error: " + ex.Message + "\n" + ex.StackTrace);
            }
        }
#endif

        /// <summary>
        /// 上传本地图片到七牛云并发送图片消息 (带实时进度条显示与取消支持)
        /// </summary>
        private async Task UploadAndSendLocalImageAsync(Windows.Storage.StorageFile file)
        {
            if (file == null || string.IsNullOrEmpty(_token)) return;

            string errMsg = null;
            _uploadCts = new CancellationTokenSource();
            UploadProgressPanel.Visibility = Visibility.Visible;
            UploadProgressBar.Value = 0;
            TxtUploadPercentage.Text = "0%";
            TxtUploadStatus.Text = "准备上传图片...";

            var uploadProgress = new Progress<double>(pct =>
            {
                UploadProgressBar.Value = pct;
                TxtUploadPercentage.Text = string.Format("{0:0}%", pct);
                if (pct < 20)
                {
                    TxtUploadStatus.Text = "准备图片数据...";
                }
                else if (pct < 95)
                {
                    TxtUploadStatus.Text = "正在直传七牛云...";
                }
                else
                {
                    TxtUploadStatus.Text = "上传完成，正在发送...";
                }
            });

            try
            {
                // 调用系统七牛云直传组件 (附带实时进度报告与 CancellationToken)
                string publicUrl = await QiniuUploadHelper.UploadImageAsync(file, _token, uploadProgress, _uploadCts.Token);
                if (!string.IsNullOrEmpty(publicUrl))
                {
                    await DoSendImageAsync(publicUrl);
                }
                else
                {
                    errMsg = "上传图片未获取到访问地址";
                }
            }
            catch (OperationCanceledException)
            {
                AppLogger.Log("ChatPage", "用户取消了图片上传");
            }
            catch (Exception ex)
            {
                errMsg = "图片上传失败: " + ex.Message;
            }
            finally
            {
                _uploadCts = null;
                var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (UploadProgressPanel != null)
                        {
                            UploadProgressPanel.Visibility = Visibility.Collapsed;
                        }
                    }
                    catch { }
                });
            }

            if (errMsg != null)
            {
                await ShowToastAsync(errMsg);
            }
        }

        /// <summary>
        /// 上传本地视频到七牛云并发送视频消息 (带实时进度条显示与取消支持)
        /// </summary>
        private async Task UploadAndSendLocalVideoAsync(Windows.Storage.StorageFile file)
        {
            if (file == null || string.IsNullOrEmpty(_token)) return;

            string errMsg = null;
            _uploadCts = new CancellationTokenSource();
            UploadProgressPanel.Visibility = Visibility.Visible;
            UploadProgressBar.Value = 0;
            TxtUploadPercentage.Text = "0%";
            TxtUploadStatus.Text = "准备上传视频...";

            var uploadProgress = new Progress<double>(pct =>
            {
                UploadProgressBar.Value = pct;
                TxtUploadPercentage.Text = string.Format("{0:0}%", pct);
                if (pct < 20)
                {
                    TxtUploadStatus.Text = "准备视频数据...";
                }
                else if (pct < 95)
                {
                    TxtUploadStatus.Text = "正在直传七牛云...";
                }
                else
                {
                    TxtUploadStatus.Text = "上传完成，正在发送...";
                }
            });

            try
            {
                // 获取本地视频基础属性 (大小与文件名)
                var props = await file.GetBasicPropertiesAsync();
                long fileSize = (long)props.Size;
                string fileName = file.Name;

                // 调用系统七牛云直传组件 (附带实时进度报告与 CancellationToken)
                var uploadRes = await QiniuUploadHelper.UploadVideoDetailedAsync(file, _token, uploadProgress, _uploadCts.Token);
                if (uploadRes != null && !string.IsNullOrEmpty(uploadRes.Key))
                {
                    await DoSendVideoAsync(uploadRes);
                }
                else
                {
                    errMsg = "上传视频未获取到访问地址";
                }
            }
            catch (OperationCanceledException)
            {
                AppLogger.Log("ChatPage", "用户取消了视频上传");
            }
            catch (Exception ex)
            {
                errMsg = "视频上传失败: " + ex.Message;
            }
            finally
            {
                _uploadCts = null;
                var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (UploadProgressPanel != null)
                        {
                            UploadProgressPanel.Visibility = Visibility.Collapsed;
                        }
                    }
                    catch { }
                });
            }

            if (errMsg != null)
            {
                await ShowToastAsync(errMsg);
            }
        }

        private async Task DoSendVideoAsync(QiniuUploadResult videoResult)
        {
            if (videoResult == null || string.IsNullOrWhiteSpace(videoResult.Key)) return;

            string msgId = Guid.NewGuid().ToString("N");

            // 本地乐观添加视频消息 (ContentType = 10 视频)
            var localMsg = new ChatMessageItem
            {
                MsgId = msgId,
                ChatId = _chatId,
                ChatType = _chatType,
                Direction = "right",
                ContentType = 10,
                VideoUrl = videoResult.PublicUrl,
                FileName = !string.IsNullOrEmpty(videoResult.FileName) ? videoResult.FileName : "视频",
                FileSize = videoResult.FileSize,
                MediaWidth = videoResult.Width,
                MediaHeight = videoResult.Height,
                SendTime = DateTime.UtcNow.Ticks / 10000 - 62135596800000L, // UTC ms
                SenderName = "我",
                SenderAvatarUrl = _selfAvatarUrl,
                SenderAvatarBitmap = _selfAvatarBitmap
            };
            localMsg.InitParsedData();

            _messageList.Add(localMsg);
            if (EmptyMsgPanel != null)
            {
                EmptyMsgPanel.Visibility = Visibility.Collapsed;
            }
            ScrollToBottom(true);

            string sendErr = null;
            try
            {
                var res = await MessageApi.SendVideoMessageAsync(
                    _token, 
                    _chatId, 
                    _chatType, 
                    videoResult.Key, 
                    videoResult.Hash, 
                    videoResult.FileName, 
                    videoResult.FileSize, 
                    videoResult.Width, 
                    videoResult.Height, 
                    videoResult.MimeType, 
                    videoResult.FileExtension, 
                    msgId);

                if (!res.IsSuccess)
                {
                    sendErr = "发送视频失败: " + res.Msg;
                }
            }
            catch (Exception ex)
            {
                sendErr = "发送视频异常: " + ex.Message;
            }

            if (sendErr != null)
            {
                await ShowToastAsync(sendErr);
            }
        }

        private async Task DoSendVideoAsync(string videoUrl, string fileName = null, long fileSize = 0)
        {
            if (string.IsNullOrWhiteSpace(videoUrl)) return;
            string cleanUrl = videoUrl.Trim();
            string key = cleanUrl;
            if (key.Contains("/"))
            {
                key = key.Substring(key.LastIndexOf('/') + 1);
            }
            int qIdx = key.IndexOf('?');
            if (qIdx >= 0)
            {
                key = key.Substring(0, qIdx);
            }
            string ext = "mp4";
            if (key.Contains("."))
            {
                ext = key.Substring(key.LastIndexOf('.') + 1).ToLowerInvariant();
            }

            var result = new QiniuUploadResult
            {
                Key = key,
                Hash = key.Contains(".") ? key.Substring(0, key.IndexOf('.')) : key,
                FileName = !string.IsNullOrEmpty(fileName) ? fileName : key,
                FileSize = fileSize,
                FileExtension = ext,
                MimeType = "video/" + ext,
                PublicUrl = cleanUrl,
                Width = 0,
                Height = 0
            };
            await DoSendVideoAsync(result);
        }

        /// <summary>
        /// 上传本地普通文件到七牛云并发送文件消息 (带实时进度条显示与取消支持)
        /// </summary>
        private async Task UploadAndSendLocalFileAsync(Windows.Storage.StorageFile file)
        {
            if (file == null || string.IsNullOrEmpty(_token)) return;

            string errMsg = null;
            _uploadCts = new CancellationTokenSource();
            UploadProgressPanel.Visibility = Visibility.Visible;
            UploadProgressBar.Value = 0;
            TxtUploadPercentage.Text = "0%";
            TxtUploadStatus.Text = "准备上传文件...";

            var uploadProgress = new Progress<double>(pct =>
            {
                UploadProgressBar.Value = pct;
                TxtUploadPercentage.Text = string.Format("{0:0}%", pct);
                if (pct < 20)
                {
                    TxtUploadStatus.Text = "准备文件数据...";
                }
                else if (pct < 95)
                {
                    TxtUploadStatus.Text = "正在直传七牛云...";
                }
                else
                {
                    TxtUploadStatus.Text = "上传完成，正在发送...";
                }
            });

            try
            {
                // 调用系统七牛云直传组件 (附带实时进度报告与 CancellationToken)
                var uploadRes = await QiniuUploadHelper.UploadFileDetailedAsync(file, _token, uploadProgress, _uploadCts.Token);
                if (uploadRes != null && !string.IsNullOrEmpty(uploadRes.Key))
                {
                    await DoSendFileAsync(uploadRes);
                }
                else
                {
                    errMsg = "上传文件未获取到访问地址";
                }
            }
            catch (OperationCanceledException)
            {
                AppLogger.Log("ChatPage", "用户取消了文件上传");
            }
            catch (Exception ex)
            {
                errMsg = "文件上传失败: " + ex.Message;
            }
            finally
            {
                _uploadCts = null;
                var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (UploadProgressPanel != null)
                        {
                            UploadProgressPanel.Visibility = Visibility.Collapsed;
                        }
                    }
                    catch { }
                });
            }

            if (errMsg != null)
            {
                await ShowToastAsync(errMsg);
            }
        }

        private async Task DoSendFileAsync(QiniuUploadResult fileResult)
        {
            if (fileResult == null || string.IsNullOrWhiteSpace(fileResult.Key)) return;

            string msgId = Guid.NewGuid().ToString("N");
            string fullFileUrl = fileResult.PublicUrl;

            // 本地乐观添加文件消息 (ContentType = 4 文件)
            var localMsg = new ChatMessageItem
            {
                MsgId = msgId,
                ChatId = _chatId,
                ChatType = _chatType,
                Direction = "right",
                ContentType = 4,
                FileUrl = fullFileUrl,
                FileName = !string.IsNullOrEmpty(fileResult.FileName) ? fileResult.FileName : "文件",
                FileSize = fileResult.FileSize,
                Text = string.Format("[文件: {0}]", fileResult.FileName),
                SendTime = DateTime.UtcNow.Ticks / 10000 - 62135596800000L, // UTC ms
                SenderName = "我",
                SenderAvatarUrl = _selfAvatarUrl,
                SenderAvatarBitmap = _selfAvatarBitmap
            };
            localMsg.InitParsedData();

            _messageList.Add(localMsg);
            if (EmptyMsgPanel != null)
            {
                EmptyMsgPanel.Visibility = Visibility.Collapsed;
            }
            ScrollToBottom(true);

            string sendErr = null;
            try
            {
                var res = await MessageApi.SendFileMessageAsync(
                    token: _token,
                    chatId: _chatId,
                    chatType: _chatType,
                    fileKey: fileResult.Key,
                    fileHash: fileResult.Hash,
                    fileName: fileResult.FileName,
                    fileSize: fileResult.FileSize,
                    mimeType: fileResult.MimeType,
                    fileExtension: fileResult.FileExtension,
                    customMsgId: msgId);

                if (!res.IsSuccess)
                {
                    sendErr = "发送文件失败: " + res.Msg;
                }
            }
            catch (Exception ex)
            {
                sendErr = "发送文件异常: " + ex.Message;
            }

            if (sendErr != null)
            {
                await ShowToastAsync(sendErr);
            }
        }

        private async Task DoSendFileAsync(string fileUrl, string fileName = null, long fileSize = 0)
        {
            if (string.IsNullOrWhiteSpace(fileUrl)) return;
            string cleanUrl = fileUrl.Trim();
            string key = cleanUrl;
            if (key.Contains("/"))
            {
                key = key.Substring(key.LastIndexOf('/') + 1);
            }
            int qIdx = key.IndexOf('?');
            if (qIdx >= 0)
            {
                key = key.Substring(0, qIdx);
            }
            string ext = "dat";
            if (key.Contains("."))
            {
                ext = key.Substring(key.LastIndexOf('.') + 1).ToLowerInvariant();
            }

            var result = new QiniuUploadResult
            {
                Key = key,
                Hash = key.Contains(".") ? key.Substring(0, key.IndexOf('.')) : key,
                FileName = !string.IsNullOrEmpty(fileName) ? fileName : key,
                FileSize = fileSize,
                FileExtension = ext,
                MimeType = "application/octet-stream",
                PublicUrl = cleanUrl
            };
            await DoSendFileAsync(result);
        }

        private async void AppBarBtnSendUrl_Click(object sender, RoutedEventArgs e)
        {
            string currentText = TxtInput.Text.Trim();
            if (!string.IsNullOrEmpty(currentText) && (currentText.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || currentText.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                TxtInput.Text = "";
                string lower = currentText.ToLowerInvariant();
                if (lower.EndsWith(".mp4") || lower.EndsWith(".mov") || lower.EndsWith(".wmv") || lower.EndsWith(".avi") || lower.EndsWith(".3gp") || lower.EndsWith(".mkv") || lower.EndsWith(".webm") || lower.Contains("/video/"))
                {
                    await DoSendVideoAsync(currentText);
                }
                else if (lower.EndsWith(".zip") || lower.EndsWith(".rar") || lower.EndsWith(".7z") || lower.EndsWith(".pdf") || lower.EndsWith(".doc") || lower.EndsWith(".docx") || lower.EndsWith(".xls") || lower.EndsWith(".xlsx") || lower.EndsWith(".ppt") || lower.EndsWith(".pptx") || lower.EndsWith(".apk") || lower.EndsWith(".xap") || lower.EndsWith(".appx") || lower.EndsWith(".txt") || lower.Contains("/file/"))
                {
                    await DoSendFileAsync(currentText);
                }
                else
                {
                    await DoSendImageAsync(currentText);
                }
            }
            else
            {
                await ShowToastAsync("请先在输入框中输入或粘贴媒体/文件 URL (http/https 直链)，然后点击此项发送");
            }
        }

        private void AppBarBtnClearInput_Click(object sender, RoutedEventArgs e)
        {
            TxtInput.Text = "";
        }

        private void AppBarBtnScrollBottom_Click(object sender, RoutedEventArgs e)
        {
            ScrollToBottom();
        }

        private async void AppBarBtnViewLogs_Click(object sender, RoutedEventArgs e)
        {
            string logs = await AppLogger.ReadFullPersistedLogsAsync();
            if (string.IsNullOrEmpty(logs)) logs = "暂无诊断日志";
            Frame.Navigate(typeof(TextViewerPage), logs);
        }

        #region 软键盘与 CommandBar 交互防冲突处理

        private void InputPane_Showing(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            // 当软键盘弹出时，隐藏底部的 CommandBar，防止其遮挡或截获发送按钮的触摸事件
            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.Visibility = Visibility.Collapsed;
                this.BottomAppBar.IsOpen = false;
            }
        }

        private void InputPane_Hiding(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            // 当软键盘收起时，恢复底部的 CommandBar
            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.Visibility = Visibility.Visible;
            }
        }

        #endregion

        #region XAML 兼容事件处理器 (确保 VS2013 任意缓存版本均能 100% 编译通过)

        private void BtnAttach_Click(object sender, RoutedEventArgs e)
        {
            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.IsOpen = !this.BottomAppBar.IsOpen;
            }
        }

        private void TxtInput_GotFocus(object sender, RoutedEventArgs e)
        {
            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.IsOpen = false;
                this.BottomAppBar.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnClearUrl_Click(object sender, RoutedEventArgs e)
        {
            TxtInput.Text = "";
        }

        private async void BtnSendImage_Click(object sender, RoutedEventArgs e)
        {
            string text = TxtInput.Text.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                TxtInput.Text = "";
                await DoSendImageAsync(text);
            }
        }

        private async void QuickImage_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element != null && element.Tag != null)
            {
                string tagUrl = element.Tag.ToString();
                if (!string.IsNullOrEmpty(tagUrl))
                {
                    await DoSendImageAsync(tagUrl);
                }
            }
        }

        #endregion

        private async Task DoSendImageAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return;

            string cleanUrl = imageUrl.Trim();
            string msgText = string.Format("![图片]({0})", cleanUrl);
            string msgId = Guid.NewGuid().ToString("N");

            // 本地乐观添加图片消息
            var localMsg = new ChatMessageItem
            {
                MsgId = msgId,
                ChatId = _chatId,
                ChatType = _chatType,
                Direction = "right",
                ContentType = 2,
                ImageUrl = cleanUrl,
                Text = msgText,
                SendTime = DateTime.UtcNow.Ticks / 10000 - 62135596800000L, // UTC ms
                SenderName = "我",
                SenderAvatarUrl = _selfAvatarUrl,
                SenderAvatarBitmap = _selfAvatarBitmap
            };
            localMsg.InitParsedData();

            _messageList.Add(localMsg);
            EmptyMsgPanel.Visibility = Visibility.Collapsed;
            ScrollToBottom(true);

            string sendErr = null;
            try
            {
                var res = await MessageApi.SendImageMessageAsync(_token, _chatId, _chatType, cleanUrl, msgText, msgId);
                if (!res.IsSuccess)
                {
                    sendErr = "发送图片失败: " + res.Msg;
                }
            }
            catch (Exception ex)
            {
                sendErr = "发送图片异常: " + ex.Message;
            }

            if (sendErr != null)
            {
                await ShowToastAsync(sendErr);
            }
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            await DoSendMessageAsync();
            // 发送后保持输入框焦点，使用户可以连续键入，避免键盘意外收起
            try
            {
                TxtInput.Focus(FocusState.Programmatic);
            }
            catch { }
        }

        private async void TxtInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                await DoSendMessageAsync();
                try
                {
                    TxtInput.Focus(FocusState.Programmatic);
                }
                catch { }
            }
        }

        private async Task DoSendMessageAsync()
        {
            if (_isSending) return;
            string text = TxtInput.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            _isSending = true;
            TxtInput.Text = "";

            try
            {
                string msgId = Guid.NewGuid().ToString("N");

                // 本地乐观添加消息
                var localMsg = new ChatMessageItem
                {
                    MsgId = msgId,
                    ChatId = _chatId,
                    ChatType = _chatType,
                    Direction = "right",
                    ContentType = 1,
                    Text = text.Trim(),
                    SendTime = DateTime.UtcNow.Ticks / 10000 - 62135596800000L, // UTC ms
                    SenderName = "我",
                    SenderAvatarUrl = _selfAvatarUrl,
                    SenderAvatarBitmap = _selfAvatarBitmap  // 直接复用缓存，头像即时显示
                };
                localMsg.InitParsedData();

                _messageList.Add(localMsg);
                EmptyMsgPanel.Visibility = Visibility.Collapsed;
                ScrollToBottom(true);

                string sendErr = null;
                try
                {
                    var res = await MessageApi.SendTextMessageAsync(_token, _chatId, _chatType, text.Trim(), msgId);
                    if (!res.IsSuccess)
                    {
                        sendErr = "发送失败: " + res.Msg;
                    }
                }
                catch (Exception ex)
                {
                    sendErr = "发送异常: " + ex.Message;
                }

                if (sendErr != null)
                {
                    await ShowToastAsync(sendErr);
                }
            }
            finally
            {
                _isSending = false;
            }
        }

        /// <summary>
        /// 点击头像：自己 → MyProfilePage；对方 → UserDetailPage
        /// </summary>
        private void Avatar_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            var element = sender as FrameworkElement;
            if (element == null) return;

            var msg = element.DataContext as ChatMessageItem;
            if (msg == null) return;

            if (msg.IsSelf)
            {
                // 自己的头像 → 进入我的个人信息页面
                Frame.Navigate(typeof(MyProfilePage));
            }
            else
            {
                // 对方头像 → 进入对方用户详情页面
                if (!string.IsNullOrEmpty(msg.SenderId))
                {
                    Frame.Navigate(typeof(UserDetailPage), new UserDetailNavArgs
                    {
                        UserId = msg.SenderId,
                        Name = msg.DisplaySenderName,
                        AvatarUrl = msg.SenderAvatarUrl ?? ""
                    });
                }
            }
        }

        /// <summary>
        /// 消息单击弹出操作菜单 (复制、引用回复、撤回、删除)
        /// </summary>
        private void MessageBubble_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element == null) return;

            var msg = element.DataContext as ChatMessageItem;
            if (msg == null) return;

            var flyout = new MenuFlyout();

            // 0. 视频播放与直链 (如果是视频消息)
            if (msg.IsVideoMsg && !string.IsNullOrEmpty(msg.ExtractedVideoUrl))
            {
                string rawVideoUrl = msg.ExtractedVideoUrl;
                string fullVideoUrl = ImageHelper.FormatQiniuUrl(rawVideoUrl, 0, 0);

                var itemPlayVideo = new MenuFlyoutItem { Text = "播放视频 (专用播放器)" };
                itemPlayVideo.Click += (s, args) =>
                {
                    Frame.Navigate(typeof(VideoPlayerPage), new VideoPlayerNavArgs
                    {
                        VideoUrl = fullVideoUrl,
                        Title = string.Format("{0} 的视频", msg.DisplaySenderName)
                    });
                };
                flyout.Items.Add(itemPlayVideo);

                var itemCopyVideo = new MenuFlyoutItem { Text = "复制/查看视频直链" };
                itemCopyVideo.Click += (s, args) =>
                {
                    Frame.Navigate(typeof(TextViewerPage), fullVideoUrl);
                };
                flyout.Items.Add(itemCopyVideo);
            }

            // 1. 复制/查看文本 (进入独立全屏页面，巨大输入框随意复制)
            if (!string.IsNullOrEmpty(msg.Text))
            {
                var itemCopy = new MenuFlyoutItem { Text = "复制/查看文本" };
                itemCopy.Click += (s, args) =>
                {
                    Frame.Navigate(typeof(TextViewerPage), msg.Text);
                };
                flyout.Items.Add(itemCopy);
            }

            // 2. 引用回复 (Reply)
            var itemReply = new MenuFlyoutItem { Text = "引用回复" };
            itemReply.Click += (s, args) =>
            {
                string senderName = msg.IsSelf ? "我" : msg.DisplaySenderName;
                string quotePrefix = "「" + senderName + ": " + (msg.Text ?? "") + "」\n";
                TxtInput.Text = quotePrefix + TxtInput.Text;
                TxtInput.Focus(FocusState.Programmatic);
                TxtInput.SelectionStart = TxtInput.Text.Length;
            };
            flyout.Items.Add(itemReply);

            // 3. 撤回消息 (Recall) - 仅限自己发送的消息
            if (msg.IsSelf && !string.IsNullOrEmpty(msg.MsgId))
            {
                var itemRecall = new MenuFlyoutItem { Text = "撤回消息" };
                itemRecall.Click += async (s, args) =>
                {
                    await RecallChatMessageAsync(msg);
                };
                flyout.Items.Add(itemRecall);
            }

            // 4. 删除 (Delete) - 本地移除
            var itemDelete = new MenuFlyoutItem { Text = "删除消息" };
            itemDelete.Click += (s, args) =>
            {
                _messageList.Remove(msg);
                if (_messageList.Count == 0)
                {
                    EmptyMsgPanel.Visibility = Visibility.Visible;
                }
            };
            flyout.Items.Add(itemDelete);

            flyout.ShowAt(element);
        }

        /// <summary>
        /// 撤回指定的聊天消息
        /// </summary>
        private async Task RecallChatMessageAsync(ChatMessageItem msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.MsgId)) return;

            string errMsg = null;
            try
            {
                MsgProgressBar.Visibility = Visibility.Visible;
                var res = await MessageApi.RecallMessageAsync(_token, msg.MsgId, _chatId, _chatType);
                MsgProgressBar.Visibility = Visibility.Collapsed;

                if (res.IsSuccess)
                {
                    msg.Text = "你撤回了一条消息";
                }
                else
                {
                    errMsg = "撤回失败: " + (res.Msg ?? "未知错误");
                }
            }
            catch (Exception ex)
            {
                MsgProgressBar.Visibility = Visibility.Collapsed;
                errMsg = "撤回异常: " + ex.Message;
            }

            if (errMsg != null)
            {
                await ShowToastAsync(errMsg);
            }
        }

        /// <summary>
        /// 点击文件消息卡片，带 Referer 防盗链请求头与实时进度条进行文件下载 (静默下载，不弹无谓 Toast)
        /// </summary>
        private async void FileBubble_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            var element = sender as FrameworkElement;
            if (element == null) return;

            var msg = element.DataContext as ChatMessageItem;
            if (msg == null) return;

            if (string.IsNullOrEmpty(msg.FileUrl))
            {
                return;
            }

            if (msg.IsDownloading)
            {
                return;
            }

            // 若已经下载过，尝试直接用系统关联应用打开该文件
            if (msg.IsDownloaded && msg.DownloadedFile != null)
            {
                try
                {
                    bool launched = await Windows.System.Launcher.LaunchFileAsync(msg.DownloadedFile);
                    if (launched) return;
                }
                catch { }
            }

            msg.IsDownloading = true;
            msg.DownloadProgress = 0;
            msg.DownloadStatusText = "准备下载...";

            var downloadProgress = new Progress<double>(pct =>
            {
                msg.DownloadProgress = pct;
                msg.DownloadStatusText = string.Format("下载中 {0:0}%", pct);
            });

            string errMsg = null;
            StorageFile savedFile = null;

            try
            {
                savedFile = await FileDownloadHelper.DownloadFileWithProgressAsync(
                    msg.FileUrl,
                    msg.DisplayFileName,
                    msg.FileSize,
                    downloadProgress);

                msg.DownloadedFile = savedFile;
                msg.IsDownloading = false;
                msg.IsDownloaded = true;

                string folderDesc = "公共目录";
                if (savedFile != null && !string.IsNullOrEmpty(savedFile.Path))
                {
                    if (savedFile.Path.IndexOf("Music", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        folderDesc = "【Music (音乐)】目录";
                        msg.DownloadStatusText = "已存入音乐库";
                    }
                    else if (savedFile.Path.IndexOf("Pictures", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        folderDesc = "【Pictures (相册)】目录";
                        msg.DownloadStatusText = "已存入相册";
                    }
                    else if (savedFile.Path.IndexOf("Videos", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        folderDesc = "【Videos (视频)】目录";
                        msg.DownloadStatusText = "已存入视频库";
                    }
                    else
                    {
                        folderDesc = "本地目录";
                        msg.DownloadStatusText = "已下载";
                    }
                }
                else
                {
                    msg.DownloadStatusText = "已下载";
                }

                await ShowToastAsync(string.Format("文件已存入系统{0}: {1}", folderDesc, (savedFile != null ? savedFile.Name : msg.DisplayFileName)));

                // 下载完成后尝试唤起系统默认应用打开
                if (savedFile != null)
                {
                    try
                    {
                        await Windows.System.Launcher.LaunchFileAsync(savedFile);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                msg.IsDownloading = false;
                msg.DownloadStatusText = "下载失败(重试)";
                errMsg = "文件下载失败: " + ex.Message;
            }

            if (errMsg != null)
            {
                await ShowToastAsync(errMsg);
            }
        }

        /// <summary>
        /// 点击视频消息预览块，直接打开专属视频播放器页面 (支持 Referer 防盗链流式缓冲与硬件解码)
        /// </summary>
        private void VideoBubble_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            var element = sender as FrameworkElement;
            if (element == null) return;

            var msg = element.DataContext as ChatMessageItem;
            if (msg == null) return;

            string rawUrl = msg.ExtractedVideoUrl;
            if (string.IsNullOrEmpty(rawUrl)) return;

            string fullUrl = ImageHelper.FormatQiniuUrl(rawUrl, 0, 0);

            Frame.Navigate(typeof(VideoPlayerPage), new VideoPlayerNavArgs
            {
                VideoUrl = fullUrl,
                Title = string.Format("{0} 的视频", msg.DisplaySenderName)
            });
        }

        /// <summary>
        /// 点击图片消息预览块，进入全屏图片预览器
        /// </summary>
        private void ImageBubble_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            var element = sender as FrameworkElement;
            if (element == null) return;

            var msg = element.DataContext as ChatMessageItem;
            if (msg == null) return;

            string url = msg.ExtractedImageUrl;
            if (!string.IsNullOrEmpty(url))
            {
                Frame.Navigate(typeof(ImageViewerPage), new ImageViewerNavArgs
                {
                    ImageUrl = url,
                    Title = string.Format("{0} 的图片", msg.DisplaySenderName)
                });
            }
        }

        /// <summary>
        /// 点击 HTML 网页富文本消息卡片，直接打开复制/文本查看页 (不渲染原生 HTML，避免卡顿与排版异常)
        /// </summary>
        private void HtmlBubble_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            var element = sender as FrameworkElement;
            if (element == null) return;

            var msg = element.DataContext as ChatMessageItem;
            if (msg == null) return;

            string textToView = msg.Text ?? "";
            Frame.Navigate(typeof(TextViewerPage), textToView);
        }

        /// <summary>
        /// 点击动态消息预览卡片，直接跳转至动态详情页 (PostDetailPage)
        /// </summary>
        private void PostBubble_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            var element = sender as FrameworkElement;
            if (element == null) return;

            var msg = element.DataContext as ChatMessageItem;
            if (msg == null) return;

            string pid = !string.IsNullOrEmpty(msg.ParsedPostId) ? msg.ParsedPostId : msg.PostId;
            long parsedId = 0;
            if (!string.IsNullOrEmpty(pid) && long.TryParse(pid, out parsedId) && parsedId > 0)
            {
                try
                {
                    var navArgs = new PostDetailNavigationArgs
                    {
                        PostId = parsedId,
                        Token = _token
                    };
                    Frame.Navigate(typeof(PostDetailPage), navArgs);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("ChatPage", "跳转动态详情异常: " + ex.Message);
                }
            }
            else
            {
                // 如果没有提取到有效 postId，若有标题或正文文本，降级打开文本查看器
                if (!string.IsNullOrEmpty(msg.Text))
                {
                    Frame.Navigate(typeof(TextViewerPage), msg.Text);
                }
            }
        }


        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            _isLoadingHistory = false;
            if (string.IsNullOrEmpty(_token))
            {
                _token = await TokenManager.GetTokenAsync();
            }
            await LoadHistoryMessagesAsync();
        }

        private async Task ShowToastAsync(string message)
        {
            var dialog = new MessageDialog(message, "云湖");
            await dialog.ShowAsync();
        }
    }
}
