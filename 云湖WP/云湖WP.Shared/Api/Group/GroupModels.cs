using System.Collections.Generic;
using System.ComponentModel;
using Windows.UI.Xaml.Media.Imaging;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.Group
{
    public class GroupInfoModel
    {
        public string GroupId { get; set; }
        public string Name { get; set; }
        public string AvatarUrl { get; set; }
        public long AvatarId { get; set; }
        public string Introduction { get; set; }
        public long Headcount { get; set; }
        public string Owner { get; set; }
        public long PermissionLevel { get; set; }
        public bool DirectJoin { get; set; }
        public bool HistoryMsg { get; set; }
        public bool Private { get; set; }
        public bool DoNotDisturb { get; set; }
        public bool Top { get; set; }
        public bool Recommendation { get; set; }
        public string LimitedMsgType { get; set; }
        public bool HideGroupMembers { get; set; }
        public long AutoDeleteMessage { get; set; }
        public bool DenyMembersUploadToGroupDisk { get; set; }
        public string MyGroupNickname { get; set; }
        public string GroupCode { get; set; }
        public string CategoryName { get; set; }
        public long CategoryId { get; set; }
        public long CommunityId { get; set; }
        public string CommunityName { get; set; }
        public long UnbanTimestamp { get; set; }
        public string BanReason { get; set; }
        public List<string> AdminIds { get; set; }

        public GroupInfoModel() { AdminIds = new List<string>(); }
        public string PermissionText { get { return PermissionLevel >= 100 ? "群主" : (PermissionLevel >= 2 ? "管理员" : "群成员"); } }
    }

    public class GroupMemberModel : INotifyPropertyChanged
    {
        public string UserId { get; set; }
        public string Name { get; set; }
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
        public long PermissionLevel { get; set; }
        public long GagTimestamp { get; set; }
        public bool IsGag { get; set; }
        public string Initial { get { return string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1); } }
        public string RoleText { get { return PermissionLevel >= 100 ? "群主" : (PermissionLevel >= 2 ? "管理员" : ""); } }
        public string Subtitle { get { return string.IsNullOrEmpty(RoleText) ? UserId : RoleText + " · " + UserId; } }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class GroupCategoryItem
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public override string ToString() { return DisplayName ?? Name ?? ""; }
    }

    public class GroupInfoResult : ApiResult { public GroupInfoModel Group { get; set; } }
    public class GroupMembersResult : ApiResult
    {
        public List<GroupMemberModel> Members { get; set; }
        public long Total { get; set; }
        public GroupMembersResult() { Members = new List<GroupMemberModel>(); }
    }
}

