using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Common;
using 云湖WP.Api.User;
using 云湖WP.Token;
using 云湖WP.Utils;
using Windows.UI;

namespace 云湖WP
{
    /// <summary>
    /// 登录页面 (支持手机号和邮箱登录)
    /// </summary>
    public sealed partial class LoginPage : Page
    {
        private DispatcherTimer _smsCountDownTimer;
        private int _countDownSeconds = 60;
        private string _captchaId = "";
        private bool _isPhoneLogin = false; // true=手机号, false=邮箱

        private const string SettingKeyEmail = "SavedEmail";
        private const string SettingKeyPhone = "SavedPhone";
        private const string SettingKeyToken = "UserToken";
        private const string SettingKeyRemember = "RememberMe";

        public LoginPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;

            InitTimer();
            ShowEmailLogin(); // 默认显示邮箱登录
        }

        private void InitTimer()
        {
            _smsCountDownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _smsCountDownTimer.Tick += SmsCountDownTimer_Tick;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            LoadSavedCredentials();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            if (_smsCountDownTimer != null && _smsCountDownTimer.IsEnabled)
            {
                _smsCountDownTimer.Stop();
            }
        }

        private void ShowEmailLogin()
        {
            _isPhoneLogin = false;
            EmailPanel.Visibility = Visibility.Visible;
            PhonePanel.Visibility = Visibility.Collapsed;
            BtnEmailToggle.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)); // Accent color
            BtnPhoneToggle.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
        }

        private void ShowPhoneLogin()
        {
            _isPhoneLogin = true;
            EmailPanel.Visibility = Visibility.Collapsed;
            PhonePanel.Visibility = Visibility.Visible;
            BtnPhoneToggle.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)); // Accent color
            BtnEmailToggle.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            RefreshCaptchaAsync();
        }

        private void BtnEmailToggle_Click(object sender, RoutedEventArgs e)
        {
            ShowEmailLogin();
        }

        private void BtnPhoneToggle_Click(object sender, RoutedEventArgs e)
        {
            ShowPhoneLogin();
        }

        private async Task RefreshCaptchaAsync()
        {
            try
            {
                var res = await YunhuApiClient.GetCaptchaAsync();
                if (res.IsSuccess && !string.IsNullOrEmpty(res.B64s))
                {
                    ImgCaptcha.Source = await ImageHelper.Base64ToBitmapImageAsync(res.B64s);
                    _captchaId = res.Id;
                    TxtCaptchaTip.Text = "点击刷新";
                }
                else
                {
                    TxtCaptchaTip.Text = "获取验证码失败，点击重试";
                }
            }
            catch (Exception)
            {
                TxtCaptchaTip.Text = "网络错误，点击重试";
            }
        }

        private async void ImgCaptcha_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await RefreshCaptchaAsync();
        }

        private async void BtnGetVerifyCode_Click(object sender, RoutedEventArgs e)
        {
            string phone = TxtPhone.Text.Trim();
            string imageCode = TxtImageCode.Text.Trim();

            if (string.IsNullOrEmpty(phone))
            {
                await ShowToastAsync("请输入手机号");
                TxtPhone.Focus(FocusState.Programmatic);
                return;
            }

            if (!Regex.IsMatch(phone, @"^1\d{10}$"))
            {
                await ShowToastAsync("请输入正确的11位手机号码");
                TxtPhone.Focus(FocusState.Programmatic);
                return;
            }

            if (string.IsNullOrEmpty(imageCode))
            {
                await ShowToastAsync("请输入图形验证码");
                TxtImageCode.Focus(FocusState.Programmatic);
                return;
            }

            BtnGetVerifyCode.IsEnabled = false;
            BtnGetVerifyCode.Content = "获取中...";
            _countDownSeconds = 60;
            _smsCountDownTimer.Start();

            try
            {
                var res = await YunhuApiClient.GetSmsVerificationCodeAsync(phone, imageCode, _captchaId);
                if (res.IsSuccess)
                {
                    await ShowToastAsync("验证码已发送");
                    TxtVerifyCode.Focus(FocusState.Programmatic);
                }
                else
                {
                    await ShowToastAsync(res.Msg ?? "验证码发送失败");
                    await RefreshCaptchaAsync();
                }
            }
            catch (Exception ex)
            {
                ShowToastAsync("验证码发送失败: " + ex.Message);
                RefreshCaptchaAsync();
            }
            finally
            {
                BtnGetVerifyCode.IsEnabled = true;
                BtnGetVerifyCode.Content = "获取验证码";
            }
        }

        private async void BtnPhoneLogin_Click(object sender, RoutedEventArgs e)
        {
            string phone = TxtPhone.Text.Trim();
            string smsCode = TxtVerifyCode.Text.Trim();

            if (string.IsNullOrEmpty(phone))
            {
                await ShowToastAsync("请输入手机号");
                TxtPhone.Focus(FocusState.Programmatic);
                return;
            }

            if (!Regex.IsMatch(phone, @"^1\d{10}$"))
            {
                await ShowToastAsync("请输入正确的11位手机号码");
                TxtPhone.Focus(FocusState.Programmatic);
                return;
            }

            if (string.IsNullOrEmpty(smsCode))
            {
                await ShowToastAsync("请输入收到的短信验证码");
                TxtVerifyCode.Focus(FocusState.Programmatic);
                return;
            }

            SetLoading(true, "正在登录云湖...");
            var res = await YunhuApiClient.PhoneVerificationLoginAsync(phone, smsCode);
            SetLoading(false);

            if (res.IsSuccess)
            {
                await SaveCredentialsAsync(phone, false, res.Token);
                Frame.Navigate(typeof(CommunityPage));
            }
            else
            {
                string errorMsg = !string.IsNullOrEmpty(res.Msg) ? res.Msg : "登录失败，请检查验证码";
                await ShowToastAsync(errorMsg);
            }
        }

        private async void BtnEmailLogin_Click(object sender, RoutedEventArgs e)
        {
            string email = TxtEmail.Text.Trim();
            string password = TxtEmailPassword.Password;

            if (string.IsNullOrEmpty(email))
            {
                await ShowToastAsync("请输入邮箱地址");
                TxtEmail.Focus(FocusState.Programmatic);
                return;
            }

            if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                await ShowToastAsync("请输入有效的邮箱格式（例如: user@example.com）");
                TxtEmail.Focus(FocusState.Programmatic);
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                await ShowToastAsync("请输入登录密码");
                TxtEmailPassword.Focus(FocusState.Programmatic);
                return;
            }

            SetLoading(true, "正在验证邮箱并登录...");
            var res = await YunhuApiClient.EmailLoginAsync(email, password);
            SetLoading(false);

            if (res.IsSuccess)
            {
                await SaveCredentialsAsync(email, true, res.Token);
                Frame.Navigate(typeof(CommunityPage));
            }
            else
            {
                string errorMsg = !string.IsNullOrEmpty(res.Msg) ? res.Msg : "登录失败，请检查邮箱与密码";
                await ShowToastAsync(errorMsg);
            }
        }

        private void SetLoading(bool isLoading, string statusText = "")
        {
            LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            ProgressIndicator.IsActive = isLoading;
            TxtLoadingStatus.Text = statusText;
            BtnEmailLogin.IsEnabled = !isLoading;
            BtnPhoneLogin.IsEnabled = !isLoading;
        }

        private async void BtnForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            await ShowToastAsync("找回密码请访问官网: https://www.yunhu.life");
        }

        private void LoadSavedCredentials()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values.ContainsKey(SettingKeyRemember))
                {
                    bool remember = (bool)localSettings.Values[SettingKeyRemember];
                    if (remember)
                    {
                        if (localSettings.Values.ContainsKey(SettingKeyEmail))
                        {
                            TxtEmail.Text = localSettings.Values[SettingKeyEmail] as string;
                        }
                        if (localSettings.Values.ContainsKey(SettingKeyPhone))
                        {
                            TxtPhone.Text = localSettings.Values[SettingKeyPhone] as string;
                        }
                        ChkRememberMe.IsChecked = true;
                    }
                }
            }
            catch { }
        }

        private async Task SaveCredentialsAsync(string identifier, bool isEmail, string token)
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values[SettingKeyToken] = token;
                localSettings.Values[SettingKeyRemember] = ChkRememberMe.IsChecked ?? false;
                await TokenManager.SaveTokenAsync(token, identifier);

                if (isEmail)
                {
                    localSettings.Values[SettingKeyEmail] = identifier;
                    localSettings.Values.Remove(SettingKeyPhone);
                }
                else
                {
                    localSettings.Values[SettingKeyPhone] = identifier;
                    localSettings.Values.Remove(SettingKeyEmail);
                }
            }
            catch { }
        }

        private async Task ShowToastAsync(string message)
        {
            var dialog = new MessageDialog(message, "云湖提示");
            await dialog.ShowAsync();
        }

        private void SmsCountDownTimer_Tick(object sender, object e)
        {
            _countDownSeconds--;
            if (_countDownSeconds <= 0)
            {
                _smsCountDownTimer.Stop();
                BtnGetVerifyCode.IsEnabled = true;
                BtnGetVerifyCode.Content = "获取验证码";
            }
            else
            {
                BtnGetVerifyCode.Content = string.Format("{0}s", _countDownSeconds);
            }
        }
    }
}
