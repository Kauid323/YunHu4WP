using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Phone.UI.Input;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Message;
using 云湖WP.Token;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 云湖WP 聊天详情页面 (纯正 Metro 现代直角几何风格，支持划到顶部自动分页加载更早历史消息)
    /// </summary>
    public sealed partial class ChatPage : Page
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

        public ChatPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Disabled;
            this.BottomAppBar = null;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            this.BottomAppBar = null;

            // 注册硬件返回按键事件
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            // 读取导航参数
            var args = e.Parameter as ChatNavigationArgs;
            if (args != null)
            {
                _chatId = args.ChatId;
                _chatType = args.ChatType;
                _title = args.Title;
                _avatarUrl = args.AvatarUrl;
                _token = args.Token;
            }

            if (string.IsNullOrEmpty(_token))
            {
                _token = await TokenManager.GetTokenAsync();
            }

            TxtChatTitle.Text = !string.IsNullOrEmpty(_title) ? _title : "云湖会话";

            ChatListView.ItemsSource = _messageList;

            _hasMoreHistory = true;
            _isLoadingHistory = false;
            await LoadHistoryMessagesAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;

            if (_chatScrollViewer != null)
            {
                _chatScrollViewer.ViewChanged -= ChatScrollViewer_ViewChanged;
                _chatScrollViewer = null;
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
            var sv = sender as ScrollViewer;
            if (sv == null) return;

            // 当滑动到顶部 (VerticalOffset <= 60) 且有更多历史数据且当前不在加载中时触发
            if (sv.VerticalOffset <= 60 && !_isLoadingHistory && _hasMoreHistory && _messageList.Count > 0)
            {
                await LoadMoreHistoryMessagesAsync();
            }
        }

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
            if (string.IsNullOrEmpty(_token) || string.IsNullOrEmpty(_chatId)) return;

            _isLoadingHistory = true;
            _hasMoreHistory = true;
            string errMsg = null;
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
                        _messageList.Add(m);
                    }

                    EmptyMsgPanel.Visibility = Visibility.Collapsed;
                    _hasMoreHistory = (list.Count >= 10);
                    if (TopLoadMoreBorder != null)
                    {
                        TopLoadMoreBorder.Visibility = _hasMoreHistory ? Visibility.Visible : Visibility.Collapsed;
                    }
                    ScrollToBottom();

                    // 平滑预加载发送者头像
                    PreloadSenderAvatars(list);
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
            }
            finally
            {
                _isLoadingHistory = false;
            }

            // 确保绑定内部 ScrollViewer
            EnsureScrollViewerAttached();

            if (errMsg != null)
            {
                await ShowToastAsync(errMsg);
            }
        }

        /// <summary>
        /// 划到顶部时自动分页拉取更早的历史消息 (优先使用 /v1/msg/list-message 按 msgId 分页，兼容 by-seq)
        /// </summary>
        private async Task LoadMoreHistoryMessagesAsync()
        {
            if (_isLoadingHistory || !_hasMoreHistory || string.IsNullOrEmpty(_token) || string.IsNullOrEmpty(_chatId) || _messageList.Count == 0)
            {
                return;
            }

            _isLoadingHistory = true;
            if (TxtTopLoadMore != null)
            {
                TxtTopLoadMore.Text = "正在加载更早历史消息...";
            }

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

                    if (newOlderList.Count > 0)
                    {
                        // 确保新拉取的消息按时间升序排列
                        newOlderList.Sort((a, b) => a.SendTime.CompareTo(b.SendTime));

                        // 记录插入前顶部的消息，以便插入后保持视图位置不跳变
                        var previousTopItem = _messageList.Count > 0 ? _messageList[0] : null;

                        // 倒序依次插入到头部 index 0
                        for (int i = newOlderList.Count - 1; i >= 0; i--)
                        {
                            _messageList.Insert(0, newOlderList[i]);
                        }

                        // 恢复之前的顶部消息位置，避免视口跳动
                        if (previousTopItem != null)
                        {
                            try
                            {
                                ChatListView.ScrollIntoView(previousTopItem, ScrollIntoViewAlignment.Leading);
                            }
                            catch
                            {
                                try { ChatListView.ScrollIntoView(previousTopItem); } catch { }
                            }
                        }

                        // 允许继续加载下一页更早的历史消息
                        _hasMoreHistory = true;
                        if (TopLoadMoreBorder != null)
                        {
                            TopLoadMoreBorder.Visibility = Visibility.Visible;
                        }

                        // 后台预加载新拉取历史消息的头像
                        PreloadSenderAvatars(newOlderList);
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
                System.Diagnostics.Debug.WriteLine("LoadMoreHistoryMessages failed: " + ex.Message);
            }

            if (TxtTopLoadMore != null)
            {
                TxtTopLoadMore.Text = _hasMoreHistory ? "点击或继续上滑加载更早消息..." : "已加载全部历史消息";
            }

            // 短暂延迟 300ms 释放加载状态，防止高频惯性滚动重复触发 (C# 5.0 语法兼容)
            await Task.Delay(300);
            _isLoadingHistory = false;
        }

        /// <summary>
        /// 后台异步低优先级预加载消息发送者头像
        /// </summary>
        private void PreloadSenderAvatars(IEnumerable<ChatMessageItem> items)
        {
            if (items == null) return;
            var list = new List<ChatMessageItem>(items);

            Task.Run(async () =>
            {
                await Task.Delay(100);

                foreach (var item in list)
                {
                    if (item == null || string.IsNullOrEmpty(item.SenderAvatarUrl) || item.SenderAvatarBitmap != null) continue;

                    var currentItem = item;
                    string finalUrl = ImageHelper.FormatQiniuUrl(currentItem.SenderAvatarUrl, 72, 72);

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
                                    currentItem.SenderAvatarBitmap = bmp;
                                }
                            });
                        }
                    }
                    catch { }

                    await Task.Delay(15);
                }
            });
        }

        private void ScrollToBottom()
        {
            if (_messageList.Count > 0)
            {
                try
                {
                    ChatListView.ScrollIntoView(_messageList[_messageList.Count - 1]);
                }
                catch { }
            }
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            await DoSendMessageAsync();
        }

        private async void TxtInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                await DoSendMessageAsync();
            }
        }

        private async Task DoSendMessageAsync()
        {
            string text = TxtInput.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            TxtInput.Text = "";

            // 本地乐观添加消息
            var localMsg = new ChatMessageItem
            {
                MsgId = Guid.NewGuid().ToString("N"),
                ChatId = _chatId,
                ChatType = _chatType,
                Direction = "right",
                ContentType = 1,
                Text = text.Trim(),
                SendTime = DateTime.UtcNow.Ticks / 10000 - 62135596800000L, // UTC ms
                SenderName = "我"
            };

            _messageList.Add(localMsg);
            EmptyMsgPanel.Visibility = Visibility.Collapsed;
            ScrollToBottom();

            string sendErr = null;
            try
            {
                var res = await MessageApi.SendTextMessageAsync(_token, _chatId, _chatType, text.Trim());
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

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadHistoryMessagesAsync();
        }

        private async Task ShowToastAsync(string message)
        {
            var dialog = new MessageDialog(message, "云湖");
            await dialog.ShowAsync();
        }
    }
}
