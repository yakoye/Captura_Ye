using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using Captura.Models;
using Captura.ViewModels;

namespace Captura
{
    public partial class MainWindow
    {
        public static MainWindow Instance { get; private set; }

        readonly MainWindowHelper _helper;
        readonly ReviewWindowLayout _windowLayout = new ReviewWindowLayout();
        bool _expandedLayout;

        public MainWindow()
        {
            Instance = this;
            
            InitializeComponent();
            StateChanged += (sender, e) => UpdateMaximizeButton();

            _helper = ServiceProvider.Get<MainWindowHelper>();

            _helper.MainViewModel.Init(!App.CmdOptions.NoPersist, !App.CmdOptions.Reset);

            _helper.HotkeySetup.Setup();

            _helper.TimerModel.Init();
            Review.LiveRequested += ShowLivePreview;
            Review.ReviewRequested += () => { PreviewTabs.SelectedItem = ReviewTab; RecordingOptions.IsExpanded = false; };
            _helper.RecordingViewModel.PropertyChanged += async (sender, e) => {
                if (e.PropertyName == nameof(RecordingViewModel.LastSavedFile)) {
                    var file = _helper.RecordingViewModel.LastSavedFile;
                    if (System.IO.File.Exists(file) && new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".gif", ".m4v" }.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant())) {
                        _helper.Settings.UI.Expanded = true;
                        await Review.OpenMediaAsync(file);
                    }
                }
            };
            Closed += (sender, e) => Review.Dispose();

            Loaded += async (Sender, Args) =>
            {
                ApplyLayout(_helper.Settings.UI.Expanded);
                RepositionWindowIfOutside();

                ServiceProvider.Get<WebcamPage>().SetupPreview();

                _helper.HotkeySetup.ShowUnregistered();
                if (!string.IsNullOrWhiteSpace(App.CmdOptions.Review)) {
                    _helper.Settings.UI.Expanded = true;
                    await Review.OpenMediaAsync(App.CmdOptions.Review);
                }
            };

            if (App.CmdOptions.Tray || _helper.Settings.Tray.MinToTrayOnStartup)
                Hide();

            Closing += (Sender, Args) =>
            {
                if (!TryExit())
                    Args.Cancel = true;
            };

            // Register to bring this instance to foreground when other instances are launched.
            SingleInstanceManager.StartListening(WakeApp);
        }

        public void ShowLivePreview()
        {
            if (PreviewTabs.SelectedItem == ReviewTab) Review.Suspend();
            PreviewTabs.SelectedItem = LiveTab;
        }
        void PreviewTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, PreviewTabs) || Review == null) return;
            if (PreviewTabs.SelectedItem == LiveTab) { Review.Suspend(); RecordingOptions.IsExpanded = true; }
            else Review.ResumePreview();
        }
        void Main_Expanded(object sender, RoutedEventArgs e)
        {
            if (IsLoaded && ReferenceEquals(e.OriginalSource, MainExpander)) ApplyLayout(true);
        }
        void Main_Collapsed(object sender, RoutedEventArgs e)
        {
            if (IsLoaded && ReferenceEquals(e.OriginalSource, MainExpander)) ApplyLayout(false);
        }
        void ApplyLayout(bool expanded)
        {
            var area = SystemParameters.WorkArea;
            if (!expanded) Review.Suspend();
            _windowLayout.Apply(this, expanded, new System.Windows.Size(area.Width, area.Height));
            _expandedLayout = expanded;
            MaximizeWindowButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            UpdateMaximizeButton();
        }

        void WakeApp()
        {
            Dispatcher.Invoke(() =>
            {
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }

                Activate();
            });
        }

        void RepositionWindowIfOutside()
        {
            // Window dimensions taking care of DPI
            var rect = new RectangleF((float) Left,
                (float) Top,
                (float) ActualWidth,
                (float) ActualHeight).ApplyDpi();
            
            if (!Screen.AllScreens.Any(M => M.Bounds.Contains(rect)))
            {
                Left = 50;
                Top = 50;
            }
        }

        void Grid_PreviewMouseLeftButtonDown(object Sender, MouseButtonEventArgs Args)
        {
            if (Args.ClickCount == 2 && _expandedLayout) { MaximizeButton_Click(Sender, Args); Args.Handled = true; return; }
            DragMove();

            Args.Handled = true;
        }

        void MinButton_Click(object Sender, RoutedEventArgs Args) => SystemCommands.MinimizeWindow(this);
        void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_expandedLayout) return;
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }
        void UpdateMaximizeButton()
        {
            if (MaximizeWindowButton == null) return;
            var maximized = WindowState == WindowState.Maximized;
            MaximizeWindowButton.ToolTip = maximized ? "还原窗口" : "最大化窗口";
            System.Windows.Automation.AutomationProperties.SetName(MaximizeWindowButton, (string)MaximizeWindowButton.ToolTip);
            MaximizeWindowButton.IconData = (System.Windows.Media.Geometry)FindResource(maximized ? "PlayerIcon-window-restore" : "PlayerIcon-window-maximize");
        }

        void CloseButton_Click(object Sender, RoutedEventArgs Args)
        {
            if (_helper.Settings.Tray.MinToTrayOnClose)
            {
                Hide();
            }
            else Close();
        }

        void SystemTray_TrayMouseDoubleClick(object Sender, RoutedEventArgs Args)
        {
            if (Visibility == Visibility.Visible)
            {
                Hide();
            }
            else this.ShowAndFocus();
        }

        bool TryExit()
        {
            if (!_helper.RecordingViewModel.CanExit())
                return false;

            ServiceProvider.Dispose();

            return true;
        }

        void MenuExit_Click(object Sender, RoutedEventArgs Args) => Close();

        void HideButton_Click(object Sender, RoutedEventArgs Args) => Hide();

        void ShowMainWindow(object Sender, RoutedEventArgs E) => this.ShowAndFocus();
    }
}
