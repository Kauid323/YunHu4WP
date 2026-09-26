using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Phone.UI.Input;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Conversation;
using 云湖WP.Api.Friend;
using 云湖WP.Api.Message;
using 云湖WP.Token;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 我的好友独立列表页面
    /// </summary>
    public sealed partial class FriendsPage : Page
    {
        private string _token = "";
        private ObservableCollection<FriendContactItem> _friendsList = new ObservableCollection<FriendContactItem>();
        private bool _isLoading = false;

        public FriendsPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            if (PageCommandBar != null) PageCommandBar.IsOpen = false;

            _token = await TokenManager.GetTokenAsync();
            FriendsListView.ItemsSource = _friendsList;

            await LoadFriendsAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            e.Handled = true;
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private async Task LoadFriendsAsync()
        {
            if (_isLoading) return;
            _isLoading = true;

            if (LoadingProgressBar != null) LoadingProgressBar.Visibility = Visibility.Visible;
            if (EmptyPanel != null) EmptyPanel.Visibility = Visibility.Collapsed;

            string errMsg = null;
            try
            {
                var result = await FriendApi.GetAddressBookListAsync(_token);
                if (result.IsSuccess && result.Friends != null)
                {
                    _friendsList.Clear();
                    foreach (var item in result.Friends)
                    {
                        _friendsList.Add(item);
                        if (item != null && !string.IsNullOrEmpty(item.ChatId) && !string.IsNullOrEmpty(item.DisplayName))
                        {
                            NotificationHelper.RegisterChatTitle(item.ChatId, item.DisplayName);
                        }
                    }

                    if (_friendsList.Count == 0 && EmptyPanel != null)
                    {
                        EmptyPanel.Visibility = Visibility.Visible;
                    }

                    PreloadAvatars(result.Friends);
                }
                else
                {
                    if (_friendsList.Count == 0 && EmptyPanel != null)
                    {
                        EmptyPanel.Visibility = Visibility.Visible;
                    }
                    if (!result.IsSuccess && !string.IsNullOrEmpty(result.Msg))
                    {
                        errMsg = result.Msg;
                    }
                }
            }
            catch (Exception ex)
            {
                errMsg = "加载失败: " + ex.Message;
            }
            finally
            {
                if (LoadingProgressBar != null) LoadingProgressBar.Visibility = Visibility.Collapsed;
                _isLoading = false;
            }

            if (errMsg != null)
            {
                var dialog = new MessageDialog(errMsg, "云湖好友");
                await dialog.ShowAsync();
            }
        }

        private void PreloadAvatars(IEnumerable<FriendContactItem> items)
        {
            if (ImageLoader.DisableAllImages || items == null) return;
            var list = new List<FriendContactItem>(items);

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
                            await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                            {
                                try
                                {
                                    var bmp = new BitmapImage();
                                    bmp.DecodePixelWidth = 96;
                                    bmp.DecodePixelHeight = 96;
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
                                    cur.AvatarBitmap = bmp;
                                }
                                catch { }
                            });
                        }
                    }
                    catch { }
                    await Task.Delay(20);
                }
            });
        }

        private void FriendsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as FriendContactItem;
            if (item == null || string.IsNullOrEmpty(item.ChatId)) return;

            Frame.Navigate(typeof(ChatPage), new ChatNavigationArgs
            {
                ChatId = item.ChatId,
                ChatType = 1,
                Title = item.DisplayName,
                AvatarUrl = item.AvatarUrl ?? "",
                Token = _token
            });
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadFriendsAsync();
        }
    }
}
