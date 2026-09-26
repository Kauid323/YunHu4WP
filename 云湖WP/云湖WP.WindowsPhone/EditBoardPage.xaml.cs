using System;
using System.Threading.Tasks;
using Windows.Phone.UI.Input;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Community.EditBoard;
using 云湖WP.Token;

namespace 云湖WP
{
    /// <summary>
    /// 编辑板块页面 (POST /v1/community/ba/edit)
    /// </summary>
    public sealed partial class EditBoardPage : Page
    {
        private string _token = "";
        private int _baId = 0;
        private string _originalAvatar = "";
        private bool _isSaving = false;

        public EditBoardPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            _token = await TokenManager.GetTokenAsync();

            var args = e.Parameter as EditBoardNavArgs;
            if (args != null)
            {
                _baId = args.BaId;
                _originalAvatar = args.CurrentAvatar ?? "";
                if (TxtBoardName != null) TxtBoardName.Text = args.CurrentName ?? "";
                if (TxtAvatarHint != null && !string.IsNullOrEmpty(_originalAvatar))
                {
                    TxtAvatarHint.Text = "当前头像: " + _originalAvatar;
                }
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            e.Handled = true;
            if (Frame.CanGoBack) Frame.GoBack();
        }

        private async void AppBarBtnSave_Click(object sender, RoutedEventArgs e)
        {
            await SaveBoardAsync();
        }

        private async Task SaveBoardAsync()
        {
            if (_isSaving) return;

            string name = TxtBoardName != null ? TxtBoardName.Text.Trim() : "";
            string avatarUrl = TxtAvatarUrl != null ? TxtAvatarUrl.Text.Trim() : "";

            if (string.IsNullOrEmpty(name))
            {
                await ShowToastAsync("板块名称不能为空");
                return;
            }

            // 若未填头像 URL，保持原头像
            if (string.IsNullOrEmpty(avatarUrl))
            {
                avatarUrl = _originalAvatar;
            }

            _isSaving = true;
            if (ActionProgressBar != null) ActionProgressBar.Visibility = Visibility.Visible;
            if (AppBarBtnSave != null) AppBarBtnSave.IsEnabled = false;

            string errMsg = null;
            bool saveSuccess = false;
            try
            {
                var result = await EditBoardApi.EditBoardAsync(_token, _baId, name, avatarUrl);
                if (result != null && result.IsSuccess)
                {
                    saveSuccess = true;
                }
                else
                {
                    string err = (result != null && !string.IsNullOrEmpty(result.Msg)) ? result.Msg : "保存失败";
                    errMsg = "保存失败: " + err;
                }
            }
            catch (Exception ex)
            {
                errMsg = "保存异常: " + ex.Message;
            }
            finally
            {
                _isSaving = false;
                if (ActionProgressBar != null) ActionProgressBar.Visibility = Visibility.Collapsed;
                if (AppBarBtnSave != null) AppBarBtnSave.IsEnabled = true;
            }

            if (saveSuccess)
            {
                await ShowToastAsync("板块信息已更新");
                if (Frame.CanGoBack) Frame.GoBack();
            }
            else if (errMsg != null)
            {
                await ShowToastAsync(errMsg);
            }
        }

        private async Task ShowToastAsync(string msg)
        {
            try { await new MessageDialog(msg, "云湖板块").ShowAsync(); } catch { }
        }
    }
}
