using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Graphics.Display;
using Windows.Phone.UI.Input;
using Windows.Storage;
using Windows.System;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using 云湖WP.Utils;

namespace 云湖WP
{
    /// <summary>
    /// 全屏文本查看与复制界面 (支持纯文本选中复制、WebView 页面渲染模式、调用系统浏览器渲染)
    /// </summary>
    public sealed partial class TextViewerPage : Page
    {
        private bool _isWebMode = false;

        public TextViewerPage()
        {
            this.InitializeComponent();
            this.Loaded += TextViewerPage_Loaded;
            this.Unloaded += TextViewerPage_Unloaded;
            this.SizeChanged += TextViewerPage_SizeChanged;
        }

        private void TextViewerPage_Loaded(object sender, RoutedEventArgs e)
        {
            DisplayInformation.GetForCurrentView().OrientationChanged += DisplayInformation_OrientationChanged;
            UpdateOrientationLayout();
        }

        private void TextViewerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            DisplayInformation.GetForCurrentView().OrientationChanged -= DisplayInformation_OrientationChanged;
        }

        private void DisplayInformation_OrientationChanged(DisplayInformation sender, object args)
        {
            UpdateOrientationLayout();
        }

        private void TextViewerPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateOrientationLayout();
        }

        private void UpdateOrientationLayout()
        {
            try
            {
                var orientation = DisplayInformation.GetForCurrentView().CurrentOrientation;
                bool isLandscape = orientation == DisplayOrientations.Landscape ||
                                   orientation == DisplayOrientations.LandscapeFlipped ||
                                   (Window.Current != null && Window.Current.Bounds.Width > Window.Current.Bounds.Height);

                if (isLandscape)
                {
                    if (HeaderPanel != null) HeaderPanel.Margin = new Thickness(16, 4, 16, 2);
                    if (TxtSubTitle != null) TxtSubTitle.Visibility = Visibility.Collapsed;
                    if (TxtPageTitle != null) TxtPageTitle.FontSize = 24;
                }
                else
                {
                    if (HeaderPanel != null) HeaderPanel.Margin = new Thickness(19, 10, 19, 6);
                    if (TxtSubTitle != null) TxtSubTitle.Visibility = Visibility.Visible;
                    if (TxtPageTitle != null) TxtPageTitle.FontSize = 42;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("TextViewerPage", "UpdateOrientationLayout error: " + ex.Message);
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            string content = e.Parameter as string;
            if (content != null)
            {
                TxtMainContent.Text = content;
                // 如果传入的内容明显是 HTML，自动建议并准备渲染
                if (IsLikelyHtml(content))
                {
                    AppBarBtnToggleWeb.Label = "WebView渲染";
                }
            }
            else
            {
                TxtMainContent.Text = "";
            }

            _isWebMode = false;
            TxtMainContent.Visibility = Visibility.Visible;
            WebMainContent.Visibility = Visibility.Collapsed;
            AppBarBtnToggleWeb.Label = "WebView渲染";
            AppBarBtnSelectAll.IsEnabled = true;

            UpdateOrientationLayout();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            e.Handled = true;

            if (_isWebMode && WebMainContent.CanGoBack)
            {
                WebMainContent.GoBack();
                return;
            }

            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void AppBarBtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (!_isWebMode)
            {
                TxtMainContent.Focus(FocusState.Programmatic);
                TxtMainContent.SelectAll();
            }
        }

        private void AppBarBtnClear_Click(object sender, RoutedEventArgs e)
        {
            TxtMainContent.Text = "";
            if (_isWebMode)
            {
                WebMainContent.NavigateToString("<html><body></body></html>");
            }
        }

        private void AppBarBtnToggleWeb_Click(object sender, RoutedEventArgs e)
        {
            _isWebMode = !_isWebMode;

            if (_isWebMode)
            {
                TxtMainContent.Visibility = Visibility.Collapsed;
                WebMainContent.Visibility = Visibility.Visible;
                AppBarBtnToggleWeb.Label = "文本模式";
                AppBarBtnSelectAll.IsEnabled = false;

                string html = WrapHtmlForMobile(TxtMainContent.Text);
                try
                {
                    WebMainContent.NavigateToString(html);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("TextViewerPage", "NavigateToString error: " + ex.Message);
                }
            }
            else
            {
                TxtMainContent.Visibility = Visibility.Visible;
                WebMainContent.Visibility = Visibility.Collapsed;
                AppBarBtnToggleWeb.Label = "WebView渲染";
                AppBarBtnSelectAll.IsEnabled = true;
            }
        }

        private async void AppBarBtnBrowser_Click(object sender, RoutedEventArgs e)
        {
            string content = TxtMainContent.Text != null ? TxtMainContent.Text.Trim() : "";
            if (string.IsNullOrEmpty(content))
            {
                var dialog = new MessageDialog("内容为空，无法在浏览器中打开。", "提示");
                await dialog.ShowAsync();
                return;
            }

            string errorMsg = null;
            try
            {
                // 如果是直接的 URL 链接，直接调用系统浏览器启动
                if (content.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    content.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    await Launcher.LaunchUriAsync(new Uri(content));
                    return;
                }

                // 如果是 HTML 源码或文本，生成临时 HTML 文件并唤起系统 IE 浏览器渲染
                var tempFolder = ApplicationData.Current.TemporaryFolder;
                var htmlFile = await tempFolder.CreateFileAsync("preview.html", CreationCollisionOption.ReplaceExisting);
                string fullHtml = WrapHtmlForMobile(content);
                await FileIO.WriteTextAsync(htmlFile, fullHtml);

                bool success = await Launcher.LaunchFileAsync(htmlFile);
                if (!success)
                {
                    // 备选方案：尝试直接使用 file:// uri 启动
                    await Launcher.LaunchUriAsync(new Uri("file:///" + htmlFile.Path.Replace("\\", "/")));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("TextViewerPage", "Launch browser error: " + ex.Message);
                errorMsg = "调用系统浏览器失败: " + ex.Message;
            }

            if (errorMsg != null)
            {
                var dialog = new MessageDialog(errorMsg, "提示");
                await dialog.ShowAsync();
            }
        }

        private static bool IsLikelyHtml(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            return t.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   t.IndexOf("<!doctype", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   t.IndexOf("<div", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   t.IndexOf("<p>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   t.IndexOf("</p>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   t.IndexOf("<body", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   t.IndexOf("<table", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   t.IndexOf("<script", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   t.IndexOf("<style", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   t.IndexOf("<a href", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   t.IndexOf("<img", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string WrapHtmlForMobile(string rawContent)
        {
            if (string.IsNullOrWhiteSpace(rawContent)) return "<html><body></body></html>";

            string trimmed = rawContent.Trim();
            if (trimmed.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0 &&
                trimmed.IndexOf("<body", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // 如果已经是完整 HTML，且缺少 viewport 则注入 viewport
                if (trimmed.IndexOf("viewport", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    int headIdx = trimmed.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
                    if (headIdx >= 0)
                    {
                        return trimmed.Insert(headIdx + 6, "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0, maximum-scale=3.0, user-scalable=yes\">");
                    }
                }
                return trimmed;
            }

            // HTML 片段，包裹标准的移动端响应式暗色主题外壳
            return string.Format(@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=3.0, user-scalable=yes"" />
    <style>
        body {{
            background-color: #000000;
            color: #FFFFFF;
            font-family: 'Segoe UI', Helvetica, Arial, sans-serif;
            font-size: 15px;
            line-height: 1.5;
            padding: 12px;
            word-wrap: break-word;
        }}
        img {{
            max-width: 100%;
            height: auto;
        }}
        a {{
            color: #0099FF;
            text-decoration: underline;
        }}
        pre, code {{
            background-color: #1E1E1E;
            color: #DCDCDC;
            padding: 4px 6px;
            border-radius: 4px;
            font-family: Consolas, monospace;
            overflow-x: auto;
        }}
        table {{
            border-collapse: collapse;
            width: 100%;
        }}
        th, td {{
            border: 1px solid #333333;
            padding: 6px;
        }}
    </style>
</head>
<body>
    {0}
</body>
</html>", rawContent);
        }
    }
}
