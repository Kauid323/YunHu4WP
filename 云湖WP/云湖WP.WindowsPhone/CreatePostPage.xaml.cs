using System;
using System.Threading.Tasks;
using Windows.Phone.UI.Input;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Community.CreatePost;
using 云湖WP.Api.Community.EditPost;
using 云湖WP.Api.Community.PostDraft;
using 云湖WP.Token;

namespace 云湖WP
{
    /// <summary>
    /// 发布动态页面 (支持 Markdown/文本切换、大尺寸多行无限输入、草稿箱保存与退出确认)
    /// </summary>
    public sealed partial class CreatePostPage : Page
    {
        private string _token = "";
        private int _baId = 0;
        private string _boardName = "";
        private int _draftId = 0;
        private long _postId = 0; // 大于 0 时为编辑模式
        private int _contentType = 2; // 默认 2-Markdown, 1-纯文本
        private bool _isSubmitting = false;
        private bool _hasSavedOrPublished = false;

        private bool _isExitDialogShowing = false;

        public CreatePostPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            var args = e.Parameter as CreatePostNavArgs;
            if (args != null)
            {
                _baId = args.BaId;
                _boardName = args.BoardName;
                _draftId = args.DraftId;
                _postId = args.PostId; // 编辑模式
                if (args.InitialContentType > 0)
                {
                    _contentType = args.InitialContentType;
                }
                if (!string.IsNullOrEmpty(args.InitialTitle))
                {
                    TxtTitle.Text = args.InitialTitle;
                }
                if (!string.IsNullOrEmpty(args.InitialContent))
                {
                    TxtContent.Text = args.InitialContent;
                }
            }
            else if (e.Parameter is int)
            {
                _baId = (int)e.Parameter;
            }

            if (string.IsNullOrEmpty(_token))
            {
                _token = await TokenManager.GetTokenAsync();
            }

            if (_postId > 0)
            {
                if (TxtPageHeader != null) TxtPageHeader.Text = "编辑动态";
                if (AppBarBtnDraft != null) AppBarBtnDraft.Visibility = Visibility.Collapsed;
            }

            UpdateContentTypeUI();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;

            if (this.BottomAppBar != null)
            {
                this.BottomAppBar.IsOpen = false;
            }
        }

        private async void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            e.Handled = true;
            await HandlePageExitAsync();
        }

        /// <summary>
        /// 处理退出确认提示 (MessageDialog 弹窗)
        /// </summary>
        private async Task HandlePageExitAsync()
        {
            if (_isSubmitting || _isExitDialogShowing) return;

            string title = TxtTitle != null ? TxtTitle.Text.Trim() : "";
            string content = TxtContent != null ? TxtContent.Text.Trim() : "";

            // 如果内容为空或已经成功提交/保存过，直接退出
            if (_hasSavedOrPublished || (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(content)))
            {
                if (Frame.CanGoBack) Frame.GoBack();
                return;
            }

            _isExitDialogShowing = true;
            IUICommand chosen = null;
            try
            {
                var dialog = new MessageDialog("是否保存当前内容到草稿箱后再退出？\n（按手机返回键可取消并留在此页）", "退出发动态");

                var saveCmd = new UICommand("保存到草稿箱");
                var exitCmd = new UICommand("确定退出");

                dialog.Commands.Add(saveCmd);
                dialog.Commands.Add(exitCmd);

                // 注意：不要将 CancelCommandIndex 设置为 exitCmd，避免息屏/取消时自动触发退出
                dialog.DefaultCommandIndex = 0;

                chosen = await dialog.ShowAsync();
            }
            catch
            {
                // 息屏、中断或取消异常时留在此页，绝不自动退出
                chosen = null;
            }
            finally
            {
                _isExitDialogShowing = false;
            }

            if (chosen != null)
            {
                if (chosen.Label == "保存到草稿箱")
                {
                    bool saved = await SaveDraftInternalAsync(showSuccessToast: true);
                    if (saved)
                    {
                        if (Frame.CanGoBack) Frame.GoBack();
                    }
                }
                else if (chosen.Label == "确定退出")
                {
                    if (Frame.CanGoBack) Frame.GoBack();
                }
            }
        }

        private void UpdateContentTypeUI()
        {
            bool isMd = (_contentType == 2);
            if (AppBarBtnType != null)
            {
                AppBarBtnType.Label = isMd ? "Markdown" : "纯文本";
                AppBarBtnType.Icon = isMd ? new SymbolIcon(Symbol.Document) : new SymbolIcon(Symbol.Font);
            }
        }

        private void AppBarBtnType_Click(object sender, RoutedEventArgs e)
        {
            ToggleContentType();
        }

        private void ToggleContentType()
        {
            _contentType = (_contentType == 2) ? 1 : 2;
            UpdateContentTypeUI();
        }

        #region 发送动态与存草稿

        private async void AppBarBtnSend_Click(object sender, RoutedEventArgs e)
        {
            await SubmitPostAsync();
        }

        private async Task SubmitPostAsync()
        {
            if (_isSubmitting) return;

            string title = TxtTitle != null ? TxtTitle.Text.Trim() : "";
            string content = TxtContent != null ? TxtContent.Text.Trim() : "";

            if (string.IsNullOrEmpty(title))
            {
                await ShowToastAsync("请输入动态标题");
                if (TxtTitle != null) TxtTitle.Focus(FocusState.Programmatic);
                return;
            }

            if (string.IsNullOrEmpty(content))
            {
                await ShowToastAsync("请输入动态正文内容");
                if (TxtContent != null) TxtContent.Focus(FocusState.Programmatic);
                return;
            }

            if (string.IsNullOrEmpty(_token))
            {
                _token = await TokenManager.GetTokenAsync();
            }

            _isSubmitting = true;
            if (ActionProgressBar != null) ActionProgressBar.Visibility = Visibility.Visible;
            if (AppBarBtnSend != null) AppBarBtnSend.IsEnabled = false;

            string errMsg = null;
            try
            {
                bool isEdit = _postId > 0;
                if (isEdit)
                {
                    // 编辑模式
                    var res = await EditPostApi.EditPostAsync(
                        token: _token,
                        postId: _postId,
                        title: title,
                        content: content,
                        contentType: _contentType
                    );
                    if (res != null && res.IsSuccess)
                    {
                        _hasSavedOrPublished = true;
                        await ShowToastAsync("动态更新成功！");
                        if (Frame.CanGoBack) Frame.GoBack();
                    }
                    else
                    {
                        string err = (res != null && !string.IsNullOrEmpty(res.Msg)) ? res.Msg : "更新失败，请重试";
                        errMsg = "更新失败: " + err;
                    }
                }
                else
                {
                    // 新建模式
                    var res = await CreatePostApi.CreatePostAsync(
                        token: _token,
                        baId: _baId,
                        title: title,
                        content: content,
                        contentType: _contentType,
                        groupId: "",
                        draftId: _draftId
                    );
                    if (res != null && res.IsSuccess)
                    {
                        _hasSavedOrPublished = true;
                        await ShowToastAsync("动态发布成功！");
                        if (Frame.CanGoBack) Frame.GoBack();
                    }
                    else
                    {
                        string err = (res != null && !string.IsNullOrEmpty(res.Msg)) ? res.Msg : "发布失败，请重试";
                        errMsg = "发布失败: " + err;
                    }
                }
            }
            catch (Exception ex)
            {
                errMsg = "操作异常: " + ex.Message;
            }
            finally
            {
                _isSubmitting = false;
                if (ActionProgressBar != null) ActionProgressBar.Visibility = Visibility.Collapsed;
                if (AppBarBtnSend != null) AppBarBtnSend.IsEnabled = true;
            }

            if (errMsg != null)
            {
                await ShowToastAsync(errMsg);
            }
        }

        private async void AppBarBtnDraft_Click(object sender, RoutedEventArgs e)
        {
            await SaveDraftInternalAsync(showSuccessToast: true);
        }

        private async Task<bool> SaveDraftInternalAsync(bool showSuccessToast)
        {
            if (_isSubmitting) return false;

            string title = TxtTitle != null ? TxtTitle.Text.Trim() : "";
            string content = TxtContent != null ? TxtContent.Text.Trim() : "";

            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(content))
            {
                if (showSuccessToast) await ShowToastAsync("动态标题或正文不能为空");
                return false;
            }

            if (string.IsNullOrEmpty(_token))
            {
                _token = await TokenManager.GetTokenAsync();
            }

            _isSubmitting = true;
            if (ActionProgressBar != null) ActionProgressBar.Visibility = Visibility.Visible;

            bool success = false;
            string toastMsg = null;
            try
            {
                var res = await PostDraftApi.SaveDraftAsync(
                    token: _token,
                    baId: _baId,
                    title: title,
                    content: content,
                    contentType: _contentType,
                    draftId: _draftId
                );

                if (res != null && res.IsSuccess)
                {
                    if (res.DraftId > 0)
                    {
                        _draftId = res.DraftId;
                    }
                    _hasSavedOrPublished = true;
                    success = true;
                    if (showSuccessToast)
                    {
                        toastMsg = "已成功保存到草稿箱！";
                    }
                }
                else
                {
                    string err = (res != null && !string.IsNullOrEmpty(res.Msg)) ? res.Msg : "保存草稿失败";
                    if (showSuccessToast) toastMsg = "保存草稿提示: " + err;
                }
            }
            catch (Exception ex)
            {
                if (showSuccessToast) toastMsg = "保存草稿异常: " + ex.Message;
            }
            finally
            {
                _isSubmitting = false;
                if (ActionProgressBar != null) ActionProgressBar.Visibility = Visibility.Collapsed;
            }

            if (toastMsg != null)
            {
                await ShowToastAsync(toastMsg);
            }

            return success;
        }

        private void AppBarBtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (TxtTitle != null) TxtTitle.Text = "";
            if (TxtContent != null) TxtContent.Text = "";
        }

        #endregion

        private async Task ShowToastAsync(string message)
        {
            try
            {
                var dialog = new MessageDialog(message, "云湖动态");
                await dialog.ShowAsync();
            }
            catch { }
        }
    }
}
