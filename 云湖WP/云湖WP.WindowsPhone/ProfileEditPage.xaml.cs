using System;
using System.Threading.Tasks;
using Windows.Phone.UI.Input;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.User;
using 云湖WP.Api.User.Edit;
using 云湖WP.Token;

namespace 云湖WP
{
    /// <summary>
    /// 资料修改页面 (Windows Phone 8.1 Metro 风格)
    /// </summary>
    public sealed partial class ProfileEditPage : Page
    {
        private string _token = "";

        public ProfileEditPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            _token = await TokenManager.GetTokenAsync();
            LoadCurrentProfileAsync();
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

        private async void LoadCurrentProfileAsync()
        {
            if (string.IsNullOrEmpty(_token)) return;

            EditProgressBar.Visibility = Visibility.Visible;
            try
            {
                var selfRes = await UserApi.GetSelfInfoAsync(_token);
                if (selfRes.IsSuccess && selfRes.Data != null)
                {
                    TxtName.Text = selfRes.Data.Name ?? "";
                    TxtIntroduction.Text = selfRes.Data.Introduction ?? "";
                    if (!string.IsNullOrEmpty(selfRes.Data.IpGeo))
                    {
                        TxtCity.Text = selfRes.Data.IpGeo;
                    }
                }

                // 尝试拉取更详细的 UserData
                var dataRes = await UserApi.GetUserDataAsync(_token);
                if (dataRes.IsSuccess && dataRes.Data != null)
                {
                    if (string.IsNullOrEmpty(TxtIntroduction.Text))
                    {
                        TxtIntroduction.Text = dataRes.Data.Introduction ?? "";
                    }

                    if (dataRes.Data.Gender == 1) ComboGender.SelectedIndex = 1;
                    else if (dataRes.Data.Gender == 2) ComboGender.SelectedIndex = 2;
                    else ComboGender.SelectedIndex = 0;

                    if (!string.IsNullOrEmpty(dataRes.Data.City))
                    {
                        TxtCity.Text = dataRes.Data.City;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadCurrentProfile failed: " + ex.Message);
            }
            finally
            {
                EditProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_token))
            {
                await ShowToastAsync("请先登录");
                return;
            }

            int gender = 3;
            if (ComboGender.SelectedIndex == 1) gender = 1;
            else if (ComboGender.SelectedIndex == 2) gender = 2;

            var req = new UserEditRequest
            {
                Name = TxtName.Text.Trim(),
                Introduction = TxtIntroduction.Text.Trim(),
                Gender = gender,
                City = TxtCity.Text.Trim()
            };

            EditProgressBar.Visibility = Visibility.Visible;
            BtnSave.IsEnabled = false;

            string errMsg = null;
            bool isSuccess = false;

            try
            {
                var res = await UserApi.EditUserProfileAsync(_token, req);
                if (res.IsSuccess)
                {
                    isSuccess = true;
                }
                else
                {
                    errMsg = "保存失败: " + res.Msg;
                }
            }
            catch (Exception ex)
            {
                errMsg = "网络异常: " + ex.Message;
            }
            finally
            {
                EditProgressBar.Visibility = Visibility.Collapsed;
                BtnSave.IsEnabled = true;
            }

            if (isSuccess)
            {
                await ShowToastAsync("资料保存成功！");
                if (Frame.CanGoBack)
                {
                    Frame.GoBack();
                }
            }
            else if (errMsg != null)
            {
                await ShowToastAsync(errMsg);
            }
        }

        private async Task ShowToastAsync(string message)
        {
            var dialog = new MessageDialog(message, "云湖");
            await dialog.ShowAsync();
        }
    }
}
