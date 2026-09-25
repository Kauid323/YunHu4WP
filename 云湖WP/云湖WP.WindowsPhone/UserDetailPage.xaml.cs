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
using 云湖WP.Api.User.Info;
using 云湖WP.Token;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 用户详细资料页面 (Metro 直角风格)
    /// </summary>
    public sealed partial class UserDetailPage : Page
    {
        private string _token = "";
        private string _userId = "";
        private string _userName = "";
        private string _avatarUrl = "";

        public UserDetailPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            var args = e.Parameter as UserDetailNavArgs;
            if (args != null)
            {
                _userId = args.UserId ?? "";
                _userName = args.Name ?? "";
                _avatarUrl = args.AvatarUrl ?? "";
            }
            else if (e.Parameter is string)
            {
                _userId = (string)e.Parameter;
            }

            // 初始显示传入的概要信息
            UpdateInitialUI();

            // 从服务端拉取完整资料
            LoadUserDetailAsync();
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

        private void UpdateInitialUI()
        {
            TxtUserName.Text = !string.IsNullOrEmpty(_userName) ? _userName : (!string.IsNullOrEmpty(_userId) ? _userId : "云湖用户");
            TxtUserId.Text = !string.IsNullOrEmpty(_userId) ? ("ID: " + _userId) : "ID: -";
            TxtAvatarLetter.Text = !string.IsNullOrEmpty(_userName) ? _userName.Substring(0, 1).ToUpper() : "云";

            if (!string.IsNullOrEmpty(_avatarUrl))
            {
                LoadAvatarImage(_avatarUrl);
            }
        }

        private async void LoadUserDetailAsync()
        {
            if (string.IsNullOrEmpty(_token))
            {
                _token = await TokenManager.GetTokenAsync();
            }
            if (string.IsNullOrEmpty(_token)) return;

            UserProgressBar.Visibility = Visibility.Visible;
            string errMsg = null;

            try
            {
                if (!string.IsNullOrEmpty(_userId))
                {
                    var res = await UserApi.GetUserDetailAsync(_token, _userId);
                    if (res.IsSuccess && res.Data != null)
                    {
                        BindUserDetail(res.Data);
                    }
                    else if (!res.IsSuccess && !string.IsNullOrEmpty(res.Msg))
                    {
                        errMsg = "获取资料失败: " + res.Msg;
                    }
                }
                else
                {
                    // 查询自身资料
                    var selfRes = await UserApi.GetUserInfoAsync(_token);
                    if (selfRes.IsSuccess)
                    {
                        _userId = selfRes.Id;
                        _userName = selfRes.Name;
                        _avatarUrl = selfRes.AvatarUrl;

                        var detailModel = new UserDetailModel
                        {
                            Id = selfRes.Id,
                            Name = selfRes.Name,
                            AvatarUrl = selfRes.AvatarUrl,
                            IsVip = selfRes.IsVip,
                            Coin = selfRes.Coin
                        };
                        BindUserDetail(detailModel);

                        // 再拉取自身详细资料补充字段
                        if (!string.IsNullOrEmpty(selfRes.Id))
                        {
                            var fullRes = await UserApi.GetUserDetailAsync(_token, selfRes.Id);
                            if (fullRes.IsSuccess && fullRes.Data != null)
                            {
                                BindUserDetail(fullRes.Data);
                            }
                        }
                    }
                    else
                    {
                        errMsg = "获取自身资料失败: " + selfRes.Msg;
                    }
                }
            }
            catch (Exception ex)
            {
                errMsg = "网络异常: " + ex.Message;
            }
            finally
            {
                UserProgressBar.Visibility = Visibility.Collapsed;
            }

            if (errMsg != null)
            {
                await ShowToastAsync(errMsg);
            }
        }

        private void BindUserDetail(UserDetailModel model)
        {
            if (model == null) return;

            if (!string.IsNullOrEmpty(model.Name)) _userName = model.Name;
            if (!string.IsNullOrEmpty(model.Id)) _userId = model.Id;
            if (!string.IsNullOrEmpty(model.AvatarUrl)) _avatarUrl = model.AvatarUrl;

            TxtUserName.Text = model.DisplayName;
            TxtUserId.Text = "ID: " + model.Id;
            TxtAvatarLetter.Text = model.AvatarLetter;

            BorderVip.Visibility = model.IsVip ? Visibility.Visible : Visibility.Collapsed;
            TxtIpGeo.Text = !string.IsNullOrEmpty(model.IpGeo) ? ("IP属地: " + model.IpGeo) : "IP属地: -";

            TxtRegisterTime.Text = !string.IsNullOrEmpty(model.RegisterTime) ? model.RegisterTime : "-";
            TxtOnlineDays.Text = string.Format("{0} 天 (连续 {1} 天)", model.OnlineDays, model.ContinuousOnlineDays);
            TxtGender.Text = model.GenderText;
            TxtBirthday.Text = model.BirthdayText;
            TxtLastActive.Text = !string.IsNullOrEmpty(model.LastActiveTime) ? model.LastActiveTime : "-";

            TxtIntroduction.Text = !string.IsNullOrEmpty(model.Introduction) ? model.Introduction : "暂无个性签名";
            TxtMedals.Text = model.MedalsText;

            if (!string.IsNullOrEmpty(model.AvatarUrl))
            {
                LoadAvatarImage(model.AvatarUrl);
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

        private void BtnSendMessage_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_userId)) return;

            var navArgs = new ChatNavigationArgs
            {
                ChatId = _userId,
                ChatType = 1,
                Title = !string.IsNullOrEmpty(_userName) ? _userName : _userId,
                AvatarUrl = _avatarUrl,
                Token = _token
            };

            Frame.Navigate(typeof(ChatPage), navArgs);
        }

        private async void BtnCopyId_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_userId)) return;
            await ShowToastAsync("用户 ID: " + _userId);
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void Avatar_Tapped(object sender, TappedRoutedEventArgs e)
        {
            string letter = !string.IsNullOrEmpty(_userName) ? _userName.Substring(0, 1).ToUpper() : "云";
            var navArgs = new ImageViewerNavArgs
            {
                ImageUrl = _avatarUrl ?? "",
                Title = (!string.IsNullOrEmpty(_userName) ? _userName : "用户") + " 头像",
                FallbackLetter = letter
            };
            Frame.Navigate(typeof(ImageViewerPage), navArgs);
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadUserDetailAsync();
        }

        private async Task ShowToastAsync(string message)
        {
            var dialog = new MessageDialog(message, "云湖");
            await dialog.ShowAsync();
        }
    }
}
