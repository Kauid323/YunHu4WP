using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.Data.Json;
using Windows.UI.Popups;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Phone.UI.Input;
using 云湖WP.Api.Common;
using 云湖WP.Api.Group;
using 云湖WP.Token;
using 云湖WP.Utils;

namespace 云湖WP
{
    public sealed partial class GroupDetailPage : Page, IFileOpenPickerContinuable
    {
        private string _token;
        private string _groupId;
        private GroupInfoModel _group;
        private bool _updating;
        private bool _loadingMembers;
        private bool _hasMoreMembers = true;
        private int _memberPage = 1;
        private string _memberKeyword = "";
        private readonly ObservableCollection<GroupMemberModel> _members = new ObservableCollection<GroupMemberModel>();
        private bool _loadingBots;
        private bool _botsLoaded;

        private ScrollViewer _membersScrollViewer;

        public GroupDetailPage()
        {
            InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            MembersList.ItemsSource = _members;
            MembersList.Loaded += MembersList_Loaded;
            MembersList.Unloaded += MembersList_Unloaded;
        }

        private void MembersList_Loaded(object sender, RoutedEventArgs e)
        {
            if (_membersScrollViewer == null)
            {
                _membersScrollViewer = FindScrollViewer(MembersList);
            }
            if (_membersScrollViewer != null)
            {
                _membersScrollViewer.ViewChanged -= MembersScroll_ViewChanged;
                _membersScrollViewer.ViewChanged += MembersScroll_ViewChanged;
            }
        }

        private void MembersList_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_membersScrollViewer != null)
            {
                _membersScrollViewer.ViewChanged -= MembersScroll_ViewChanged;
            }
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            for (var i = 0; i < Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
                var scroll = child as ScrollViewer;
                if (scroll != null) return scroll;
                var nested = FindScrollViewer(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private async void MembersScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (e != null && e.IsIntermediate) return;
            var scroll = sender as ScrollViewer;
            if (scroll == null || _loadingMembers || !_hasMoreMembers) return;
            if (scroll.ScrollableHeight > 0 && scroll.VerticalOffset >= scroll.ScrollableHeight - 80)
            {
                await LoadMembersAsync(false);
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            var args = e.Parameter as GroupDetailNavigationArgs;
            string newGroupId = args == null ? e.Parameter as string : args.GroupId;
            string newToken = args == null ? await TokenManager.GetTokenAsync() : (args.Token ?? await TokenManager.GetTokenAsync());

            bool isDifferentGroup = !string.IsNullOrEmpty(newGroupId) && newGroupId != _groupId;
            _groupId = string.IsNullOrEmpty(newGroupId) ? _groupId : newGroupId;
            _token = string.IsNullOrEmpty(newToken) ? _token : newToken;

            // 如果是从子页面返回 (Back) 且已经加载过当前群信息，直接保留页面与控件状态，避免重复全量网络请求
            if (e.NavigationMode == NavigationMode.Back && _group != null && !isDifferentGroup)
            {
                AppLogger.Log("GroupDetailPage", string.Format("返回群聊详情页 (保留缓存状态): GroupId={0}", _groupId));
                return;
            }

            AppLogger.Log("GroupDetailPage", string.Format("进入群聊详情页加载数据: GroupId={0}, NavMode={1}", _groupId, e.NavigationMode));
            await LoadInfoAsync();
            await LoadMembersAsync(true);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
            base.OnNavigatedFrom(e);
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            e.Handled = true;
            NavigateBack();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            NavigateBack();
        }

        private void NavigateBack()
        {
            if (Frame != null && Frame.CanGoBack) Frame.GoBack();
        }

        private async Task LoadInfoAsync()
        {
            if (string.IsNullOrEmpty(_groupId)) return;
            Progress.Visibility = Visibility.Visible;
            try
            {
                var result = await GroupApi.GetInfoAsync(_token, _groupId);
                if (!result.IsSuccess || result.Group == null)
                {
                    string err = result.Msg ?? "获取群聊信息失败";
                    AppLogger.Log("GroupDetailPage", string.Format("LoadInfoAsync failed: GroupId={0}, Code={1}, Msg={2}", _groupId, result.Code, err));
                    await Alert(err);
                    return;
                }
                _group = result.Group;
                NameText.Text = _group.Name ?? _groupId;
                GroupPivot.Title = _group.Name ?? "群聊详情";
                IntroText.Text = string.IsNullOrEmpty(_group.Introduction) ? "暂无群聊简介" : _group.Introduction;
                InfoText.Text = string.Format("{0} 人 · {1}", _group.Headcount, _group.PermissionText);
                CodeText.Text = string.IsNullOrEmpty(_group.GroupCode) ? "未设置" : _group.GroupCode;
                CategoryText.Text = string.IsNullOrEmpty(_group.CategoryName) ? "未分类" : _group.CategoryName;
                _updating = true;
                NicknameBox.Text = _group.MyGroupNickname ?? "";
                NicknameBox.PlaceholderText = string.IsNullOrEmpty(_group.MyGroupNickname) ? "输入在群内显示的昵称 (留空为默认)" : ("当前昵称: " + _group.MyGroupNickname);
                CategoryBox.Text = _group.CategoryName ?? "";
                CategoryBox.PlaceholderText = string.IsNullOrEmpty(_group.CategoryName) ? "点击选择群分类" : ("当前分类: " + _group.CategoryName);
                CategoryText.Text = string.IsNullOrEmpty(_group.CategoryName) ? "未设置分类 (点击选择)" : ("已选分类: " + _group.CategoryName);
                HistorySwitch.IsOn = _group.HistoryMsg;
                MuteSwitch.IsOn = _group.DoNotDisturb;
                TopSwitch.IsOn = _group.Top;
                PrivateSwitch.IsOn = _group.Private;
                RecommendationSwitch.IsOn = _group.Recommendation;
                HideMembersSwitch.IsOn = _group.HideGroupMembers;
                UploadSwitch.IsOn = _group.DenyMembersUploadToGroupDisk;
                _updating = false;
                AppLogger.Log("GroupDetailPage", string.Format("LoadInfoAsync success: Name={0}, Headcount={1}", _group.Name, _group.Headcount));
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDetailPage", "LoadInfoAsync exception: " + ex.ToString());
            }
            finally
            {
                Progress.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadMembersAsync(bool reset)
        {
            if (_loadingMembers) return;
            if (!reset && !_hasMoreMembers) return;
            _loadingMembers = true;
            try
            {
                var page = reset ? 1 : _memberPage + 1;
                var result = await GroupApi.GetMembersAsync(_token, _groupId, page, 50, _memberKeyword);
                if (!result.IsSuccess)
                {
                    string err = result.Msg ?? "获取群成员失败";
                    AppLogger.Log("GroupDetailPage", string.Format("LoadMembersAsync failed: GroupId={0}, Page={1}, Msg={2}", _groupId, page, err));
                    return;
                }

                if (reset)
                {
                    _members.Clear();
                    _memberPage = 1;
                }
                else
                {
                    _memberPage = page;
                }

                if (result.Members != null)
                {
                    foreach (var member in result.Members)
                    {
                        if (member != null) _members.Add(member);
                    }
                    foreach (var member in result.Members)
                    {
                        if (member != null) LoadMemberAvatarAsync(member);
                    }
                }

                _hasMoreMembers = result.Total > 0 ? _members.Count < result.Total : (result.Members != null && result.Members.Count >= 50);
                AppLogger.Log("GroupDetailPage", string.Format("LoadMembersAsync loaded: Count={0}, Total={1}, HasMore={2}", _members.Count, result.Total, _hasMoreMembers));
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDetailPage", "LoadMembersAsync exception: " + ex.ToString());
            }
            finally
            {
                _loadingMembers = false;
            }
        }

        private async void MemberSearch_TextChanged(object sender, TextChangedEventArgs e) { _memberKeyword = MemberSearchBox.Text ?? ""; await LoadMembersAsync(true); }
        private async void Member_Click(object sender, ItemClickEventArgs e)
        {
            var member = e.ClickedItem as GroupMemberModel;
            if (member == null || member.PermissionLevel >= 100) return;

            var panel = new StackPanel();
            var adminButton = new Button { Content = member.PermissionLevel >= 2 ? "取消管理员" : "设为管理员", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
            var gagButton = new Button { Content = member.IsGag ? "解除禁言" : "禁言 10 分钟", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
            var removeButton = new Button { Content = "移出群聊", HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.Children.Add(adminButton);
            panel.Children.Add(gagButton);
            panel.Children.Add(removeButton);

            var dialog = new ContentDialog { Title = member.Name, Content = panel, SecondaryButtonText = "取消" };
            adminButton.Click += async (s, args) =>
            {
                string errMsg = null;
                try
                {
                    dialog.Hide();
                    Progress.Visibility = Visibility.Visible;
                    bool targetAdmin = member.PermissionLevel < 2;
                    AppLogger.Log("GroupDetailPage", string.Format("设置管理员: Member={0}({1}), TargetAdmin={2}", member.Name, member.UserId, targetAdmin));
                    var result = await GroupApi.SetAdminAsync(_token, _groupId, member.UserId, targetAdmin);
                    Progress.Visibility = Visibility.Collapsed;
                    if (!result.IsSuccess)
                    {
                        errMsg = result.Msg ?? "管理员设置失败";
                        AppLogger.Log("GroupDetailPage", "SetAdminAsync 失败: " + errMsg);
                    }
                    else
                    {
                        AppLogger.Log("GroupDetailPage", "SetAdminAsync 成功: " + result.Msg);
                        await LoadMembersAsync(true);
                    }
                }
                catch (Exception ex)
                {
                    Progress.Visibility = Visibility.Collapsed;
                    AppLogger.Log("GroupDetailPage", "SetAdmin failed: " + ex.ToString());
                    errMsg = "管理员设置失败: " + ex.Message;
                }
                if (!string.IsNullOrEmpty(errMsg)) await Alert(errMsg);
            };
            gagButton.Click += async (s, args) =>
            {
                string errMsg = null;
                try
                {
                    dialog.Hide();
                    Progress.Visibility = Visibility.Visible;
                    var result = await GroupApi.GagMemberAsync(_token, _groupId, member.UserId, member.IsGag ? 0 : 600);
                    Progress.Visibility = Visibility.Collapsed;
                    if (!result.IsSuccess) errMsg = result.Msg ?? "禁言设置失败";
                    else await LoadMembersAsync(true);
                }
                catch (Exception ex)
                {
                    Progress.Visibility = Visibility.Collapsed;
                    AppLogger.Log("GroupDetailPage", "GagMember failed: " + ex.ToString());
                    errMsg = "禁言设置失败: " + ex.Message;
                }
                if (!string.IsNullOrEmpty(errMsg)) await Alert(errMsg);
            };
            removeButton.Click += async (s, args) =>
            {
                string errMsg = null;
                try
                {
                    dialog.Hide();
                    Progress.Visibility = Visibility.Visible;
                    var result = await GroupApi.RemoveMemberAsync(_token, _groupId, member.UserId);
                    Progress.Visibility = Visibility.Collapsed;
                    if (!result.IsSuccess) errMsg = result.Msg ?? "移出群聊失败";
                    else await LoadMembersAsync(true);
                }
                catch (Exception ex)
                {
                    Progress.Visibility = Visibility.Collapsed;
                    AppLogger.Log("GroupDetailPage", "RemoveMember failed: " + ex.ToString());
                    errMsg = "移出群聊失败: " + ex.Message;
                }
                if (!string.IsNullOrEmpty(errMsg)) await Alert(errMsg);
            };

            await dialog.ShowAsync();
        }

        private async Task<bool> UpdateGroupAsync(Action<Yh.EditGroupRequest> updateAction)
        {
            if (_group == null || string.IsNullOrEmpty(_groupId)) return false;
            string errMsg = null;
            try
            {
                var req = new Yh.EditGroupRequest
                {
                    GroupId = _groupId,
                    Name = _group.Name ?? "",
                    Introduction = _group.Introduction ?? "",
                    AvatarUrl = _group.AvatarUrl ?? "",
                    DirectJoin = _group.DirectJoin,
                    HistoryMsg = _group.HistoryMsg,
                    CategoryName = _group.CategoryName ?? "",
                    CategoryId = (ulong)_group.CategoryId,
                    Private = _group.Private,
                    HideGroupMembers = _group.HideGroupMembers
                };

                if (updateAction != null) updateAction(req);

                Progress.Visibility = Visibility.Visible;
                var result = await GroupApi.EditAsync(_token, req);
                Progress.Visibility = Visibility.Collapsed;

                if (!result.IsSuccess)
                {
                    errMsg = result.Msg ?? "群信息修改失败";
                    AppLogger.Log("GroupDetailPage", "UpdateGroupAsync 失败: " + errMsg);
                }
                else
                {
                    _group.Name = req.Name;
                    _group.Introduction = req.Introduction;
                    _group.AvatarUrl = req.AvatarUrl;
                    _group.DirectJoin = req.DirectJoin;
                    _group.HistoryMsg = req.HistoryMsg;
                    _group.CategoryName = req.CategoryName;
                    _group.CategoryId = (long)req.CategoryId;
                    _group.Private = req.Private;
                    _group.HideGroupMembers = req.HideGroupMembers;

                    _updating = true;
                    NameText.Text = _group.Name ?? _groupId;
                    GroupPivot.Title = _group.Name ?? "群聊详情";
                    IntroText.Text = string.IsNullOrEmpty(_group.Introduction) ? "暂无群聊简介" : _group.Introduction;
                    CategoryBox.Text = _group.CategoryName ?? "";
                    CategoryBox.PlaceholderText = string.IsNullOrEmpty(_group.CategoryName) ? "点击选择群分类" : ("当前分类: " + _group.CategoryName);
                    CategoryText.Text = string.IsNullOrEmpty(_group.CategoryName) ? "未设置分类 (点击选择)" : ("已选分类: " + _group.CategoryName);
                    HistorySwitch.IsOn = _group.HistoryMsg;
                    PrivateSwitch.IsOn = _group.Private;
                    HideMembersSwitch.IsOn = _group.HideGroupMembers;
                    _updating = false;

                    AppLogger.Log("GroupDetailPage", string.Format("UpdateGroupAsync 成功: Name={0}, Category={1}({2})", _group.Name, _group.CategoryName, _group.CategoryId));
                    return true;
                }
            }
            catch (Exception ex)
            {
                Progress.Visibility = Visibility.Collapsed;
                AppLogger.Log("GroupDetailPage", "UpdateGroupAsync exception: " + ex.ToString());
                errMsg = "修改失败: " + ex.Message;
            }
            if (!string.IsNullOrEmpty(errMsg)) await Alert(errMsg);
            return false;
        }

        private async void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (_group == null) return;
            var name = new TextBox
            {
                Text = _group.Name ?? "",
                PlaceholderText = string.IsNullOrEmpty(_group.Name) ? "请输入群名称" : ("原群名称: " + _group.Name),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var intro = new TextBox
            {
                Text = _group.Introduction ?? "",
                PlaceholderText = string.IsNullOrEmpty(_group.Introduction) ? "请输入群简介" : ("原群简介: " + _group.Introduction),
                AcceptsReturn = true,
                Height = 90
            };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "群聊名称", FontSize = 12, Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneMidBrush"], Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(name);
            panel.Children.Add(new TextBlock { Text = "群聊简介", FontSize = 12, Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneMidBrush"], Margin = new Thickness(0, 6, 0, 4) });
            panel.Children.Add(intro);

            var dialog = new ContentDialog { Title = "编辑群聊信息", Content = panel, PrimaryButtonText = "保存", SecondaryButtonText = "取消" };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(name.Text))
            {
                await UpdateGroupAsync(req =>
                {
                    req.Name = name.Text.Trim();
                    req.Introduction = intro.Text;
                });
            }
        }

        private async void CategoryBox_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            await ShowCategoryPickerAsync();
        }

        private async void SelectCategory_Click(object sender, RoutedEventArgs e)
        {
            await ShowCategoryPickerAsync();
        }

        private async Task ShowCategoryPickerAsync()
        {
            try
            {
                Progress.Visibility = Visibility.Visible;
                var categories = await GroupApi.GetCategoriesListAsync(_token);
                Progress.Visibility = Visibility.Collapsed;

                if (categories == null || categories.Count == 0)
                {
                    await Alert("未能获取到群分类列表，请检查网络后重试");
                    return;
                }

                var list = new List<GroupCategoryItem>();
                list.Add(new GroupCategoryItem { Id = 0, Name = "", DisplayName = "（无分类）" });
                list.AddRange(categories);

                var listView = new ListView
                {
                    ItemsSource = list,
                    IsItemClickEnabled = true,
                    MaxHeight = 350,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                var dialog = new ContentDialog
                {
                    Title = "选择群分类",
                    Content = listView,
                    SecondaryButtonText = "取消"
                };

                listView.ItemClick += async (s, args) =>
                {
                    var selected = args.ClickedItem as GroupCategoryItem;
                    dialog.Hide();

                    if (selected == null) return;
                    await UpdateGroupAsync(req =>
                    {
                        req.CategoryName = selected.Id == 0 ? "" : (selected.DisplayName ?? selected.Name ?? "");
                        req.CategoryId = (ulong)selected.Id;
                    });
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Progress.Visibility = Visibility.Collapsed;
                AppLogger.Log("GroupDetailPage", "ShowCategoryPickerAsync exception: " + ex.ToString());
            }
        }

        private async void Nickname_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var r = await GroupApi.SetMyNicknameAsync(_token, _groupId, NicknameBox.Text ?? "");
                if (!r.IsSuccess) await Alert(r.Msg);
                else AppLogger.Log("GroupDetailPage", "SetMyNickname success");
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDetailPage", "Nickname_Click exception: " + ex.ToString());
            }
        }

        private async void History_Toggled(object s, RoutedEventArgs e) { if (!_updating) await UpdateGroupAsync(req => req.HistoryMsg = HistorySwitch.IsOn); }
        private async void HideMembers_Toggled(object s, RoutedEventArgs e) { if (!_updating) await UpdateGroupAsync(req => req.HideGroupMembers = HideMembersSwitch.IsOn); }
        private async void Private_Toggled(object s, RoutedEventArgs e) { if (!_updating) await UpdateGroupAsync(req => req.Private = PrivateSwitch.IsOn); }
        private async void Recommendation_Toggled(object s, RoutedEventArgs e) { if (!_updating) { var r = await GroupApi.SwitchRecommendationAsync(_token, _groupId, !RecommendationSwitch.IsOn); if (!r.IsSuccess) await Alert(r.Msg); } }
        private async void Mute_Toggled(object s, RoutedEventArgs e) { if (!_updating) await ConversationSettingAsync("dnd", MuteSwitch.IsOn); }
        private async void Top_Toggled(object s, RoutedEventArgs e) { if (!_updating) await ConversationSettingAsync("top", TopSwitch.IsOn); }
        private async void Upload_Toggled(object s, RoutedEventArgs e) { if (!_updating) { var r = await GroupApi.SetMemberUploadDisabledAsync(_token, _groupId, UploadSwitch.IsOn); if (!r.IsSuccess) await Alert(r.Msg); } }

        private void MsgLimitPage_Click(object s, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MessageTypeLimitPage), new GroupDetailNavigationArgs { GroupId = _groupId, Token = _token });
        }

        private async void LoadMemberAvatarAsync(GroupMemberModel member)
        {
            if (member == null || ImageLoader.DisableAllImages || string.IsNullOrEmpty(member.AvatarUrl)) return;
            try
            {
                member.AvatarBitmap = await ImageLoader.LoadAvatarAsync(member.AvatarUrl, 72, 72);
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDetailPage", "LoadMemberAvatarAsync exception: " + ex.Message);
            }
        }

        private async Task ConversationSettingAsync(string type, bool enabled)
        {
            try
            {
                var r = await GroupApi.SetConversationSettingAsync(_token, _groupId, type, enabled);
                if (!r.IsSuccess) await Alert(r.Msg);
                else AppLogger.Log("GroupDetailPage", string.Format("ConversationSettingAsync success: type={0}, enabled={1}", type, enabled));
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDetailPage", "ConversationSettingAsync exception: " + ex.ToString());
            }
        }

        private async void Pivot_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (GroupPivot.SelectedIndex == 2) await LoadTagsAsync();
            if (GroupPivot.SelectedIndex == 3) await LoadBotsAsync(false);
        }

        private async void TagSearch_TextChanged(object s, TextChangedEventArgs e)
        {
            if (GroupPivot.SelectedIndex == 2) await LoadTagsAsync();
        }

        private async Task LoadTagsAsync()
        {
            try
            {
                var tags = await GroupTagApi.ListAsync(_token, _groupId, TagSearchBox.Text ?? "");
                if (tags.Count == 0) tags.Add("暂无标签");
                TagsList.ItemsSource = tags;
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDetailPage", "LoadTagsAsync exception: " + ex.ToString());
            }
        }

        private async void CreateTag_Click(object s, RoutedEventArgs e)
        {
            var box = new TextBox { PlaceholderText = "标签名称" };
            var d = new ContentDialog { Title = "新建标签", Content = box, PrimaryButtonText = "创建", SecondaryButtonText = "取消" };
            if (await d.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
            {
                try
                {
                    await GroupTagApi.CreateAsync(_token, _groupId, box.Text.Trim());
                    await LoadTagsAsync();
                }
                catch (Exception ex)
                {
                    AppLogger.Log("GroupDetailPage", "CreateTag_Click exception: " + ex.ToString());
                }
            }
        }

        private async Task LoadBotsAsync(bool force)
        {
            if (_loadingBots || (_botsLoaded && !force)) return;
            _loadingBots = true;
            try
            {
                var result = await GroupBotApi.ListAsync(_token, _groupId);
                BotsList.ItemsSource = result;
                _botsLoaded = true;
                foreach (var bot in result) LoadBotAvatarAsync(bot);
                AppLogger.Log("GroupDetailPage", string.Format("LoadBotsAsync success: Count={0}", result != null ? result.Count : 0));
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDetailPage", "LoadBotsAsync exception: " + ex.ToString());
            }
            finally
            {
                _loadingBots = false;
            }
        }

        private async void LoadBotAvatarAsync(GroupBotModel bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.AvatarUrl)) return;
            try
            {
                bot.AvatarBitmap = await ImageLoader.LoadAvatarAsync(bot.AvatarUrl, 72, 72);
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDetailPage", "LoadBotAvatarAsync exception: " + ex.Message);
            }
        }

        private async void BotsRefresh_Click(object s, RoutedEventArgs e) { await LoadBotsAsync(true); }

        private async void Bot_Click(object s, ItemClickEventArgs e)
        {
            var bot = e.ClickedItem as GroupBotModel;
            if (bot == null) return;
            var d = new MessageDialog(bot.Name, "群机器人");
            d.Commands.Add(new UICommand("移出群聊", async x =>
            {
                try
                {
                    await GroupBotApi.RemoveAsync(_token, _groupId, bot.BotId);
                    _botsLoaded = false;
                    await LoadBotsAsync(true);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("GroupDetailPage", "RemoveBot exception: " + ex.ToString());
                }
            }));
            d.Commands.Add(new UICommand("取消"));
            await d.ShowAsync();
        }

        private async void Refresh_Click(object s, RoutedEventArgs e)
        {
            _botsLoaded = false;
            await LoadInfoAsync();
            await LoadMembersAsync(true);
        }

        private void Avatar_Click(object s, RoutedEventArgs e) { PickImage("Avatar"); }
        private void Background_Click(object s, RoutedEventArgs e) { PickImage("Background"); }

        private void PickImage(string action)
        {
            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".gif");
            picker.ContinuationData["Action"] = action;
            picker.PickSingleFileAndContinue();
        }

        public async void ContinueFileOpenPicker(FileOpenPickerContinuationEventArgs args)
        {
            if (args == null || args.Files == null || args.Files.Count == 0) return;
            Progress.Visibility = Visibility.Visible; Progress.IsIndeterminate = false;
            try
            {
                var uploadProgress = new Progress<double>(value => Progress.Value = value);
                var url = await QiniuUploadHelper.UploadImageAsync(args.Files[0], _token, uploadProgress);
                var action = args.ContinuationData.ContainsKey("Action") ? args.ContinuationData["Action"] as string : "";
                if (action == "Avatar")
                {
                    await UpdateGroupAsync(req => req.AvatarUrl = url);
                }
                else
                {
                    await GroupAuxApi.SetChatBackgroundAsync(_token, _groupId, url);
                }
                await LoadInfoAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDetailPage", "ContinueFileOpenPicker exception: " + ex.ToString());
            }
            finally
            {
                Progress.Visibility = Visibility.Collapsed;
                Progress.IsIndeterminate = true;
            }
        }

        private async void Share_Click(object s, RoutedEventArgs e)
        {
            try
            {
                var text = await GroupAuxApi.CreateShareAsync(_token, _groupId, _group.Name);
                await Alert(ExtractShareUrl(text));
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDetailPage", "Share_Click exception: " + ex.ToString());
            }
        }

        private void Disk_Click(object s, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(GroupDiskPage), new GroupDetailNavigationArgs { GroupId = _groupId, Token = _token });
        }

        private async void Report_Click(object s, RoutedEventArgs e)
        {
            var box = new TextBox { PlaceholderText = "举报原因" };
            var d = new ContentDialog { Title = "举报群聊", Content = box, PrimaryButtonText = "提交", SecondaryButtonText = "取消" };
            if (await d.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
            {
                try
                {
                    await Alert(await GroupAuxApi.ReportAsync(_token, _groupId, _group.Name, box.Text.Trim(), ""));
                }
                catch (Exception ex)
                {
                    AppLogger.Log("GroupDetailPage", "Report_Click exception: " + ex.ToString());
                }
            }
        }

        private async void Leave_Click(object s, RoutedEventArgs e)
        {
            var d = new MessageDialog("确定退出此群聊吗？", "退出群聊");
            d.Commands.Add(new UICommand("退出", async x =>
            {
                try
                {
                    await GroupApi.LeaveAsync(_token, _groupId);
                    if (Frame.CanGoBack) Frame.GoBack();
                }
                catch (Exception ex)
                {
                    AppLogger.Log("GroupDetailPage", "Leave_Click exception: " + ex.ToString());
                }
            }));
            d.Commands.Add(new UICommand("取消"));
            await d.ShowAsync();
        }

        private async Task Alert(string text)
        {
            try
            {
                await new MessageDialog(text ?? "操作失败", "云湖").ShowAsync();
            }
            catch { }
        }

        private static string ExtractShareUrl(string text)
        {
            try
            {
                JsonObject root;
                if (JsonObject.TryParse(text, out root) && root.ContainsKey("data"))
                {
                    var data = root.GetNamedObject("data");
                    if (data.ContainsKey("shareUrl")) return data.GetNamedString("shareUrl") + (data.ContainsKey("key") ? "?key=" + data.GetNamedString("key") : "");
                }
            }
            catch { }
            return text;
        }
    }

    public class GroupDetailNavigationArgs
    {
        public string GroupId { get; set; }
        public string Token { get; set; }
        public int ChatType { get; set; }
        public string Title { get; set; }
        public string AvatarUrl { get; set; }
    }
}
