using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Imaging;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Group
{
    public class GroupBotModel : INotifyPropertyChanged
    {
        public string BotId { get; set; }
        public string Name { get; set; }
        public string Introduction { get; set; }
        public string AvatarUrl { get; set; }
        private BitmapImage _avatarBitmap;
        public BitmapImage AvatarBitmap
        {
            get { return _avatarBitmap; }
            set
            {
                if (_avatarBitmap == value) return;
                _avatarBitmap = value;
                var handler = PropertyChanged;
                if (handler != null) handler(this, new PropertyChangedEventArgs("AvatarBitmap"));
            }
        }
        public string Initial { get { return string.IsNullOrEmpty(Name) ? "B" : Name.Substring(0, 1); } }
        public string Subtitle { get { return string.IsNullOrEmpty(Introduction) ? BotId : Introduction; } }
        public override string ToString() { return string.IsNullOrEmpty(Introduction) ? Name : Name + "\n" + Introduction; }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public static class GroupBotApi
    {
        public static async Task<List<GroupBotModel>> ListAsync(string token, string groupId)
        {
            var result = new List<GroupBotModel>();
            var bytes = await HttpHelper.PostProtobufAsync("/v1/group/bot-list", Yh.BotListRequest.SerializeToBytes(new Yh.BotListRequest { GroupId = groupId }), token);
            var response = Yh.BotListResponse.Deserialize(bytes);
            if (response.Bot != null) foreach (var bot in response.Bot) result.Add(new GroupBotModel { BotId = bot.BotId, Name = bot.Name ?? bot.BotId, Introduction = bot.Introduction, AvatarUrl = bot.AvatarUrl });
            return result;
        }
        public static Task<ApiResult> RemoveAsync(string token, string groupId, string botId) { return GroupApi.RemoveBotAsync(token, groupId, botId); }
    }
}
