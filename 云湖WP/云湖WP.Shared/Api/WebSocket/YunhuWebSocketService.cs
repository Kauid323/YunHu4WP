using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Data.Json;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using 云湖WP.Api.Message;
using 云湖WP.Api.Protobuf;
using 云湖WP.Utils;

namespace 云湖WP.Api.WebSocket
{
    /// <summary>
    /// 云湖 WebSocket 实时长连接服务 (单例模式)
    /// 负责心跳维持、登录鉴权、实时接收推送消息与会话更新通知
    /// </summary>
    public sealed class YunhuWebSocketService
    {
        private static readonly Lazy<YunhuWebSocketService> _instance =
            new Lazy<YunhuWebSocketService>(() => new YunhuWebSocketService());

        public static YunhuWebSocketService Instance
        {
            get { return _instance.Value; }
        }

        private MessageWebSocket _socket;
        private string _userId = "";
        private string _userToken = "";
        private bool _isConnecting = false;
        private bool _isConnected = false;
        private DispatcherTimer _heartbeatTimer;
        private DispatcherTimer _reconnectTimer;
        private int _reconnectAttempts = 0;

        /// <summary>
        /// 当前用户正在查看的活跃会话 ID (用于免打扰或不增加未读计数)
        /// </summary>
        public string CurrentActiveChatId { get; set; }

        public bool IsConnected
        {
            get { return _isConnected; }
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    DispatchToUI(() =>
                    {
                        if (OnConnectionStateChanged != null)
                        {
                            OnConnectionStateChanged(_isConnected);
                        }
                    });
                }
            }
        }

        public event Action<ChatMessageItem> OnNewMessageReceived;
        public event Action<ChatMessageItem> OnMessageEdited;
        public event Action<string, string, int> OnMessageRecalled; // msgId, chatId, chatType
        public event Action<bool> OnConnectionStateChanged;

        private YunhuWebSocketService()
        {
            CurrentActiveChatId = "";
        }

        /// <summary>
        /// 启动 WebSocket 连接
        /// </summary>
        public async Task StartAsync(string userId, string token)
        {
            if (string.IsNullOrEmpty(token)) return;

            _userId = userId ?? "";
            _userToken = token;
            _reconnectAttempts = 0;

            await ConnectInternalAsync();
        }

        /// <summary>
        /// 关闭并重置 WebSocket
        /// </summary>
        public void Stop()
        {
            StopHeartbeat();
            StopReconnectTimer();

            _isConnected = false;
            _isConnecting = false;

            if (_socket != null)
            {
                try
                {
                    _socket.Closed -= Socket_Closed;
                    _socket.MessageReceived -= Socket_MessageReceived;
                    _socket.Dispose();
                }
                catch { }
                _socket = null;
            }

            AppLogger.Log("WebSocket", "WebSocket 连接已主动停止");
        }

        private async Task ConnectInternalAsync()
        {
            if (_isConnecting || (IsConnected && _socket != null)) return;
            _isConnecting = true;

            try
            {
                Stop();
                _isConnecting = true;

                AppLogger.Log("WebSocket", "开始连接 wss://chat-ws-go.jwzhd.com/ws ...");

                _socket = new MessageWebSocket();
                _socket.Control.MessageType = SocketMessageType.Binary;
                _socket.MessageReceived += Socket_MessageReceived;
                _socket.Closed += Socket_Closed;

                await _socket.ConnectAsync(new Uri("wss://chat-ws-go.jwzhd.com/ws"));

                IsConnected = true;
                _isConnecting = false;
                _reconnectAttempts = 0;
                AppLogger.Log("WebSocket", "✅ WebSocket 连接成功，开始发送登录鉴权...");

                // 1. 发送登录数据包
                await SendLoginAsync();

                // 2. 启动 25 秒心跳保活定时器
                StartHeartbeat();
            }
            catch (Exception ex)
            {
                _isConnecting = false;
                IsConnected = false;
                AppLogger.Log("WebSocket", "❌ WebSocket 连接失败: " + ex.Message);
                ScheduleReconnect();
            }
        }

        private void Socket_Closed(IWebSocket sender, WebSocketClosedEventArgs args)
        {
            AppLogger.Log("WebSocket", string.Format("WebSocket 已断开: Code={0}, Reason={1}", args.Code, args.Reason));
            IsConnected = false;
            _isConnecting = false;
            StopHeartbeat();
            ScheduleReconnect();
        }

        private void Socket_MessageReceived(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
        {
            try
            {
                using (var reader = args.GetDataReader())
                {
                    uint len = reader.UnconsumedBufferLength;
                    if (len == 0) return;

                    byte[] buffer = new byte[len];
                    reader.ReadBytes(buffer);

                    // 判断是否为文本/JSON
                    if (buffer[0] == '{')
                    {
                        string text = System.Text.Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                        AppLogger.Log("WebSocket", "收到文本帧: " + text);
                        return;
                    }

                    // 二进制 Protobuf 帧解码
                    var decoded = WsProtobufCodec.DecodeFrame(buffer, _userId);
                    if (decoded != null)
                    {
                        AppLogger.Log("WebSocket", string.Format("📩 收到推送指令 [{0}], Seq={1}", decoded.Cmd, decoded.Seq));

                        if (decoded.Cmd == "push_message" && decoded.MessageItem != null)
                        {
                            var msg = decoded.MessageItem;

                            // 当收到新消息时，若非自己发送且非当前活跃查看的会话，弹出系统 Toast 通知与 Live Tile 磁贴更新
                            if (!msg.IsSelf && (string.IsNullOrEmpty(CurrentActiveChatId) || CurrentActiveChatId != msg.ChatId))
                            {
                                NotificationHelper.ShowMessageToast(msg);
                                NotificationHelper.UpdateLiveTile(!string.IsNullOrEmpty(msg.SenderName) ? msg.SenderName : "云湖", NotificationHelper.GetMessageSummary(msg));
                            }

                            DispatchToUI(() =>
                            {
                                if (OnNewMessageReceived != null)
                                {
                                    OnNewMessageReceived(msg);
                                }
                            });
                        }
                        else if (decoded.Cmd == "edit_message" && decoded.MessageItem != null)
                        {
                            var msg = decoded.MessageItem;
                            DispatchToUI(() =>
                            {
                                if (OnMessageEdited != null)
                                {
                                    OnMessageEdited(msg);
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("WebSocket", "处理消息帧异常: " + ex.Message);
            }
        }

        private async Task SendLoginAsync()
        {
            var dataObj = new JsonObject();
            dataObj.SetNamedValue("userId", JsonValue.CreateStringValue(_userId));
            dataObj.SetNamedValue("token", JsonValue.CreateStringValue(_userToken));
            dataObj.SetNamedValue("platform", JsonValue.CreateStringValue("windows"));
            dataObj.SetNamedValue("deviceId", JsonValue.CreateStringValue("wp81_" + (!string.IsNullOrEmpty(_userId) ? _userId : "client")));

            var rootObj = new JsonObject();
            rootObj.SetNamedValue("seq", JsonValue.CreateStringValue(Guid.NewGuid().ToString("N")));
            rootObj.SetNamedValue("cmd", JsonValue.CreateStringValue("login"));
            rootObj.SetNamedValue("data", dataObj);

            string jsonStr = rootObj.Stringify();
            AppLogger.Log("WebSocket", "发送登录鉴权: " + jsonStr);
            await SendStringAsync(jsonStr);
        }

        private async Task SendHeartbeatAsync()
        {
            if (!IsConnected || _socket == null) return;

            var rootObj = new JsonObject();
            rootObj.SetNamedValue("seq", JsonValue.CreateStringValue(Guid.NewGuid().ToString("N")));
            rootObj.SetNamedValue("cmd", JsonValue.CreateStringValue("heartbeat"));
            rootObj.SetNamedValue("data", new JsonObject());

            string jsonStr = rootObj.Stringify();
            await SendStringAsync(jsonStr);
        }

        private async Task SendStringAsync(string str)
        {
            if (_socket == null) return;
            try
            {
                using (var writer = new DataWriter(_socket.OutputStream))
                {
                    writer.WriteString(str);
                    await writer.StoreAsync();
                    writer.DetachStream();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("WebSocket", "SendStringAsync error: " + ex.Message);
            }
        }

        private void StartHeartbeat()
        {
            StopHeartbeat();
            DispatchToUI(() =>
            {
                _heartbeatTimer = new DispatcherTimer();
                _heartbeatTimer.Interval = TimeSpan.FromSeconds(25);
                _heartbeatTimer.Tick += async (s, e) =>
                {
                    await SendHeartbeatAsync();
                };
                _heartbeatTimer.Start();
            });
        }

        private void StopHeartbeat()
        {
            if (_heartbeatTimer != null)
            {
                _heartbeatTimer.Stop();
                _heartbeatTimer = null;
            }
        }

        private void ScheduleReconnect()
        {
            StopReconnectTimer();
            if (string.IsNullOrEmpty(_userToken)) return;

            _reconnectAttempts++;
            int delaySeconds = Math.Min(30, _reconnectAttempts * 3);

            AppLogger.Log("WebSocket", string.Format("{0} 秒后尝试第 {1} 次重连...", delaySeconds, _reconnectAttempts));

            DispatchToUI(() =>
            {
                _reconnectTimer = new DispatcherTimer();
                _reconnectTimer.Interval = TimeSpan.FromSeconds(delaySeconds);
                _reconnectTimer.Tick += async (s, e) =>
                {
                    StopReconnectTimer();
                    await ConnectInternalAsync();
                };
                _reconnectTimer.Start();
            });
        }

        private void StopReconnectTimer()
        {
            if (_reconnectTimer != null)
            {
                _reconnectTimer.Stop();
                _reconnectTimer = null;
            }
        }

        private async void DispatchToUI(Action action)
        {
            if (action == null) return;
            try
            {
                var dispatcher = CoreApplication.MainView.CoreWindow.Dispatcher;
                if (dispatcher.HasThreadAccess)
                {
                    action();
                }
                else
                {
                    await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => action());
                }
            }
            catch { }
        }
    }
}
