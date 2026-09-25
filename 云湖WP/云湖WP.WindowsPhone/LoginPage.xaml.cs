using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Phone.UI.Input;
using Windows.Storage;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Token;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 云湖WP 登录页面 (精炼 Modern 界面，保留原版 ShowToastAsync 提示弹窗)
    /// </summary>
    public sealed partial class LoginPage : Page
    {
        private DispatcherTimer _smsCountDownTimer;
        private int _countDownSeconds = 60;
        private string _captchaId = "";

        private const string SettingKeyEmail = "SavedEmail";
        private const string SettingKeyPhone = "SavedPhone";
        private const string SettingKeyToken = "UserToken";
        private const string SettingKeyRemember = "RememberMe";

        public LoginPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;

            InitTimer();
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

            // 注册 Windows Phone 硬件返回按键事件
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            // 加载已记住的账号
            LoadSavedCredentials();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            // 移除返回键监听
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;

            // 停止倒计时以释放资源
            if (_smsCountDownTimer != null && _smsCountDownTimer.IsEnabled)
            {
                _smsCountDownTimer.Stop();
            }
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                e.Handled = true;
                Frame.GoBack();
            }
        }

        /// <summary>
        /// 原版 ShowToastAsync 提示弹窗 (MessageDialog)
        /// </summary>
        private async Task ShowToastAsync(string message)
        {
            var dialog = new MessageDialog(message, "云湖提示");
            await dialog.ShowAsync();
        }

        /// <summary>
        /// 读取已保存的凭据
        /// </summary>
        private void LoadSavedCredentials()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values.ContainsKey(SettingKeyRemember))
                {
                    bool remember = (bool)localSettings.Values[SettingKeyRemember];
                    ChkRememberMe.IsChecked = remember;

                    if (remember)
                    {
                        if (localSettings.Values.ContainsKey(SettingKeyEmail))
                        {
                            string email = localSettings.Values[SettingKeyEmail] as string;
                            if (!string.IsNullOrEmpty(email))
                            {
                                TxtEmail.Text = email;
                            }
                        }

                        if (localSettings.Values.ContainsKey(SettingKeyPhone))
                        {
                            string phone = localSettings.Values[SettingKeyPhone] as string;
                            if (!string.IsNullOrEmpty(phone))
                            {
                                TxtPhone.Text = phone;
                            }
                        }
                    }
                }
            }
            catch
            {
                // 忽略读取存储异常
            }
        }

        /// <summary>
        /// 保存账号配置与加密 Token
        /// </summary>
        private async Task SaveCredentialsAsync(string account, bool isEmail, string token)
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                bool isRemember = ChkRememberMe.IsChecked ?? false;
                localSettings.Values[SettingKeyRemember] = isRemember;

                if (!string.IsNullOrEmpty(token))
                {
                    await TokenManager.SaveTokenAsync(token, account);
                }

                if (isRemember)
                {
                    if (isEmail)
                    {
                        localSettings.Values[SettingKeyEmail] = account;
                    }
                    else
                    {
                        localSettings.Values[SettingKeyPhone] = account;
                    }
                }
                else
                {
                    localSettings.Values.Remove(SettingKeyEmail);
                    localSettings.Values.Remove(SettingKeyPhone);
                }
            }
            catch
            {
                // 忽略写入存储异常
            }
        }

        /// <summary>
        /// 切换到“手机号”标签时，自动获取图形验证码
        /// </summary>
        private async void LoginPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LoginPivot.SelectedIndex == 1)
            {
                if (string.IsNullOrEmpty(_captchaId))
                {
                    await RefreshCaptchaAsync();
                }
            }
        }

        /// <summary>
        /// 点击图形验证码图片进行刷新
        /// </summary>
        private async void ImgCaptcha_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await RefreshCaptchaAsync();
        }

        /// <summary>
        /// 获取或刷新人机图形验证码
        /// </summary>
        private async Task RefreshCaptchaAsync()
        {
            TxtCaptchaTip.Text = "加载中...";
            TxtCaptchaTip.Visibility = Visibility.Visible;
            ImgCaptcha.Source = null;

            var res = await YunhuApiClient.GetCaptchaAsync();
            if (res.IsSuccess && !string.IsNullOrEmpty(res.B64s))
            {
                _captchaId = res.Id;
                var bitmap = await YunhuApiClient.Base64ToBitmapImageAsync(res.B64s);
                if (bitmap != null)
                {
                    ImgCaptcha.Source = bitmap;
                    TxtCaptchaTip.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TxtCaptchaTip.Text = "点击重试";
                    TxtCaptchaTip.Visibility = Visibility.Visible;
                }
            }
            else
            {
                _captchaId = "";
                ImgCaptcha.Source = null;
                TxtCaptchaTip.Text = "获取失败，点击重试";
                TxtCaptchaTip.Visibility = Visibility.Visible;
            }

            TxtImageCode.Text = "";
        }

        /// <summary>
        /// 获取短信验证码
        /// </summary>
        private async void BtnGetVerifyCode_Click(object sender, RoutedEventArgs e)
        {
            string phone = TxtPhone.Text.Trim();
            string imgCode = TxtImageCode.Text.Trim();

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

            if (string.IsNullOrEmpty(imgCode))
            {
                await ShowToastAsync("请输入图形验证码");
                TxtImageCode.Focus(FocusState.Programmatic);
                return;
            }

            if (string.IsNullOrEmpty(_captchaId))
            {
                await ShowToastAsync("正在加载图形验证码，请稍候...");
                await RefreshCaptchaAsync();
                return;
            }

            SetLoading(true, "正在发送短信验证码...");
            var res = await YunhuApiClient.GetSmsVerificationCodeAsync(phone, imgCode, _captchaId);
            SetLoading(false);

            if (res.IsSuccess)
            {
                // 启动 60s 倒计时
                BtnGetVerifyCode.IsEnabled = false;
                _countDownSeconds = 60;
                BtnGetVerifyCode.Content = string.Format("{0}s", _countDownSeconds);
                _smsCountDownTimer.Start();

                await ShowToastAsync(string.IsNullOrEmpty(res.Msg) || res.Msg == "success" ? "短信验证码已发送" : res.Msg);
                TxtVerifyCode.Focus(FocusState.Programmatic);
            }
            else
            {
                string errorMsg = !string.IsNullOrEmpty(res.Msg) ? res.Msg : "验证码发送失败";
                await ShowToastAsync(errorMsg);

                // 发送失败或图形验证码失效，自动刷新图形验证码
                await RefreshCaptchaAsync();
            }
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

        /// <summary>
        /// 手机号短信验证码登录
        /// </summary>
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
                Frame.Navigate(typeof(MainPage), phone);
            }
            else
            {
                string errorMsg = !string.IsNullOrEmpty(res.Msg) ? res.Msg : "登录失败，请检查验证码";
                await ShowToastAsync(errorMsg);
            }
        }

        /// <summary>
        /// 邮箱密码登录
        /// </summary>
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
                Frame.Navigate(typeof(MainPage), email);
            }
            else
            {
                string errorMsg = !string.IsNullOrEmpty(res.Msg) ? res.Msg : "登录失败，请检查邮箱与密码";
                await ShowToastAsync(errorMsg);
            }
        }

        /// <summary>
        /// 设置加载遮罩状态
        /// </summary>
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
    }
}
