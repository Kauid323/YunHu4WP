using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.Phone.UI.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Api.Common;
using 云湖WP.Api.Group;
using 云湖WP.Api.User;
using 云湖WP.Api.WebDAV;
using 云湖WP.Token;
using 云湖WP.Utils;

namespace 云湖WP
{
    public sealed partial class GroupDiskPage : Page, IFileOpenPickerContinuable
    {
        private string _token;
        private string _groupId;
        private GroupInfoModel _groupInfo;
        private bool _isOwnerVip;
        private long _totalCapacity = 512L * 1024L * 1024L; // 默认 512MB
        private long _usedCapacity = 0;

        // 群文件状态
        private long _currentFolderId = 0;
        private readonly List<BreadcrumbItem> _breadcrumbs = new List<BreadcrumbItem>();
        private List<GroupDiskFile> _currentFiles = new List<GroupDiskFile>();

        // WebDAV 状态
        private List<WebDAVMountSetting> _mounts = new List<WebDAVMountSetting>();
        private WebDAVMountSetting _currentWebDavMount;
        private string _currentWebDavRelPath = "";
        private readonly List<string> _webDavBreadcrumbs = new List<string>();

        // 上传/下载取消控制
        private System.Threading.CancellationTokenSource _transferCts;

        public class BreadcrumbItem
        {
            public long Id { get; set; }
            public string Name { get; set; }
        }

        public GroupDiskPage()
        {
            InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            var args = e.Parameter as GroupDetailNavigationArgs;
            string newGroupId = args == null ? e.Parameter as string : args.GroupId;
            string newToken = args == null ? await TokenManager.GetTokenAsync() : (args.Token ?? await TokenManager.GetTokenAsync());

            bool isDifferent = !string.IsNullOrEmpty(newGroupId) && newGroupId != _groupId;
            _groupId = string.IsNullOrEmpty(newGroupId) ? _groupId : newGroupId;
            _token = string.IsNullOrEmpty(newToken) ? _token : newToken;

            if (e.NavigationMode == NavigationMode.Back && !isDifferent && _currentFiles != null && _currentFiles.Count > 0)
            {
                AppLogger.Log("GroupDiskPage", "返回群网盘页面 (使用已有缓存): " + _groupId);
                return;
            }

            _currentFolderId = 0;
            _breadcrumbs.Clear();
            _currentWebDavMount = null;
            _currentWebDavRelPath = "";
            _webDavBreadcrumbs.Clear();
            WebDavMountsView.Visibility = Visibility.Visible;
            WebDavFilesView.Visibility = Visibility.Collapsed;

            UpdateBreadcrumbsUI();
            await LoadGroupCapacityAsync();
            await LoadDiskFilesAsync(0);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
            base.OnNavigatedFrom(e);
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            e.Handled = true;
            HandleBackNavigation();
        }

        private void HandleBackNavigation()
        {
            // 1. 如果在 WebDAV 文件浏览视图
            if (DiskPivot.SelectedIndex == 1 && WebDavFilesView.Visibility == Visibility.Visible)
            {
                if (_webDavBreadcrumbs.Count > 0)
                {
                    _webDavBreadcrumbs.RemoveAt(_webDavBreadcrumbs.Count - 1);
                    _currentWebDavRelPath = string.Join("/", _webDavBreadcrumbs);
                    UpdateWebDavBreadcrumbsUI();
                    var task = LoadWebDavFilesAsync();
                    return;
                }
                else
                {
                    // 返回挂载点列表
                    _currentWebDavMount = null;
                    WebDavFilesView.Visibility = Visibility.Collapsed;
                    WebDavMountsView.Visibility = Visibility.Visible;
                    UpdateCommandBarState();
                    return;
                }
            }

            // 2. 如果在群网盘子文件夹内
            if (DiskPivot.SelectedIndex == 0 && _breadcrumbs.Count > 0)
            {
                _breadcrumbs.RemoveAt(_breadcrumbs.Count - 1);
                long targetId = _breadcrumbs.Count > 0 ? _breadcrumbs.Last().Id : 0;
                _currentFolderId = targetId;
                UpdateBreadcrumbsUI();
                var task = LoadDiskFilesAsync(targetId);
                return;
            }

            // 3. 根目录退出页面
            if (Frame != null && Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void BackUp_Click(object sender, RoutedEventArgs e)
        {
            HandleBackNavigation();
        }

        #region 容量与群信息加载

        private async Task LoadGroupCapacityAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_groupId)) return;
                var infoResult = await GroupApi.GetInfoAsync(_token, _groupId);
                if (infoResult != null && infoResult.Group != null)
                {
                    _groupInfo = infoResult.Group;
                    DiskPivot.Title = string.IsNullOrEmpty(_groupInfo.Name) ? "群网盘" : (_groupInfo.Name + " 的网盘");

                    // 检查群主是否 VIP
                    string ownerId = _groupInfo.Owner;
                    if (!string.IsNullOrEmpty(ownerId))
                    {
                        var ownerDetail = await UserApi.GetUserDetailAsync(_token, ownerId);
                        if (ownerDetail != null && ownerDetail.Data != null)
                        {
                            _isOwnerVip = ownerDetail.Data.IsVip;
                        }
                    }

                    // VIP 群主 1.0 GB，非 VIP 512.0 MB
                    _totalCapacity = _isOwnerVip ? (1024L * 1024L * 1024L) : (512L * 1024L * 1024L);
                }

                // 获取服务器统计的总占用大小
                _usedCapacity = await GroupDiskApi.GetTotalSizeAsync(_token, _groupId);
                UpdateCapacityUI();
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDiskPage", "LoadGroupCapacityAsync 异常: " + ex.Message);
            }
        }

        private void UpdateCapacityUI()
        {
            double usedMB = _usedCapacity / (1024.0 * 1024.0);
            double totalMB = _totalCapacity / (1024.0 * 1024.0);
            double pct = totalMB > 0 ? (usedMB / totalMB * 100.0) : 0.0;

            CapacityDetailText.Text = string.Format("{0} / {1} ({2:F1}%)", GroupDiskFile.FormatSize(_usedCapacity), GroupDiskFile.FormatSize(_totalCapacity), pct);
            CapacityProgressBar.Value = Math.Min(100.0, Math.Max(0.0, pct));
        }

        #endregion

        #region 群文件列表与面包屑

        private async Task LoadDiskFilesAsync(long folderId)
        {
            Progress.Visibility = Visibility.Visible;
            _currentFolderId = folderId;
            UpdateCommandBarState();

            try
            {
                var files = await GroupDiskApi.ListAsync(_token, _groupId, folderId);
                // 文件夹排在前面，文件排在后面
                var sorted = files.OrderByDescending(f => f.IsFolder).ThenBy(f => f.Name).ToList();
                _currentFiles = sorted;
                FilesList.ItemsSource = _currentFiles;
                EmptyFilesText.Visibility = (_currentFiles == null || _currentFiles.Count == 0) ? Visibility.Visible : Visibility.Collapsed;

                // 如果服务器未返回总大小，用文件大小累计辅助更新
                if (_usedCapacity <= 0 && _currentFiles != null)
                {
                    long sum = _currentFiles.Sum(f => f.FileSize);
                    if (sum > 0)
                    {
                        _usedCapacity = sum;
                        UpdateCapacityUI();
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDiskPage", "LoadDiskFilesAsync 异常: " + ex.Message);
            }
            finally
            {
                Progress.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateBreadcrumbsUI()
        {
            BreadcrumbPanel.Children.Clear();

            // 1. 根目录按钮
            var rootBtn = new Button
            {
                Content = "根目录",
                FontSize = 12,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 4, 0),
                Background = _currentFolderId == 0 ? (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneAccentBrush"] : null
            };
            rootBtn.Click += (s, e) =>
            {
                _breadcrumbs.Clear();
                UpdateBreadcrumbsUI();
                var task = LoadDiskFilesAsync(0);
            };
            BreadcrumbPanel.Children.Add(rootBtn);

            // 2. 层级路径按钮
            for (int i = 0; i < _breadcrumbs.Count; i++)
            {
                var item = _breadcrumbs[i];
                int index = i;

                var sep = new TextBlock
                {
                    Text = "›",
                    FontSize = 14,
                    Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneMidBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 4, 0)
                };
                BreadcrumbPanel.Children.Add(sep);

                bool isLast = (i == _breadcrumbs.Count - 1);
                var btn = new Button
                {
                    Content = item.Name,
                    FontSize = 12,
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = isLast ? (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneAccentBrush"] : null
                };
                btn.Click += (s, e) =>
                {
                    var newCrumbs = _breadcrumbs.Take(index + 1).ToList();
                    _breadcrumbs.Clear();
                    _breadcrumbs.AddRange(newCrumbs);
                    UpdateBreadcrumbsUI();
                    var task = LoadDiskFilesAsync(item.Id);
                };
                BreadcrumbPanel.Children.Add(btn);
            }

            UpdateCommandBarState();
        }

        private async void File_Click(object sender, ItemClickEventArgs e)
        {
            var file = e.ClickedItem as GroupDiskFile;
            if (file == null) return;

            // 1. 如果是文件夹，进入文件夹
            if (file.IsFolder)
            {
                _breadcrumbs.Add(new BreadcrumbItem { Id = file.Id, Name = file.Name });
                UpdateBreadcrumbsUI();
                await LoadDiskFilesAsync(file.Id);
                return;
            }

            // 2. 如果是文件，弹出操作面板
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = file.Name, FontSize = 16, FontWeight = Windows.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = string.Format("大小: {0} · 上传者: {1}", file.TypeText, string.IsNullOrEmpty(file.UploadByName) ? "未知" : file.UploadByName), FontSize = 12, Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneMidBrush"], Margin = new Thickness(0, 4, 0, 12) });

            var downloadBtn = new Button { Content = "下载到手机", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
            var renameBtn = new Button { Content = "重命名", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
            var deleteBtn = new Button { Content = "删除文件", HorizontalAlignment = HorizontalAlignment.Stretch };

            panel.Children.Add(downloadBtn);
            panel.Children.Add(renameBtn);
            panel.Children.Add(deleteBtn);

            var dialog = new ContentDialog { Title = "文件操作", Content = panel, SecondaryButtonText = "关闭" };

            downloadBtn.Click += async (s, args) =>
            {
                dialog.Hide();
                await StartDownloadDiskFileAsync(file);
            };

            renameBtn.Click += async (s, args) =>
            {
                dialog.Hide();
                await ShowRenameDialogAsync(file.Id, file.ObjectType, file.Name);
            };

            deleteBtn.Click += async (s, args) =>
            {
                dialog.Hide();
                await ShowDeleteDialogAsync(file.Id, file.ObjectType, file.Name);
            };

            await dialog.ShowAsync();
        }

        private async Task StartDownloadDiskFileAsync(GroupDiskFile file)
        {
            if (file == null || string.IsNullOrEmpty(file.DownloadUrl))
            {
                await Alert("文件下载链接无效");
                return;
            }

            if (_transferCts != null)
            {
                try { _transferCts.Cancel(); } catch { }
            }
            _transferCts = new System.Threading.CancellationTokenSource();

            TransferStatusBorder.Visibility = Visibility.Visible;
            TransferProgressBar.Value = 0;
            TransferStatusText.Text = string.Format("正在下载 {0} (0%)...", file.Name);

            var progress = new Progress<double>(pct =>
            {
                TransferProgressBar.Value = pct;
                TransferStatusText.Text = string.Format("正在下载 {0} ({1:F0}%)...", file.Name, pct);
            });

            try
            {
                var downloadedFile = await FileDownloadHelper.DownloadFileWithProgressAsync(file.DownloadUrl, file.Name, file.FileSize, progress, _transferCts.Token);
                TransferStatusText.Text = string.Format("{0} 下载完成", file.Name);
                AppLogger.Log("GroupDiskPage", "群文件下载完成: " + (downloadedFile != null ? downloadedFile.Path : "系统库"));
                AutoDismissTransferStatusAsync(3000);
            }
            catch (OperationCanceledException)
            {
                TransferStatusText.Text = "已取消下载";
                AutoDismissTransferStatusAsync(2000);
            }
            catch (System.Net.WebException wex)
            {
                if (_transferCts.IsCancellationRequested)
                {
                    TransferStatusText.Text = "已取消下载";
                    AutoDismissTransferStatusAsync(2000);
                }
                else
                {
                    TransferStatusText.Text = string.Format("下载失败: {0}", wex.Message);
                    AppLogger.Log("GroupDiskPage", "群文件下载 WebException: " + wex.Message);
                    AutoDismissTransferStatusAsync(4000);
                }
            }
            catch (Exception ex)
            {
                if (_transferCts.IsCancellationRequested)
                {
                    TransferStatusText.Text = "已取消下载";
                    AutoDismissTransferStatusAsync(2000);
                }
                else
                {
                    TransferStatusText.Text = string.Format("下载失败: {0}", ex.Message);
                    AppLogger.Log("GroupDiskPage", "群文件下载失败: " + ex.Message);
                    AutoDismissTransferStatusAsync(4000);
                }
            }
        }

        private async Task ShowRenameDialogAsync(long id, int objectType, string currentName)
        {
            var box = new TextBox { Text = currentName ?? "", PlaceholderText = "请输入新名称" };
            var d = new ContentDialog { Title = "重命名", Content = box, PrimaryButtonText = "保存", SecondaryButtonText = "取消" };
            if (await d.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
            {
                Progress.Visibility = Visibility.Visible;
                var res = await GroupDiskApi.RenameAsync(_token, id, objectType, box.Text.Trim());
                Progress.Visibility = Visibility.Collapsed;
                if (!res.IsSuccess) await Alert(res.Msg ?? "重命名失败");
                else await LoadDiskFilesAsync(_currentFolderId);
            }
        }

        private async Task ShowDeleteDialogAsync(long id, int objectType, string name)
        {
            var md = new MessageDialog(string.Format("确定删除 \"{0}\" 吗？", name), "删除确认");
            md.Commands.Add(new UICommand("删除", async x =>
            {
                Progress.Visibility = Visibility.Visible;
                var res = await GroupDiskApi.RemoveAsync(_token, id, objectType);
                Progress.Visibility = Visibility.Collapsed;
                if (!res.IsSuccess) await Alert(res.Msg ?? "删除失败");
                else
                {
                    await LoadDiskFilesAsync(_currentFolderId);
                    await LoadGroupCapacityAsync();
                }
            }));
            md.Commands.Add(new UICommand("取消"));
            await md.ShowAsync();
        }

        #endregion

        #region WebDAV 挂载与文件浏览

        private async Task LoadWebDavMountsAsync()
        {
            Progress.Visibility = Visibility.Visible;
            try
            {
                _mounts = await GroupMountApi.GetMountListAsync(_token, _groupId);
                MountsList.ItemsSource = _mounts;
                EmptyMountsText.Visibility = (_mounts == null || _mounts.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDiskPage", "LoadWebDavMountsAsync 异常: " + ex.Message);
            }
            finally
            {
                Progress.Visibility = Visibility.Collapsed;
            }
        }

        private async void Mount_Click(object sender, ItemClickEventArgs e)
        {
            var mount = e.ClickedItem as WebDAVMountSetting;
            if (mount == null) return;

            _currentWebDavMount = mount;
            _webDavBreadcrumbs.Clear();
            _currentWebDavRelPath = "";

            WebDavMountTitle.Text = mount.DisplayTitle;
            WebDavMountSub.Text = mount.WebdavUrl;

            WebDavMountsView.Visibility = Visibility.Collapsed;
            WebDavFilesView.Visibility = Visibility.Visible;

            UpdateWebDavBreadcrumbsUI();
            await LoadWebDavFilesAsync();
        }

        private async Task LoadWebDavFilesAsync()
        {
            if (_currentWebDavMount == null) return;
            Progress.Visibility = Visibility.Visible;
            UpdateCommandBarState();

            try
            {
                var files = await WebDAVClient.ListFilesAsync(_currentWebDavMount, _currentWebDavRelPath);
                var sorted = files.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Name).ToList();
                WebDavFilesList.ItemsSource = sorted;
                EmptyWebDavFilesText.Visibility = (sorted.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                AppLogger.Log("GroupDiskPage", "LoadWebDavFilesAsync 异常: " + ex.Message);
            }
            finally
            {
                Progress.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateWebDavBreadcrumbsUI()
        {
            WebDavBreadcrumbPanel.Children.Clear();

            var rootBtn = new Button
            {
                Content = "根目录",
                FontSize = 12,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 4, 0),
                Background = _webDavBreadcrumbs.Count == 0 ? (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneAccentBrush"] : null
            };
            rootBtn.Click += (s, e) =>
            {
                _webDavBreadcrumbs.Clear();
                _currentWebDavRelPath = "";
                UpdateWebDavBreadcrumbsUI();
                var task = LoadWebDavFilesAsync();
            };
            WebDavBreadcrumbPanel.Children.Add(rootBtn);

            for (int i = 0; i < _webDavBreadcrumbs.Count; i++)
            {
                string seg = _webDavBreadcrumbs[i];
                int index = i;

                var sep = new TextBlock
                {
                    Text = "›",
                    FontSize = 14,
                    Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneMidBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 4, 0)
                };
                WebDavBreadcrumbPanel.Children.Add(sep);

                bool isLast = (i == _webDavBreadcrumbs.Count - 1);
                var btn = new Button
                {
                    Content = seg,
                    FontSize = 12,
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = isLast ? (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneAccentBrush"] : null
                };
                btn.Click += (s, e) =>
                {
                    var newCrumbs = _webDavBreadcrumbs.Take(index + 1).ToList();
                    _webDavBreadcrumbs.Clear();
                    _webDavBreadcrumbs.AddRange(newCrumbs);
                    _currentWebDavRelPath = string.Join("/", _webDavBreadcrumbs);
                    UpdateWebDavBreadcrumbsUI();
                    var task = LoadWebDavFilesAsync();
                };
                WebDavBreadcrumbPanel.Children.Add(btn);
            }

            UpdateCommandBarState();
        }

        private async void WebDavFile_Click(object sender, ItemClickEventArgs e)
        {
            var file = e.ClickedItem as WebDAVFile;
            if (file == null) return;

            if (file.IsDirectory)
            {
                _webDavBreadcrumbs.Add(file.Name);
                _currentWebDavRelPath = string.Join("/", _webDavBreadcrumbs);
                UpdateWebDavBreadcrumbsUI();
                await LoadWebDavFilesAsync();
                return;
            }

            // 文件操作
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = file.Name, FontSize = 16, FontWeight = Windows.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = string.Format("大小: {0}", file.TypeText), FontSize = 12, Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneMidBrush"], Margin = new Thickness(0, 4, 0, 12) });

            var downloadBtn = new Button { Content = "下载此 WebDAV 文件", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
            var deleteBtn = new Button { Content = "删除此文件", HorizontalAlignment = HorizontalAlignment.Stretch };

            panel.Children.Add(downloadBtn);
            panel.Children.Add(deleteBtn);

            var dialog = new ContentDialog { Title = "WebDAV 文件", Content = panel, SecondaryButtonText = "关闭" };

            downloadBtn.Click += async (s, args) =>
            {
                dialog.Hide();
                await StartDownloadWebDavFileAsync(file);
            };

            deleteBtn.Click += async (s, args) =>
            {
                dialog.Hide();
                var confirm = new MessageDialog(string.Format("确定在 WebDAV 上删除 \"{0}\" 吗？", file.Name), "删除确认");
                confirm.Commands.Add(new UICommand("删除", async x =>
                {
                    Progress.Visibility = Visibility.Visible;
                    bool ok = await WebDAVClient.DeleteAsync(_currentWebDavMount, file.Path);
                    Progress.Visibility = Visibility.Collapsed;
                    if (ok) await LoadWebDavFilesAsync(); else await Alert("删除 WebDAV 资源失败");
                }));
                confirm.Commands.Add(new UICommand("取消"));
                await confirm.ShowAsync();
            };

            await dialog.ShowAsync();
        }

        private async Task StartDownloadWebDavFileAsync(WebDAVFile file)
        {
            if (file == null || _currentWebDavMount == null) return;

            if (_transferCts != null)
            {
                try { _transferCts.Cancel(); } catch { }
            }
            _transferCts = new System.Threading.CancellationTokenSource();

            TransferStatusBorder.Visibility = Visibility.Visible;
            TransferProgressBar.Value = 0;
            TransferStatusText.Text = string.Format("正在下载 WebDAV 文件 {0} (0%)...", file.Name);

            var progress = new Progress<double>(pct =>
            {
                TransferProgressBar.Value = pct;
                TransferStatusText.Text = string.Format("正在下载 WebDAV 文件 {0} ({1:F0}%)...", file.Name, pct);
            });

            try
            {
                var dlFile = await WebDAVClient.DownloadFileAsync(_currentWebDavMount, file, progress, _transferCts.Token);
                TransferStatusText.Text = string.Format("WebDAV 文件 {0} 下载完成", file.Name);
                AutoDismissTransferStatusAsync(3000);
            }
            catch (OperationCanceledException)
            {
                TransferStatusText.Text = "已取消下载";
                AutoDismissTransferStatusAsync(2000);
            }
            catch (System.Net.WebException wex)
            {
                if (_transferCts.IsCancellationRequested)
                {
                    TransferStatusText.Text = "已取消下载";
                    AutoDismissTransferStatusAsync(2000);
                }
                else
                {
                    TransferStatusText.Text = string.Format("WebDAV 下载失败: {0}", wex.Message);
                    AutoDismissTransferStatusAsync(4000);
                }
            }
            catch (Exception ex)
            {
                if (_transferCts.IsCancellationRequested)
                {
                    TransferStatusText.Text = "已取消下载";
                    AutoDismissTransferStatusAsync(2000);
                }
                else
                {
                    TransferStatusText.Text = string.Format("WebDAV 下载失败: {0}", ex.Message);
                    AutoDismissTransferStatusAsync(4000);
                }
            }
        }

        private void ExitWebDav_Click(object sender, RoutedEventArgs e)
        {
            _currentWebDavMount = null;
            WebDavFilesView.Visibility = Visibility.Collapsed;
            WebDavMountsView.Visibility = Visibility.Visible;
            UpdateCommandBarState();
        }

        private async void AddMount_Click(object sender, RoutedEventArgs e)
        {
            var nameBox = new TextBox { PlaceholderText = "挂载点名称 (如: 我的坚果云)" };
            var urlBox = new TextBox { PlaceholderText = "WebDAV URL (如: https://dav.example.com)" };
            var userBox = new TextBox { PlaceholderText = "用户名 / 账号" };
            var pwdBox = new PasswordBox { PlaceholderText = "密码" };
            var rootBox = new TextBox { Text = "/", PlaceholderText = "根目录路径 (默认 /)" };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "挂载名称", FontSize = 12, Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneMidBrush"], Margin = new Thickness(0, 0, 0, 2) });
            panel.Children.Add(nameBox);
            panel.Children.Add(new TextBlock { Text = "WebDAV 地址", FontSize = 12, Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneMidBrush"], Margin = new Thickness(0, 6, 0, 2) });
            panel.Children.Add(urlBox);
            panel.Children.Add(new TextBlock { Text = "用户名", FontSize = 12, Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneMidBrush"], Margin = new Thickness(0, 6, 0, 2) });
            panel.Children.Add(userBox);
            panel.Children.Add(new TextBlock { Text = "密码 (服务端自动 RSA 加密保护)", FontSize = 12, Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneMidBrush"], Margin = new Thickness(0, 6, 0, 2) });
            panel.Children.Add(pwdBox);
            panel.Children.Add(new TextBlock { Text = "根路径", FontSize = 12, Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PhoneMidBrush"], Margin = new Thickness(0, 6, 0, 2) });
            panel.Children.Add(rootBox);

            var dialog = new ContentDialog { Title = "添加 WebDAV 挂载", Content = panel, PrimaryButtonText = "添加", SecondaryButtonText = "取消" };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text) || string.IsNullOrWhiteSpace(urlBox.Text))
                {
                    await Alert("挂载名称与 WebDAV URL 不能为空");
                    return;
                }

                Progress.Visibility = Visibility.Visible;
                var res = await GroupMountApi.CreateMountAsync(_token, _groupId, nameBox.Text.Trim(), urlBox.Text.Trim(), userBox.Text.Trim(), pwdBox.Password, rootBox.Text.Trim());
                Progress.Visibility = Visibility.Collapsed;

                if (!res.IsSuccess) await Alert(res.Msg ?? "添加失败");
                else await LoadWebDavMountsAsync();
            }
        }

        private async void DeleteMount_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var mount = btn != null ? btn.Tag as WebDAVMountSetting : null;
            if (mount == null) return;

            var d = new MessageDialog(string.Format("确定删除挂载点 \"{0}\" 吗？", mount.DisplayTitle), "删除 WebDAV 挂载");
            d.Commands.Add(new UICommand("删除", async x =>
            {
                Progress.Visibility = Visibility.Visible;
                var res = await GroupMountApi.DeleteMountAsync(_token, mount.Id);
                Progress.Visibility = Visibility.Collapsed;
                if (!res.IsSuccess) await Alert(res.Msg ?? "删除失败");
                else await LoadWebDavMountsAsync();
            }));
            d.Commands.Add(new UICommand("取消"));
            await d.ShowAsync();
        }

        #endregion

        #region 工具栏操作与文件选择器上传 (IFileOpenPickerContinuable)

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (DiskPivot.SelectedIndex == 0)
            {
                await LoadGroupCapacityAsync();
                await LoadDiskFilesAsync(_currentFolderId);
            }
            else
            {
                if (WebDavFilesView.Visibility == Visibility.Visible)
                {
                    await LoadWebDavFilesAsync();
                }
                else
                {
                    await LoadWebDavMountsAsync();
                }
            }
        }

        private void Upload_Click(object sender, RoutedEventArgs e)
        {
            if (DiskPivot.SelectedIndex == 0)
            {
                PickFileForUpload("UploadDisk");
            }
            else if (DiskPivot.SelectedIndex == 1 && WebDavFilesView.Visibility == Visibility.Visible)
            {
                PickFileForUpload("UploadWebDav");
            }
            else
            {
                AddMount_Click(sender, e);
            }
        }

        private void PickFileForUpload(string action)
        {
            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");
            picker.ContinuationData["Action"] = action;
            picker.ContinuationData["FolderId"] = _currentFolderId.ToString();
            picker.ContinuationData["WebDavRelPath"] = _currentWebDavRelPath;
            picker.PickSingleFileAndContinue();
        }

        public async void ContinueFileOpenPicker(FileOpenPickerContinuationEventArgs args)
        {
            if (args == null || args.Files == null || args.Files.Count == 0) return;
            var file = args.Files[0];
            string action = args.ContinuationData.ContainsKey("Action") ? args.ContinuationData["Action"] as string : "";

            if (action == "UploadDisk")
            {
                await UploadToDiskAsync(file);
            }
            else if (action == "UploadWebDav")
            {
                await UploadToWebDavAsync(file);
            }
        }

        private async Task UploadToDiskAsync(StorageFile file)
        {
            if (_transferCts != null)
            {
                try { _transferCts.Cancel(); } catch { }
            }
            _transferCts = new System.Threading.CancellationTokenSource();

            TransferStatusBorder.Visibility = Visibility.Visible;
            TransferProgressBar.Value = 0;
            TransferStatusText.Text = string.Format("正在上传 {0} 到群网盘 (0%)...", file.Name);

            var progress = new Progress<double>(pct =>
            {
                TransferProgressBar.Value = pct;
                TransferStatusText.Text = string.Format("正在上传 {0} ({1:F0}%)...", file.Name, pct);
            });

            try
            {
                var uploadRes = await QiniuUploadHelper.UploadFileDetailedAsync(file, _token, progress, _transferCts.Token);
                if (uploadRes == null || string.IsNullOrEmpty(uploadRes.Key))
                {
                    throw new Exception("文件直传未返回有效 Key");
                }

                TransferStatusText.Text = string.Format("登记文件 {0} 到群网盘...", file.Name);

                string fileMd5 = uploadRes.Key.Replace("disk/", "");
                if (fileMd5.Contains(".")) fileMd5 = fileMd5.Substring(0, fileMd5.IndexOf('.'));

                var recordRes = await GroupDiskApi.RecordUploadAsync(
                    _token,
                    _groupId,
                    uploadRes.FileName,
                    uploadRes.FileSize,
                    fileMd5,
                    uploadRes.Hash,
                    uploadRes.Key,
                    _currentFolderId
                );

                if (!recordRes.IsSuccess)
                {
                    throw new Exception(recordRes.Msg ?? "登记上传记录失败");
                }

                TransferStatusText.Text = string.Format("{0} 上传成功", file.Name);
                await LoadDiskFilesAsync(_currentFolderId);
                await LoadGroupCapacityAsync();
                AutoDismissTransferStatusAsync(3000);
            }
            catch (OperationCanceledException)
            {
                TransferStatusText.Text = "已取消上传";
                AutoDismissTransferStatusAsync(2000);
            }
            catch (Exception ex)
            {
                if (_transferCts.IsCancellationRequested)
                {
                    TransferStatusText.Text = "已取消上传";
                    AutoDismissTransferStatusAsync(2000);
                }
                else
                {
                    TransferStatusText.Text = string.Format("上传失败: {0}", ex.Message);
                    AppLogger.Log("GroupDiskPage", "UploadToDiskAsync 异常: " + ex.Message);
                    AutoDismissTransferStatusAsync(4000);
                }
            }
        }

        private async Task UploadToWebDavAsync(StorageFile file)
        {
            if (_currentWebDavMount == null) return;

            if (_transferCts != null)
            {
                try { _transferCts.Cancel(); } catch { }
            }
            _transferCts = new System.Threading.CancellationTokenSource();

            TransferStatusBorder.Visibility = Visibility.Visible;
            TransferProgressBar.Value = 0;
            TransferStatusText.Text = string.Format("正在上传 {0} 到 WebDAV...", file.Name);

            var progress = new Progress<double>(pct =>
            {
                TransferProgressBar.Value = pct;
                TransferStatusText.Text = string.Format("正在上传 {0} 到 WebDAV ({1:F0}%)...", file.Name, pct);
            });

            try
            {
                bool ok = await WebDAVClient.UploadFileAsync(_currentWebDavMount, file, _currentWebDavRelPath, progress, _transferCts.Token);
                if (!ok) throw new Exception("WebDAV 服务端返回上传失败");

                TransferStatusText.Text = string.Format("WebDAV 文件 {0} 上传成功", file.Name);
                await LoadWebDavFilesAsync();
                AutoDismissTransferStatusAsync(3000);
            }
            catch (OperationCanceledException)
            {
                TransferStatusText.Text = "已取消上传";
                AutoDismissTransferStatusAsync(2000);
            }
            catch (System.Net.WebException wex)
            {
                if (_transferCts.IsCancellationRequested)
                {
                    TransferStatusText.Text = "已取消上传";
                    AutoDismissTransferStatusAsync(2000);
                }
                else
                {
                    TransferStatusText.Text = string.Format("WebDAV 上传失败: {0}", wex.Message);
                    AutoDismissTransferStatusAsync(4000);
                }
            }
            catch (Exception ex)
            {
                if (_transferCts.IsCancellationRequested)
                {
                    TransferStatusText.Text = "已取消上传";
                    AutoDismissTransferStatusAsync(2000);
                }
                else
                {
                    TransferStatusText.Text = string.Format("WebDAV 上传失败: {0}", ex.Message);
                    AppLogger.Log("GroupDiskPage", "UploadToWebDavAsync 异常: " + ex.Message);
                    AutoDismissTransferStatusAsync(4000);
                }
            }
        }

        private async void CreateFolder_Click(object sender, RoutedEventArgs e)
        {
            var box = new TextBox { PlaceholderText = "请输入文件夹名称" };
            var dialog = new ContentDialog { Title = "新建文件夹", Content = box, PrimaryButtonText = "创建", SecondaryButtonText = "取消" };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
            {
                string folderName = box.Text.Trim();
                Progress.Visibility = Visibility.Visible;

                if (DiskPivot.SelectedIndex == 0)
                {
                    var res = await GroupDiskApi.CreateFolderAsync(_token, _groupId, folderName, _currentFolderId);
                    Progress.Visibility = Visibility.Collapsed;
                    if (!res.IsSuccess) await Alert(res.Msg ?? "创建文件夹失败");
                    else await LoadDiskFilesAsync(_currentFolderId);
                }
                else if (DiskPivot.SelectedIndex == 1 && WebDavFilesView.Visibility == Visibility.Visible && _currentWebDavMount != null)
                {
                    bool ok = await WebDAVClient.CreateFolderAsync(_currentWebDavMount, _currentWebDavRelPath, folderName);
                    Progress.Visibility = Visibility.Collapsed;
                    if (!ok) await Alert("创建 WebDAV 文件夹失败");
                    else await LoadWebDavFilesAsync();
                }
            }
        }

        private async void DiskPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCommandBarState();
            if (DiskPivot.SelectedIndex == 1 && _mounts.Count == 0 && WebDavFilesView.Visibility != Visibility.Visible)
            {
                await LoadWebDavMountsAsync();
            }
        }

        private void UpdateCommandBarState()
        {
            if (DiskPivot.SelectedIndex == 0)
            {
                UploadButton.Label = "上传文件";
                NewFolderButton.Visibility = Visibility.Visible;
                BackUpButton.Visibility = _breadcrumbs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                if (WebDavFilesView.Visibility == Visibility.Visible)
                {
                    UploadButton.Label = "上传到WebDAV";
                    NewFolderButton.Visibility = Visibility.Visible;
                    BackUpButton.Visibility = Visibility.Visible;
                }
                else
                {
                    UploadButton.Label = "添加挂载";
                    NewFolderButton.Visibility = Visibility.Collapsed;
                    BackUpButton.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void CancelTransfer_Click(object sender, RoutedEventArgs e)
        {
            if (_transferCts != null)
            {
                try { _transferCts.Cancel(); } catch { }
            }
            TransferStatusText.Text = "已取消传输";
            AutoDismissTransferStatusAsync(2000);
        }

        private async void AutoDismissTransferStatusAsync(int delayMs = 3000)
        {
            try
            {
                await Task.Delay(delayMs);
                TransferStatusBorder.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private async Task Alert(string text)
        {
            try
            {
                await new MessageDialog(text ?? "操作完成", "群网盘").ShowAsync();
            }
            catch { }
        }

        #endregion
    }
}
