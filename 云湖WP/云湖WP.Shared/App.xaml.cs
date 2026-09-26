using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

// The Blank Application template is documented at http://go.microsoft.com/fwlink/?LinkId=234227

namespace 云湖WP
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public sealed partial class App : Application
    {
#if WINDOWS_PHONE_APP
        private TransitionCollection transitions;
#endif

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.Suspending += this.OnSuspending;
            this.Resuming += (s, e) => { 云湖WP.Utils.NotificationHelper.IsAppInForeground = true; };
            this.UnhandledException += App_UnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void TaskScheduler_UnobservedTaskException(object sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            string err = string.Format("异步任务未捕获异常: {0}", e.Exception != null ? e.Exception.ToString() : "未知错误");
            云湖WP.Utils.AppLogger.Log("UnobservedTaskException", err);
        }

        private async void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            string err = string.Format("未捕获异常: {0}\n{1}", e.Message, e.Exception != null ? e.Exception.ToString() : "");
            云湖WP.Utils.AppLogger.Log("UnhandledException", err);
            try
            {
                var dialog = new Windows.UI.Popups.MessageDialog(err, "运行错误");
                await dialog.ShowAsync();
            }
            catch { }
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used when the application is launched to open a specific file, to display
        /// search results, and so forth.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
#if DEBUG
            if (System.Diagnostics.Debugger.IsAttached)
            {
                this.DebugSettings.EnableFrameRateCounter = true;
            }
#endif

            Frame rootFrame = Window.Current.Content as Frame;

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (rootFrame == null)
            {
                // Create a Frame to act as the navigation context and navigate to the first page
                rootFrame = new Frame();

                // TODO: change this value to a cache size that is appropriate for your application
                rootFrame.CacheSize = 1;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    // TODO: Load state from previously suspended application
                }

                // Place the frame in the current Window
                Window.Current.Content = rootFrame;
            }

            var chatArgs = 云湖WP.Utils.NotificationHelper.ParseChatLaunchArgs(e.Arguments);

            if (rootFrame.Content == null)
            {
#if WINDOWS_PHONE_APP
                // Removes the turnstile navigation for startup.
                if (rootFrame.ContentTransitions != null)
                {
                    this.transitions = new TransitionCollection();
                    foreach (var c in rootFrame.ContentTransitions)
                    {
                        this.transitions.Add(c);
                    }
                }

                rootFrame.ContentTransitions = null;
                rootFrame.Navigated += this.RootFrame_FirstNavigated;
#endif

#if WINDOWS_PHONE_APP
                if (云湖WP.Token.TokenManager.HasToken())
                {
                    // 正常登录状态：先进入 MainPage，保证返回键栈底正确
                    if (!rootFrame.Navigate(typeof(MainPage)))
                    {
                        throw new Exception("Failed to create initial page");
                    }

                    // 若是通过 Toast 通知启动且包含会话参数，直接推入 ChatPage
                    if (chatArgs != null)
                    {
                        rootFrame.Navigate(typeof(ChatPage), chatArgs);
                    }
                }
                else
                {
                    if (!rootFrame.Navigate(typeof(LoginPage), e.Arguments))
                    {
                        throw new Exception("Failed to create initial page");
                    }
                }
#else
                if (!rootFrame.Navigate(typeof(MainPage), e.Arguments))
                {
                    throw new Exception("Failed to create initial page");
                }
#endif
            }
            else
            {
                // App 已经在后台运行或恢复时点击了 Toast 通知
                if (chatArgs != null && 云湖WP.Token.TokenManager.HasToken())
                {
#if WINDOWS_PHONE_APP
                    if (rootFrame.Content is ChatPage)
                    {
                        if (云湖WP.Api.WebSocket.YunhuWebSocketService.Instance.CurrentActiveChatId != chatArgs.ChatId)
                        {
                            rootFrame.Navigate(typeof(ChatPage), chatArgs);
                        }
                    }
                    else
                    {
                        rootFrame.Navigate(typeof(ChatPage), chatArgs);
                    }
#endif
                }
            }

            // Register VisibilityChanged to accurately track app foreground/background lifecycle across all threads
            Window.Current.VisibilityChanged += (s, ev) =>
            {
                云湖WP.Utils.NotificationHelper.IsAppInForeground = ev.Visible;
            };
            云湖WP.Utils.NotificationHelper.IsAppInForeground = true;

            // Ensure the current window is active
            Window.Current.Activate();
        }

#if WINDOWS_PHONE_APP
        /// <summary>
        /// Restores the content transitions after the app has launched.
        /// </summary>
        /// <param name="sender">The object where the handler is attached.</param>
        /// <param name="e">Details about the navigation event.</param>
        private void RootFrame_FirstNavigated(object sender, NavigationEventArgs e)
        {
            var rootFrame = sender as Frame;
            rootFrame.ContentTransitions = this.transitions ?? new TransitionCollection() { new NavigationThemeTransition() };
            rootFrame.Navigated -= this.RootFrame_FirstNavigated;
        }
#endif

        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            云湖WP.Utils.NotificationHelper.IsAppInForeground = false;
            var deferral = e.SuspendingOperation.GetDeferral();

            // TODO: Save application state and stop any background activity
            deferral.Complete();
        }

#if WINDOWS_PHONE_APP
        /// <summary>
        /// 处理 Windows Phone 8.1 文件选择器 Continuation 激活事件与通知激活
        /// </summary>
        protected override void OnActivated(IActivatedEventArgs args)
        {
            base.OnActivated(args);
            云湖WP.Utils.NotificationHelper.IsAppInForeground = true;

            if (args.Kind == ActivationKind.PickFileContinuation)
            {
                var fileArgs = args as FileOpenPickerContinuationEventArgs;
                if (fileArgs != null)
                {
                    var rootFrame = Window.Current.Content as Frame;
                    if (rootFrame != null)
                    {
                        var continuablePage = rootFrame.Content as IFileOpenPickerContinuable;
                        if (continuablePage != null)
                        {
                            continuablePage.ContinueFileOpenPicker(fileArgs);
                        }
                    }
                }
            }

            Window.Current.Activate();
        }
#endif
    }

#if WINDOWS_PHONE_APP
    /// <summary>
    /// Windows Phone 8.1 FileOpenPicker Continuation 接口
    /// </summary>
    public interface IFileOpenPickerContinuable
    {
        void ContinueFileOpenPicker(FileOpenPickerContinuationEventArgs args);
    }
#endif
}