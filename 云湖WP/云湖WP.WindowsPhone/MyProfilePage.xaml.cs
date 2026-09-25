using System;
using System.Threading.Tasks;
using Windows.Phone.UI.Input;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Message;
using 云湖WP.Api.User;
using 云湖WP.Api.User.Self;
using 云湖WP.Token;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 当前登录用户自身个人信息界面 (GET /v1/user/info，Metro 直角排版)
    /// </summary>
    public sealed partial class MyProfilePage : Page
    {
        private string _token = "";
        private string _userId = "";
        private string _userName = "";
        private string _avatarUrl = "";
        private string _inviteCode = "";

        public MyProfilePage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            LoadSelfProfileAsync();
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

        private async void LoadSelfProfileAsync()
        {
            if (string.IsNullOrEmpty(_token))
            {
                _token = await TokenManager.GetTokenAsync();
            }
            if (string.IsNullOrEmpty(_token)) return;

            ProfileProgressBar.Visibility = Visibility.Visible;
            string errMsg = null;

            try
            {
                var selfRes = await UserApi.GetSelfInfoAsync(_token);
                if (selfRes != null && selfRes.IsSuccess && selfRes.Data != null)
                {
                    BindSelfProfile(selfRes.Data);
                }
                else
                {
                    errMsg = "获取个人信息失败: " + (selfRes != null ? selfRes.Msg : "未知错误");
                }
            }
            catch (Exception ex)
            {
                errMsg = "网络连接异常: " + ex.Message;
            }
            finally
            {
                ProfileProgressBar.Visibility = Visibility.Collapsed;
            }

            if (errMsg != null)
            {
                await ShowToastAsync(errMsg);
            }
        }

        private void BindSelfProfile(UserSelfInfoModel data)
        {
            if (data == null) return;

            _userId = data.Id ?? "";
            _userName = data.Name ?? "";
            _avatarUrl = data.AvatarUrl ?? "";
            _inviteCode = data.InvitationCode ?? "";

            TxtUserName.Text = data.DisplayName;
            TxtAvatarLetter.Text = data.AvatarLetter;
            TxtUserIdHeader.Text = !string.IsNullOrEmpty(data.Id) ? ("ID: " + data.Id) : "ID: -";
            TxtUserId.Text = !string.IsNullOrEmpty(data.Id) ? data.Id : "-";
            TxtNickName.Text = !string.IsNullOrEmpty(data.Name) ? data.Name : "-";
            TxtPhone.Text = !string.IsNullOrEmpty(data.Phone) ? data.Phone : "未绑定";
            TxtEmail.Text = !string.IsNullOrEmpty(data.Email) ? data.Email : "未绑定";
            TxtCoin.Text = string.Format("{0} 金币", data.Coin);

            string vipLabel = data.IsVip ? "VIP 会员" : "普通用户";
            TxtVipStatus.Text = vipLabel;
            TxtVipBadgeText.Text = vipLabel;
            BorderVip.Visibility = data.IsVip ? Visibility.Visible : Visibility.Collapsed;

            if (data.IsVip && data.VipExpiredTimestamp > 0)
            {
                try
                {
                    var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    var dt = epoch.AddSeconds(data.VipExpiredTimestamp).ToLocalTime();
                    TxtVipExpired.Text = dt.ToString("yyyy-MM-dd HH:mm");
                    PanelVipExpired.Visibility = Visibility.Visible;
                }
                catch
                {
                    PanelVipExpired.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                PanelVipExpired.Visibility = Visibility.Collapsed;
            }

            TxtInviteCode.Text = !string.IsNullOrEmpty(data.InvitationCode) ? data.InvitationCode : "暂无";

            if (!string.IsNullOrEmpty(data.AvatarUrl))
            {
                LoadAvatarImage(data.AvatarUrl);
            }
        }

        private void LoadAvatarImage(string avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl)) return;
            string finalUrl = ImageHelper.FormatQiniuUrl(avatarUrl, 144, 144);

            Task.Run(async () =>
            {
                try
                {
                    byte[] bytes = await ImageLoader.GetImageBytesAsync(finalUrl);
                    if (bytes != null && bytes.Length > 0)
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                        {
                            var bmp = await ImageLoader.BytesToBitmapImageAsync(bytes, 144, 144);
                            if (bmp != null)
                            {
                                ImgAvatar.Source = bmp;
                            }
                        });
                    }
                }
                catch { }
            });
        }

        private void Avatar_Tapped(object sender, TappedRoutedEventArgs e)
        {
            string letter = !string.IsNullOrEmpty(_userName) ? _userName.Substring(0, 1).ToUpper() : "云";
            var navArgs = new ImageViewerNavArgs
            {
                ImageUrl = _avatarUrl ?? "",
                Title = (!string.IsNullOrEmpty(_userName) ? _userName : "我的") + " 头像",
                FallbackLetter = letter
            };
            Frame.Navigate(typeof(ImageViewerPage), navArgs);
        }

        private async void BtnCopyId_Click(object sender, RoutedEventArgs e)
        {
            await CopyIdInternalAsync();
        }

        private async void BtnCopyId_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await CopyIdInternalAsync();
        }

        private async Task CopyIdInternalAsync()
        {
            if (string.IsNullOrEmpty(_userId)) return;
            await ShowToastAsync("用户 ID: " + _userId);
        }

        private async void BtnCopyInviteCode_Click(object sender, RoutedEventArgs e)
        {
            await CopyInviteCodeInternalAsync();
        }

        private async void BtnCopyInviteCode_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await CopyInviteCodeInternalAsync();
        }

        private async Task CopyInviteCodeInternalAsync()
        {
            if (string.IsNullOrEmpty(_inviteCode))
            {
                await ShowToastAsync("暂无可用邀请码");
                return;
            }
            await ShowToastAsync("我的邀请码: " + _inviteCode);
        }

        private void BtnEditProfile_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ProfileEditPage));
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadSelfProfileAsync();
        }

        private async Task ShowToastAsync(string message)
        {
            var dialog = new MessageDialog(message, "云湖");
            await dialog.ShowAsync();
        }
    }
}
