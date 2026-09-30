using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.Phone.UI.Input;
using 云湖WP.Api.Group;
using 云湖WP.Token;

namespace 云湖WP
{
    public sealed partial class MessageTypeLimitPage : Page
    {
        private string _token;
        private string _groupId;
        public MessageTypeLimitPage() { InitializeComponent(); }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;
            var args = e.Parameter as GroupDetailNavigationArgs;
            _groupId = args == null ? e.Parameter as string : args.GroupId;
            _token = args == null ? await TokenManager.GetTokenAsync() : (args.Token ?? await TokenManager.GetTokenAsync());
            string error = null;
            try
            {
                var result = await GroupApi.GetInfoAsync(_token, _groupId);
                if (result.IsSuccess && result.Group != null) SetChecks(result.Group.LimitedMsgType);
                else error = result.Msg ?? "获取消息类型限制失败";
            }
            catch (Exception ex) { error = ex.Message; }
            if (!string.IsNullOrEmpty(error)) await Alert(error);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
            base.OnNavigatedFrom(e);
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            if (Frame != null && Frame.CanGoBack)
            {
                e.Handled = true;
                Frame.GoBack();
            }
        }

        private void SetChecks(string value)
        {
            var selected = new HashSet<string>((value ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            var checks = new[] { MsgTypeTextCheck, MsgTypeImageCheck, MsgTypeMarkdownCheck, MsgTypeFileCheck, MsgTypeFormCheck, MsgTypeStickerCheck, MsgTypeHtmlCheck, MsgTypeTipCheck, MsgTypeAudioCheck, MsgTypeCallCheck, MsgTypeA2uiCheck };
            foreach (var check in checks) check.IsChecked = selected.Contains(check.Tag.ToString());
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var values = new List<string>();
            var checks = new[] { MsgTypeTextCheck, MsgTypeImageCheck, MsgTypeMarkdownCheck, MsgTypeFileCheck, MsgTypeFormCheck, MsgTypeStickerCheck, MsgTypeHtmlCheck, MsgTypeTipCheck, MsgTypeAudioCheck, MsgTypeCallCheck, MsgTypeA2uiCheck };
            foreach (var check in checks) if (check.IsChecked == true) values.Add(check.Tag.ToString());
            var result = await GroupApi.SetMessageTypeLimitAsync(_token, _groupId, string.Join(",", values));
            if (!result.IsSuccess) await Alert(result.Msg ?? "保存失败");
            else if (Frame != null && Frame.CanGoBack) Frame.GoBack();
        }

        private async Task Alert(string text) { await new MessageDialog(text ?? "操作失败", "云湖").ShowAsync(); }
    }
}
