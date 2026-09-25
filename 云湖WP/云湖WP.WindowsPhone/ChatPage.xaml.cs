using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Phone.UI.Input;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Common;
using 云湖WP.Api.Message;
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
        private double _lastScrollOffset = 0;
        private DateTime _lastLoadMoreTime = DateTime.MinValue;

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
            _isInitialLoadDone = false;
            AppLogger.Log("ChatPage", string.Format("OnNavigatedTo: ChatId={0}, Title={1}", _chatId, _title));
            await LoadHistoryMessagesAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
            InputPane.GetForCurrentView().Showing -= InputPane_Showing;
            InputPane.GetForCurrentView().Hiding -= InputPane_Hiding;

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

            // 严格防护：必须已完成初次加载、当前无加载任务、有更多历史数据
            if (_isInitialLoadDone && !_isLoadingHistory && _hasMoreHistory && _messageList.Count > 0)
            {
                // 只有在列表已具备滚动内容（高度>120）、用户主动由下方向上滑动到顶部边缘（VerticalOffset <= 10 且 _lastScrollOffset > 30）时才触发
                if (sv.ScrollableHeight > 120 && sv.VerticalOffset <= 10 && _lastScrollOffset > 30)
                {
                    if ((DateTime.Now - _lastLoadMoreTime).TotalMilliseconds > 2500)
                    {
                        AppLogger.Log("ChatPage", string.Format("User scroll reached top, trigger LoadMoreHistory: Offset={0}, Last={1}, Scrollable={2}", sv.VerticalOffset, _lastScrollOffset, sv.ScrollableHeight));
                        await LoadMoreHistoryMessagesAsync();
                    }
                }
            }
            _lastScrollOffset = sv.VerticalOffset;
        }

        private async void TopLoadMoreBorder_Tapped(object sender, TappedRoutedEventArgs e)
        {
            AppLogger.Log("ChatPage", "TopLoadMoreBorder tapped manually");
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
            _isInitialLoadDone = false;
            string errMsg = null;
            AppLogger.Log("ChatPage", "LoadHistoryMessagesAsync started");
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

                    AppLogger.Log("ChatPage", string.Format("Initial messages loaded: {0}", list.Count));
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
                AppLogger.Log("ChatPage", "LoadHistoryMessagesAsync error: " + ex.Message);
            }

            // 确保绑定内部 ScrollViewer
            EnsureScrollViewerAttached();

            // 延迟完成初始状态，避免进入页面时误触发分页
            await Task.Delay(500);
            _isInitialLoadDone = true;
            _isLoadingHistory = false;

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
            _lastLoadMoreTime = DateTime.Now;
            AppLogger.Log("ChatPage", "LoadMoreHistoryMessagesAsync starting...");

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

                    AppLogger.Log("ChatPage", string.Format("Fetched {0} history items, new older items: {1}", res.Messages.Count, newOlderList.Count));

                    if (newOlderList.Count > 0)
                    {
                        // 确保新拉取的消息按时间升序排列
                        newOlderList.Sort((a, b) => a.SendTime.CompareTo(b.SendTime));

                        // 记录插入前顶部的消息作为锚点
                        var anchorItem = _messageList.Count > 0 ? _messageList[0] : null;

                        // 倒序依次插入到头部 index 0
                        for (int i = newOlderList.Count - 1; i >= 0; i--)
                        {
                            _messageList.Insert(0, newOlderList[i]);
                        }

                        // 保持视口定位在原顶部消息
                        if (anchorItem != null)
                        {
                            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                try
                                {
                                    ChatListView.ScrollIntoView(anchorItem, ScrollIntoViewAlignment.Leading);
                                }
                                catch { }
                            });
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
                AppLogger.Log("ChatPage", "LoadMoreHistoryMessages error: " + ex.Message);
            }

            if (TxtTopLoadMore != null)
            {
                TxtTopLoadMore.Text = _hasMoreHistory ? "点击加载更早历史消息" : "已加载全部历史消息";
            }

            // 保持加载锁定至少 1200ms，防止高频惯性滚动连环触发
            await Task.Delay(1200);
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
                picker.PickSingleFileAndContinue();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("PickImage failed: " + ex.Message);
            }
#endif
        }

#if WINDOWS_PHONE_APP
        /// <summary>
        /// 接收系统相册选图回调并直传七牛云发送图片
        /// </summary>
        public async void ContinueFileOpenPicker(Windows.ApplicationModel.Activation.FileOpenPickerContinuationEventArgs args)
        {
            if (args != null && args.Files != null && args.Files.Count > 0)
            {
                var file = args.Files[0];
                await UploadAndSendLocalImageAsync(file);
            }
        }
#endif

        /// <summary>
        /// 上传本地图片到七牛云并发送图片消息 (带实时进度条显示)
        /// </summary>
        private async Task UploadAndSendLocalImageAsync(Windows.Storage.StorageFile file)
        {
            if (file == null || string.IsNullOrEmpty(_token)) return;

            string errMsg = null;
            UploadProgressPanel.Visibility = Visibility.Visible;
            UploadProgressBar.Value = 0;
            TxtUploadPercentage.Text = "0%";
            TxtUploadStatus.Text = "准备上传...";

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
                // 调用系统七牛云直传组件 (附带实时进度报告)
                string publicUrl = await QiniuUploadHelper.UploadImageAsync(file, _token, uploadProgress);
                if (!string.IsNullOrEmpty(publicUrl))
                {
                    await DoSendImageAsync(publicUrl);
                }
                else
                {
                    errMsg = "上传图片未获取到访问地址";
                }
            }
            catch (Exception ex)
            {
                errMsg = "图片上传失败: " + ex.Message;
            }
            finally
            {
                UploadProgressPanel.Visibility = Visibility.Collapsed;
            }

            if (errMsg != null)
            {
                await ShowToastAsync(errMsg);
            }
        }

        private async void AppBarBtnSendUrl_Click(object sender, RoutedEventArgs e)
        {
            string currentText = TxtInput.Text.Trim();
            if (!string.IsNullOrEmpty(currentText) && (currentText.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || currentText.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                TxtInput.Text = "";
                await DoSendImageAsync(currentText);
            }
            else
            {
                await ShowToastAsync("请先在输入框中输入或粘贴图片 URL (http/https)，然后点击此项发送");
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
            string logs = AppLogger.GetAllLogs();
            if (string.IsNullOrEmpty(logs)) logs = "暂无诊断日志";
            var dialog = new Windows.UI.Popups.MessageDialog(logs, "运行与上传诊断日志");
            await dialog.ShowAsync();
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

            // 本地乐观添加图片消息
            var localMsg = new ChatMessageItem
            {
                MsgId = Guid.NewGuid().ToString("N"),
                ChatId = _chatId,
                ChatType = _chatType,
                Direction = "right",
                ContentType = 2,
                ImageUrl = cleanUrl,
                Text = msgText,
                SendTime = DateTime.UtcNow.Ticks / 10000 - 62135596800000L, // UTC ms
                SenderName = "我"
            };

            _messageList.Add(localMsg);
            EmptyMsgPanel.Visibility = Visibility.Collapsed;
            ScrollToBottom();

            string sendErr = null;
            try
            {
                var res = await MessageApi.SendImageMessageAsync(_token, _chatId, _chatType, cleanUrl, msgText);
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
            finally
            {
                _isSending = false;
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

            if (msg.IsDownloading || msg.IsDownloaded)
            {
                return;
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

            try
            {
                var savedFile = await FileDownloadHelper.DownloadFileWithProgressAsync(
                    msg.FileUrl,
                    msg.DisplayFileName,
                    msg.FileSize,
                    downloadProgress);

                msg.IsDownloading = false;
                msg.IsDownloaded = true;
                msg.DownloadStatusText = "已下载";
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
        /// 点击用户头像进入用户详情页
        /// </summary>
        private void Avatar_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element == null) return;

            var msg = element.DataContext as ChatMessageItem;
            if (msg == null) return;

            string targetUserId = msg.IsSelf ? "" : (msg.SenderId ?? _chatId);
            string targetName = msg.IsSelf ? "我" : msg.DisplaySenderName;
            string targetAvatar = msg.IsSelf ? "" : msg.SenderAvatarUrl;

            Frame.Navigate(typeof(UserDetailPage), new 云湖WP.Api.User.Info.UserDetailNavArgs
            {
                UserId = targetUserId,
                Name = targetName,
                AvatarUrl = targetAvatar
            });
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
