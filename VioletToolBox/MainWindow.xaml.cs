using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Data;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Forms;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using System.ComponentModel;
using Microsoft.Win32;
using SharpVectors.Converters;
using System.IO.Compression;
using System.Net.Sockets;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors = System.Windows.Input.Cursors;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPoint = System.Windows.Point;
using SmartTool;
using System.Security.Cryptography;

namespace WpfApp1
{
    using System.Windows.Data;
    using System.Globalization;

    public class EqualsToParameterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class ProgressBarIndicatorWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3 ||
                !TryToDouble(values[0], out double value) ||
                !TryToDouble(values[1], out double maximum) ||
                !TryToDouble(values[2], out double actualWidth) ||
                maximum <= 0 ||
                actualWidth <= 0)
            {
                return 0d;
            }

            double ratio = Math.Clamp(value / maximum, 0, 1);
            return actualWidth * ratio;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return targetTypes.Select(_ => Binding.DoNothing).ToArray();
        }

        private static bool TryToDouble(object value, out double result)
        {
            if (value is double d)
            {
                result = d;
                return true;
            }

            if (value is IConvertible convertible)
                return double.TryParse(convertible.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result);

            result = 0;
            return false;
        }
    }

    public sealed class SystemZoneLogTextBox : System.Windows.Controls.RichTextBox
    {
        private static readonly Regex TimestampRegex = new(
            @"^\[(?<timestamp>[^\]]+)\]\s*",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly System.Windows.Media.Brush TimestampBrush = CreateBrush(0x94, 0xA3, 0xB8);
        private static readonly System.Windows.Media.Brush BodyBrush = CreateBrush(0x33, 0x41, 0x55);
        private static readonly System.Windows.Media.Brush SecondaryBrush = CreateBrush(0x64, 0x74, 0x8B);
        private static readonly System.Windows.Media.Brush ActionBrush = CreateBrush(0x7C, 0x3A, 0xED);
        private static readonly System.Windows.Media.Brush SuccessBrush = CreateBrush(0x16, 0xA3, 0x4A);
        private static readonly System.Windows.Media.Brush ErrorBrush = CreateBrush(0xDC, 0x26, 0x26);
        private static readonly System.Windows.Media.Brush WarningBrush = CreateBrush(0xD9, 0x77, 0x06);
        private static readonly System.Windows.Media.Brush InfoBrush = CreateBrush(0x25, 0x63, 0xEB);

        private string _plainText = string.Empty;

        public string Text
        {
            get => _plainText;
            set
            {
                string nextText = value ?? string.Empty;
                bool isAppend = _plainText.Length > 0
                    && nextText.StartsWith(_plainText, StringComparison.Ordinal);

                if (isAppend)
                {
                    AppendStyledText(nextText.Substring(_plainText.Length));
                }
                else
                {
                    Document.Blocks.Clear();
                    AppendStyledText(nextText);
                }

                _plainText = nextText;
            }
        }

        public SystemZoneLogTextBox()
        {
            Document.PagePadding = new Thickness(0);
            Document.ColumnWidth = 10000;
            Document.ColumnGap = 0;
            Document.TextAlignment = TextAlignment.Left;
        }

        private void AppendStyledText(string text)
        {
            string normalizedText = text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');

            foreach (string line in normalizedText.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0, 0.5, 0, 0.5),
                    LineHeight = 19
                };

                Match timestampMatch = TimestampRegex.Match(line);
                string message = line;
                if (timestampMatch.Success)
                {
                    paragraph.Inlines.Add(new Run(timestampMatch.Value)
                    {
                        Foreground = TimestampBrush
                    });
                    message = line.Substring(timestampMatch.Length);
                }

                LogLineStyle style = GetLogLineStyle(message);
                paragraph.Inlines.Add(new Run(message)
                {
                    Foreground = style.Brush,
                    FontWeight = style.IsEmphasized ? FontWeights.SemiBold : FontWeights.Normal
                });
                Document.Blocks.Add(paragraph);
            }
        }

        private static LogLineStyle GetLogLineStyle(string message)
        {
            if (ContainsAny(message, "错误", "失败", "无法", "异常", "拒绝", "error", "failed", "denied"))
            {
                return new LogLineStyle(ErrorBrush, true);
            }

            if (ContainsAny(message, "警告", "请先", "未获取ROOT权限", "未检测到"))
            {
                return new LogLineStyle(WarningBrush, true);
            }

            if (ContainsAny(message, "成功", "完成", "已删除", "已新建", "已授予"))
            {
                return new LogLineStyle(SuccessBrush, true);
            }

            if (ContainsAny(message, "开始", "正在", "检查"))
            {
                return new LogLineStyle(ActionBrush, true);
            }

            if (ContainsAny(message, "源:", "目标:", "路径:", "详情:", "无需打开", "加载根目录"))
            {
                return new LogLineStyle(SecondaryBrush, false);
            }

            if (ContainsAny(message, "信息", "提示"))
            {
                return new LogLineStyle(InfoBrush, false);
            }

            return new LogLineStyle(BodyBrush, false);
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
        }

        private static System.Windows.Media.Brush CreateBrush(byte red, byte green, byte blue)
        {
            var brush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private readonly record struct LogLineStyle(System.Windows.Media.Brush Brush, bool IsEmphasized);
    }

    public class AppPackageItem
    {
        public bool IsSelected { get; set; }
        public string PackageName { get; set; } = "";
        public string AppName { get; set; } = "";
        public string Version { get; set; } = "";
    }

    public class StorageViewModel : INotifyPropertyChanged
    {
        private double _storageUsage;
        private double _memoryUsage;
        private double _totalStorage;
        private double _totalMemory;

        public double StorageUsage
        {
            get => _storageUsage;
            set
            {
                _storageUsage = value;
                OnPropertyChanged(nameof(StorageUsage));
                OnPropertyChanged(nameof(StorageText));
                OnPropertyChanged(nameof(StorageDashArray));
                OnPropertyChanged(nameof(StorageUsedGB));
            }
        }

        public double MemoryUsage
        {
            get => _memoryUsage;
            set
            {
                _memoryUsage = value;
                OnPropertyChanged(nameof(MemoryUsage));
                OnPropertyChanged(nameof(MemoryText));
                OnPropertyChanged(nameof(MemoryDashArray));
                OnPropertyChanged(nameof(MemoryUsedGB));
            }
        }

        public double StorageUsedGB => (_storage_usage_safe / 100.0) * _totalStorage;
        public double MemoryUsedGB => (_memory_usage_safe / 100.0) * _totalMemory;

        public string StorageText => _totalStorage > 0 ? $"{StorageUsedGB:F1}GB/{_totalStorage:F0}GB" : "--";
        public string MemoryText => _totalMemory > 0 ? $"{MemoryUsedGB:F1}GB/{_totalMemory:F0}GB" : "--";

        private const double CircleCircumference = 471.239;
        public double StrokeThickness => 12;
        private double CircumferenceUnits => CircleCircumference / StrokeThickness;
        public DoubleCollection StorageDashArray => new DoubleCollection 
        { 
            Math.Max(0, CircumferenceUnits * (_storage_usage_safe / 100.0)), 
            Math.Max(0, CircumferenceUnits * (1.0 - _storage_usage_safe / 100.0)) 
        };
        public DoubleCollection MemoryDashArray => new DoubleCollection 
        { 
            Math.Max(0, CircumferenceUnits * (_memory_usage_safe / 100.0)), 
            Math.Max(0, CircumferenceUnits * (1.0 - _memory_usage_safe / 100.0)) 
        };

        private double _storage_usage_safe => Math.Max(0, Math.Min(100, _storageUsage));
        private double _memory_usage_safe => Math.Max(0, Math.Min(100, _memoryUsage));

        public void SetTotalMemory(double gb)
        {
            _totalMemory = gb;
            OnPropertyChanged(nameof(MemoryText));
            OnPropertyChanged(nameof(MemoryUsedGB));
            OnPropertyChanged(nameof(MemoryDashArray));
        }

        public void SetTotalStorage(double gb)
        {
            _totalStorage = gb;
            OnPropertyChanged(nameof(StorageText));
            OnPropertyChanged(nameof(StorageUsedGB));
            OnPropertyChanged(nameof(StorageDashArray));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }




    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _CurrentView = "Home";
        public string CurrentView
        {
            get => _CurrentView;
            set
            {
                if (_CurrentView != value)
                {
                    _CurrentView = value;
                    OnPropertyChanged(nameof(CurrentView));
                }
            }
        }

        private StorageViewModel _storageViewModel;
        private DispatcherTimer? _storageTimer;

        private ObservableCollection<string> deviceSerials = new ObservableCollection<string>();
        public ObservableCollection<string> DeviceSerials
        {
            get => deviceSerials;
            set
            {
                deviceSerials = value;
                OnPropertyChanged(nameof(DeviceSerials));
            }
        }

        private DispatcherTimer? deviceStatusTimer;
        private int _deviceDetectionVersion = 0;
        private bool _isDeviceDetectionEnabled = true;
        private readonly SemaphoreSlim _deviceDetectionLock = new SemaphoreSlim(1, 1);

        private string currentView = "Home";
        private string currentFlashingPartition = "";
        private StringBuilder fastbootCompleteLog = new StringBuilder();
        private ObservableCollection<AppPackageItem> AppPackages = new ObservableCollection<AppPackageItem>();

        private string lastKernelVersion = "";
        private string lastBuildDate = "";
        private string lastCpuManufacturer = "";
        private string lastCpuCodeName = "";
        private string lastWindowsVersion = "";
        private string lastDeviceStatus = "";
        private string lastConnectionType = "";
        private string lastDeviceSerial = "";
        private string lastDeviceModel = "";
        private string lastDeviceCode = "";
        private string lastAndroidVersion = "";
        private string lastUnlockStatus = "";
        private string lastABPartition = "";
        private string lastSelinuxStatus = "";
        private string? _parsedXiaomiFlashScriptPath;
        private string[]? _parsedXiaomiFlashScriptLines;
        private IReadOnlyList<string> _parsedRawProgramPaths = Array.Empty<string>();
        private string? _parsedRawProgramDisplayText;

        private Process? scrcpyProcess = null;
        private bool isScrcpyStarting = false;
        private Window? scrcpyControlBarWindow = null;
        private DispatcherTimer? scrcpyControlBarTimer = null;
        private IntPtr scrcpyMainWindowHandle = IntPtr.Zero;
        private IntPtr scrcpyLocationChangeHook = IntPtr.Zero;
        private WinEventDelegate? scrcpyLocationChangeProc = null;
        private readonly ConcurrentQueue<string> screenMirrorLogQueue = new();
        private DispatcherTimer? screenMirrorLogFlushTimer = null;
        private int screenMirrorLogPumpActive = 0;
        private const int ScreenMirrorLogMaxCharacters = 1_000;
        private const int ScreenMirrorLogMaxBatchCharacters = 4_096;
        private readonly List<string> _mirrorTitleSelectionOrder = new();
        private bool isAutoMirrorEnabled = false;
        private DispatcherTimer? autoMirrorTimer;

        private XiaomiFlashProgressState? _xiaomiFlashProgressState;

        public MainWindow()
        {
            InitializeComponent();
            GuardianService.InitializeGuardian();

            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo2.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(iconPath));
                }
                else
                {
                    var sri = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/logo2.ico"));
                    if (sri?.Stream != null)
                    {
                        this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(sri.Stream);
                    }
                }
            }
            catch { }

            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _storageViewModel = new StorageViewModel();
            this.DataContext = this;
            if (StorageMemoryBorder != null)
            {
                StorageMemoryBorder.DataContext = _storageViewModel;
            }

            InitializeDeviceStatusMonitoring();
            MultiDeviceComboBox.ItemsSource = DeviceSerials;
            if (this.FindName("AppListDataGrid") is DataGrid appListDataGrid)
            {
                appListDataGrid.ItemsSource = AppPackages;
            }

            SwitchToActiveView("Home");

            _storageTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _storageTimer.Tick += async (s, e) => await RefreshStorageMemoryAsync(true);
            _storageTimer.Start();

            this.Loaded += async (s, e) => await RefreshStorageMemoryAsync();
            this.Loaded += MainWindow_Loaded;
            this.Activated += (s, e) => ClearScrcpyWindowTopMost();
            this.LocationChanged += (s, e) => UpdateScrcpyControlBarPosition();
            this.SizeChanged += (s, e) => UpdateScrcpyControlBarPosition();
            this.StateChanged += (s, e) =>
            {
                ClearScrcpyWindowTopMost();
                UpdateScrcpyControlBarPosition();
            };
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SwitchToActiveView("Home");
            if (this.FindName("HomeButton") is HandyControl.Controls.SideMenuItem homeItem)
            {
                homeItem.IsSelected = true;
            }
            if (this.FindName("LoadingOverlay") is Grid overlay && this.FindName("LoadingContent") is Grid content)
            {
                overlay.Visibility = Visibility.Collapsed;
                content.Visibility = Visibility.Collapsed;
            }
        }

        private bool _isClosingCleanedUp;
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isClosingCleanedUp)
            {
                // 关闭二次确认弹窗
                var confirmResult = System.Windows.MessageBox.Show(
                    this,
                    "确定要退出 Yuzaki 工具箱吗？",
                    "退出确认",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);

                if (confirmResult != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                _isClosingCleanedUp = true;

                try
                {
                    // 停止设备状态监控与自动投屏定时器
                    deviceStatusTimer?.Stop();
                    deviceStatusTimer = null;
                    StopAutoMirrorTimer();

                    // 通知内存守护程序：用户确认退出，将在主进程退出后清理 C:\Yuzaki Tool Box 目录并自毁
                    GuardianService.SignalExitConfirmed();

                    // 立即隐藏主窗口，不显示任何清理弹窗
                    this.Hide();

                    // 异步杀掉 adb/fastboot/scrcpy，不阻塞也不弹窗
                    _ = KillAllAdbAndFastbootProcesses();
                    _ = KillAllScrcpyProcesses();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"关闭退出处理异常: {ex.Message}");
                }

                // 完全关闭应用程序
                System.Windows.Application.Current.Shutdown();
            }
        }

        public void SwitchToActiveView(string activeViewName)
        {
            if (this.FindName("HomeView") is Grid homeView) homeView.Visibility = activeViewName == "Home" ? Visibility.Visible : Visibility.Collapsed;
            if (this.FindName("ScreenMirrorView") is Grid screenMirrorView) screenMirrorView.Visibility = activeViewName == "ScreenMirror" ? Visibility.Visible : Visibility.Collapsed;
            if (this.FindName("BasicFlashView") is Grid basicFlashView) basicFlashView.Visibility = activeViewName == "BasicFlash" ? Visibility.Visible : Visibility.Collapsed;
            if (this.FindName("EdlFlashView") is Grid edlFlashView) edlFlashView.Visibility = activeViewName == "EdlFlash" ? Visibility.Visible : Visibility.Collapsed;
            if (this.FindName("AppManagementView") is Grid appManagementView) appManagementView.Visibility = activeViewName == "AppManagement" ? Visibility.Visible : Visibility.Collapsed;
            if (this.FindName("PayloadView") is Grid payloadView) payloadView.Visibility = activeViewName == "Payload" ? Visibility.Visible : Visibility.Collapsed;

            var menuMap = new Dictionary<string, string>
            {
                ["Home"] = "HomeButton",
                ["ScreenMirror"] = "ScreenMirrorButton",
                ["BasicFlash"] = "BasicFlashButton",
                ["EdlFlash"] = "EdlFlashButton",
                ["AppManagement"] = "AppManagementButton",
                ["Payload"] = "PayloadButton"
            };

            if (menuMap.TryGetValue(activeViewName, out string? targetBtnName))
            {
                var allItems = new[] { HomeButton, ScreenMirrorButton, BasicFlashButton, EdlFlashButton, AppManagementButton, PayloadButton };
                foreach (var item in allItems)
                {
                    if (item != null)
                    {
                        item.IsSelected = (item.Name == targetBtnName);
                    }
                }
            }

            UpdateButtonStates(activeViewName);
            currentView = activeViewName;
        }

        private void UpdateButtonStates(string activeView)
        {
            CurrentView = activeView;
            currentView = activeView;
        }

        private void ClearOtherSideMenuItemsSelection(HandyControl.Controls.SideMenuItem selectedItem)
        {
            var allItems = new HandyControl.Controls.SideMenuItem[]
            {
                HomeButton, ScreenMirrorButton, BasicFlashButton,
                EdlFlashButton, AppManagementButton, PayloadButton
            };

            foreach (var item in allItems)
            {
                if (item != null && item != selectedItem)
                {
                    item.IsSelected = false;
                }
            }
        }

        private void SideMenu_SelectionChanged(object sender, HandyControl.Data.FunctionEventArgs<object> e)
        {
            if (e.Info is HandyControl.Controls.SideMenuItem item)
            {
                ClearOtherSideMenuItemsSelection(item);

                switch (item.Name)
                {
                    case "HomeButton": SwitchToActiveView("Home"); break;
                    case "ScreenMirrorButton": SwitchToActiveView("ScreenMirror"); break;
                    case "BasicFlashButton": SwitchToActiveView("BasicFlash"); break;
                    case "EdlFlashButton": SwitchToActiveView("EdlFlash"); break;
                    case "AppManagementButton": SwitchToActiveView("AppManagement"); break;
                    case "PayloadButton": SwitchToActiveView("Payload"); break;
                }
            }
        }

        private void DirectItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is HandyControl.Controls.SideMenuItem item)
            {
                ClearOtherSideMenuItemsSelection(item);
                item.IsSelected = true;

                switch (item.Name)
                {
                    case "HomeButton": SwitchToActiveView("Home"); break;
                    case "ScreenMirrorButton": SwitchToActiveView("ScreenMirror"); break;
                    case "BasicFlashButton": SwitchToActiveView("BasicFlash"); break;
                    case "EdlFlashButton": SwitchToActiveView("EdlFlash"); break;
                    case "AppManagementButton": SwitchToActiveView("AppManagement"); break;
                    case "PayloadButton": SwitchToActiveView("Payload"); break;
                }
            }
        }

        private void SideMenuScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void HomeButton_Click(object sender, RoutedEventArgs? e) => SwitchToActiveView("Home");
        private void ScreenMirrorButton_Click(object sender, RoutedEventArgs? e) => SwitchToActiveView("ScreenMirror");
        private void BasicFlashButton_Click(object sender, RoutedEventArgs? e) => SwitchToActiveView("BasicFlash");
        private void EdlFlashButton_Click(object sender, RoutedEventArgs? e) => SwitchToActiveView("EdlFlash");
        private void AppManagementButton_Click(object sender, RoutedEventArgs? e) => SwitchToActiveView("AppManagement");

        private static void SetLocalizedText(TextBlock? textBlock, string rawText)
        {
            if (textBlock != null)
            {
                textBlock.Text = rawText;
            }
        }

        private async Task TryIncrementOpenCountAsync()
        {
            await Task.CompletedTask;
        }

        private static string ApplyViolettoolSignIfNeeded(string url) => url;

        private static string CleanLink(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var s = value.Trim().Trim('`', '"', '\'', ' ');
            return s.Trim();
        }

        private static bool IsMeizuDownloadEntryLink(string url)
        {
            url = CleanLink(url);
            return !string.IsNullOrWhiteSpace(url) &&
                   url.Contains("flyme.com/zh/download?key=", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> BuildMeizuRequestHeaders(string referer)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            referer = CleanLink(referer);
            if (!string.IsNullOrWhiteSpace(referer))
            {
                headers["Referer"] = referer;
            }
            headers["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SmartTool";
            return headers;
        }

        private static string NormalizeUrlForRequest(string url)
        {
            url = CleanLink(url);
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.ToString();
            var schemeSepIndex = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeSepIndex < 0) return url;
            var pathStart = url.IndexOf('/', schemeSepIndex + 3);
            if (pathStart < 0) return url;
            var basePart = url.Substring(0, pathStart);
            var rest = url.Substring(pathStart);
            string pathPart;
            string queryPart = string.Empty;
            var queryIndex = rest.IndexOf('?', StringComparison.Ordinal);
            if (queryIndex >= 0)
            {
                pathPart = rest.Substring(0, queryIndex);
                queryPart = rest.Substring(queryIndex);
            }
            else
            {
                pathPart = rest;
            }
            var escapedPath = string.Join("/", pathPart.Split(new[] { '/' }, StringSplitOptions.None).Select(Uri.EscapeDataString));
            return basePart + escapedPath + queryPart;
        }

        private async Task<string> ExecuteAdbCommandWithOutput(string command, CancellationToken cancellationToken = default)
        {
            Process? process = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string adbPath = GetToolPath("adb.exe");
                string selectedSerial = GetSelectedDeviceSerial();
                string arguments = string.IsNullOrWhiteSpace(selectedSerial) ? command : $"-s {selectedSerial} {command}";

                var startInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = File.Exists(adbPath) ? (Path.GetDirectoryName(adbPath) ?? AppDomain.CurrentDomain.BaseDirectory) : AppDomain.CurrentDomain.BaseDirectory
                };

                process = Process.Start(startInfo);
                if (process == null) return "Error: Process could not be started.";

                Task<string> outputTask;
                Task<string> errorTask;
                using (var outputReader = new StreamReader(process.StandardOutput.BaseStream, new UTF8Encoding(false), true))
                using (var errorReader = new StreamReader(process.StandardError.BaseStream, new UTF8Encoding(false), true))
                {
                    outputTask = outputReader.ReadToEndAsync();
                    errorTask = errorReader.ReadToEndAsync();
                    await process.WaitForExitAsync(cancellationToken);
                    await Task.WhenAll(outputTask, errorTask);
                }

                string output = await outputTask;
                string error = await errorTask;
                return string.Join(Environment.NewLine, new[] { output.Trim(), error.Trim() }.Where(text => !string.IsNullOrWhiteSpace(text)));
            }
            catch (OperationCanceledException)
            {
                try { if (process != null && !process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                throw;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
            finally
            {
                process?.Dispose();
            }
        }

        private async Task KillAllAdbAndFastbootProcesses()
        {
            try
            {
                var adbProcesses = Process.GetProcessesByName("adb");
                foreach (var process in adbProcesses)
                {
                    try { process.Kill(); await process.WaitForExitAsync(); } catch { }
                    finally { process.Dispose(); }
                }

                var fastbootProcesses = Process.GetProcessesByName("fastboot");
                foreach (var process in fastbootProcesses)
                {
                    try { process.Kill(); await process.WaitForExitAsync(); } catch { }
                    finally { process.Dispose(); }
                }
            }
            catch { }
        }

        private async Task KillAllScrcpyProcesses()
        {
            try
            {
                var scrcpyProcesses = Process.GetProcessesByName("scrcpy");
                foreach (var process in scrcpyProcesses)
                {
                    try { process.Kill(); await process.WaitForExitAsync(); } catch { }
                    finally { process.Dispose(); }
                }
            }
            catch { }
        }

        private async Task KillAllFastbootProcesses()
        {
            try
            {
                var fastbootProcesses = Process.GetProcessesByName("fastboot");
                foreach (var process in fastbootProcesses)
                {
                    try { process.Kill(); await process.WaitForExitAsync(); } catch { }
                    finally { process.Dispose(); }
                }
            }
            catch { }
        }

        private void UpdateTransferRateText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                if (bootflash != null)
                {
                    bootflash.Tag = text;
                }
                if (this.FindName("TransferRateTextBlock") is TextBlock trTb)
                {
                    trTb.Text = text;
                }
            });
        }

        private void StartScreenMirrorLogPump()
        {
            if (screenMirrorLogFlushTimer == null)
            {
                screenMirrorLogFlushTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                screenMirrorLogFlushTimer.Tick += (s, e) => FlushScreenMirrorLogQueue();
            }
            screenMirrorLogFlushTimer.Start();
        }

        private void AppendToLogTextBox(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            screenMirrorLogQueue.Enqueue(message);
            if (Interlocked.CompareExchange(ref screenMirrorLogPumpActive, 1, 0) != 0) return;

            if (Dispatcher.CheckAccess())
            {
                StartScreenMirrorLogPump();
            }
            else
            {
                try { Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(StartScreenMirrorLogPump)); }
                catch { Interlocked.Exchange(ref screenMirrorLogPumpActive, 0); }
            }
        }

        private void FlushScreenMirrorLogQueue()
        {
            var batch = new StringBuilder();
            while (batch.Length < ScreenMirrorLogMaxBatchCharacters && screenMirrorLogQueue.TryDequeue(out string? message))
            {
                batch.Append(message);
            }

            var logTextBox = this.FindName("ScreenMirrorLogTextBox") as System.Windows.Controls.TextBox;
            if (logTextBox != null && batch.Length > 0)
            {
                string batchText = batch.ToString();
                if (logTextBox.Text.Length + batchText.Length <= ScreenMirrorLogMaxCharacters)
                {
                    logTextBox.AppendText(batchText);
                }
                else
                {
                    string combined = logTextBox.Text + batchText;
                    int startIndex = Math.Max(0, combined.Length - ScreenMirrorLogMaxCharacters);
                    int firstLineBreak = combined.IndexOf('\n', startIndex);
                    if (firstLineBreak >= startIndex && firstLineBreak < combined.Length - 1)
                    {
                        startIndex = firstLineBreak + 1;
                    }
                    logTextBox.Text = combined.Substring(startIndex);
                    logTextBox.CaretIndex = logTextBox.Text.Length;
                }
                logTextBox.ScrollToEnd();
            }

            if (!screenMirrorLogQueue.IsEmpty) return;
            screenMirrorLogFlushTimer?.Stop();
            Interlocked.Exchange(ref screenMirrorLogPumpActive, 0);

            if (!screenMirrorLogQueue.IsEmpty && Interlocked.CompareExchange(ref screenMirrorLogPumpActive, 1, 0) == 0)
            {
                StartScreenMirrorLogPump();
            }
        }

        private void CopyTextBlockContent(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                string text = textBlock.Text;
                if (!string.IsNullOrEmpty(text) && text != "--" && text != "未知" && text != "单击复制")
                {
                    try
                    {
                        System.Windows.Clipboard.SetText(text);
                        ShowCopyToast();
                    }
                    catch (Exception ex)
                    {
                        AddLogMessage("错误", $"复制到剪贴板失败: {ex.Message}");
                    }
                }
            }
        }

        private string GetTextFromTextBlock(TextBlock tb)
        {
            if (tb == null) return string.Empty;
            var text = tb.Text;
            if (string.IsNullOrEmpty(text) && tb.Inlines != null)
            {
                text = string.Concat(tb.Inlines.OfType<Run>().Select(r => r.Text));
            }
            return text ?? string.Empty;
        }

        private void SaveDeviceInfoText_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var serial = GetTextFromTextBlock(DeviceSerialText).Trim();
            if (string.IsNullOrWhiteSpace(serial) || serial == "--") serial = "--";
            foreach (var ch in IOPath.GetInvalidFileNameChars())
            {
                serial = serial.Replace(ch, '_');
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = serial + ".txt"
            };
            var result = dlg.ShowDialog();
            if (result == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("设备状态: " + GetTextFromTextBlock(DeviceStatusText));
                sb.AppendLine("版本信息: " + GetTextFromTextBlock(VersionInfoText));
                sb.AppendLine("连接类型: " + GetTextFromTextBlock(ConnectionTypeText));
                sb.AppendLine("CPU厂家: " + GetTextFromTextBlock(CpuManufacturerText));
                sb.AppendLine("设备序列号: " + GetTextFromTextBlock(DeviceSerialText));
                sb.AppendLine("设备名称: " + GetTextFromTextBlock(DeviceModelText));
                sb.AppendLine("CPU名称: " + GetTextFromTextBlock(CpuNameText));
                sb.AppendLine("设备代号: " + GetTextFromTextBlock(DeviceCodeText));
                sb.AppendLine("操作系统: " + GetTextFromTextBlock(WindowsVersionText));
                sb.AppendLine("安卓版本: " + GetTextFromTextBlock(AndroidVersionText));
                sb.AppendLine("解锁状态: " + GetTextFromTextBlock(UnlockStatusText));
                sb.AppendLine("A/B分区: " + GetTextFromTextBlock(ABPartitionText));
                sb.AppendLine("内核版本: " + GetTextFromTextBlock(KernelVersionText));
                sb.AppendLine("构建日期: " + GetTextFromTextBlock(BuildDateText));
                sb.AppendLine("CPU代号: " + GetTextFromTextBlock(CpuCodeNameText));
                IOFile.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            }
        }

        private void ShowCopyToast()
        {
            if (this.FindName("CopyToastBorder") is Border border)
            {
                var anim = this.Resources["FadeInOutAnimation"] as System.Windows.Media.Animation.Storyboard;
                border.Visibility = Visibility.Visible;
                anim?.Begin();
            }
        }

        private void BitrateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (this.FindName("BitrateValueText") is TextBlock bitrateValueText)
            {
                bitrateValueText.Text = ((int)e.NewValue).ToString();
            }
        }

        private void MaxFpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (this.FindName("MaxFpsValueText") is TextBlock maxFpsValueText)
            {
                maxFpsValueText.Text = ((int)e.NewValue).ToString();
            }
        }

        private void ScreenMirrorLogTextBox_TextChanged(object sender, TextChangedEventArgs e) { }
        private void AutoRebootCheckBox_Checked(object sender, RoutedEventArgs e) { }
        private void XiaomiFlashScriptPathTextBox_TextChanged(object sender, TextChangedEventArgs e) { }

        private void CompleteWipeCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.IsChecked == true)
            {
                if (this.FindName("KeepDataCheckBox") is System.Windows.Controls.CheckBox keepData) keepData.IsChecked = false;
                if (this.FindName("WipeAndLockBLCheckBox") is System.Windows.Controls.CheckBox lockBl) lockBl.IsChecked = false;
            }
        }

        private void KeepDataCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.IsChecked == true)
            {
                if (this.FindName("CompleteWipeCheckBox") is System.Windows.Controls.CheckBox completeWipe) completeWipe.IsChecked = false;
                if (this.FindName("WipeAndLockBLCheckBox") is System.Windows.Controls.CheckBox lockBl) lockBl.IsChecked = false;
            }
        }

        private void WipeAndLockBLCheckBox_Checked_1(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.IsChecked == true)
            {
                if (this.FindName("CompleteWipeCheckBox") is System.Windows.Controls.CheckBox completeWipe) completeWipe.IsChecked = false;
                if (this.FindName("KeepDataCheckBox") is System.Windows.Controls.CheckBox keepData) keepData.IsChecked = false;
            }
        }

        private void FlashLogTextBox_TextChanged_2(object sender, TextChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                textBox.ScrollToEnd();
            }
        }

        private void CmdButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string platformToolsPath = Path.Combine(baseDir, "platform-tools");
                string workDir = Directory.Exists(platformToolsPath) ? platformToolsPath : baseDir;

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k \"set PATH={workDir};%PATH% & title Yuzaki工具箱 - 命令行终端 & cd /d \"{workDir}\" & echo ======================================== & echo   欢迎使用 Yuzaki 工具箱 命令行终端 & echo   已自动配置 platform-tools 环境变量 & echo   可直接运行 adb 或 fastboot 命令 & echo ======================================== & echo.\"",
                    UseShellExecute = true,
                    WorkingDirectory = workDir
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ShowMessage($"启动命令行失败: {ex.Message}");
            }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button button)
                {
                    string action = button.Content?.ToString() ?? "";
                    if (action.Contains("开始检测"))
                    {
                        HandleStartDeviceDetection();
                    }
                    else if (action.Contains("停止检测"))
                    {
                        HandleStopDeviceDetection();
                    }
                }
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"按钮操作异常: {ex.Message}");
            }
        }

        private void BatteryControl_Loaded(object sender, RoutedEventArgs e) { }
        private void LinkGuideTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { }
        private void ConnectionGuideTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { }
        private void UpdatePartitionButtonStates() { }
        private void UpdateXiaomiScriptOnlyOptionsState() { }
        private string GetSelectedDeviceSerial()
        {
            return Dispatcher.Invoke(() =>
            {
                if (MultiDeviceComboBox.SelectedItem != null)
                {
                    string selectedText = MultiDeviceComboBox.SelectedItem.ToString();
                    if (selectedText.Contains(" ("))
                    {
                        return selectedText.Substring(0, selectedText.IndexOf(" ("));
                    }
                    return selectedText;
                }
                return "";
            });
        }

        private static long ParseMemField(string text, string key)
        {
            var m = Regex.Match(text, key + @"\s*:\s*(\d+)\s*kB", RegexOptions.Multiline);
            return m.Success ? long.Parse(m.Groups[1].Value) : 0;
        }

        private async Task RefreshStorageMemoryAsync(bool silent = false)
        {
            int detectionVersion = _deviceDetectionVersion;
            if (!IsDeviceDetectionCycleCurrent(detectionVersion))
            {
                return;
            }

            try
            {
                string adbPath = GetToolPath("adb.exe");
                if (string.IsNullOrEmpty(adbPath))
                {
                    if (!silent) ShowMessage("未找到adb.exe");
                    return;
                }

                string selectedSerial = GetSelectedDeviceSerial();
                string serialArg = string.IsNullOrEmpty(selectedSerial) ? string.Empty : $"-s {selectedSerial} ";

                // 1. 获取内存信息
                var memTask = GetCommandOutput(adbPath, serialArg + "shell cat /proc/meminfo");
                // 2. 优先以 1K 块精确查询 /data 分区使用情况（兼容所有 Android 版本）
                var dfDataTask = GetCommandOutput(adbPath, serialArg + "shell df -k /data");

                var mem = await memTask;
                var dfData = await dfDataTask;

                if (!IsDeviceDetectionCycleCurrent(detectionVersion))
                {
                    return;
                }

                long totalKb = ParseMemField(mem, "MemTotal");
                long availKb = ParseMemField(mem, "MemAvailable");
                if (availKb <= 0)
                {
                    long freeKb = ParseMemField(mem, "MemFree");
                    long cachedKb = ParseMemField(mem, "Cached");
                    availKb = freeKb + cachedKb;
                }

                double memTotalGb = totalKb > 0 ? (totalKb / 1024.0 / 1024.0) : 0;
                double memUsedPct = (totalKb > 0) ? Math.Max(0, Math.Min(100, 100.0 - (availKb * 1.0 / totalKb) * 100.0)) : 0;

                // 标称运存容量对齐（例如 10.8GB 内核实际对应 12GB 物理内存）
                double nominalRam = memTotalGb;
                if (memTotalGb >= 1.5 && memTotalGb <= 2.5) nominalRam = 2;
                else if (memTotalGb > 2.5 && memTotalGb <= 3.5) nominalRam = 3;
                else if (memTotalGb > 3.5 && memTotalGb <= 4.5) nominalRam = 4;
                else if (memTotalGb > 4.5 && memTotalGb <= 6.5) nominalRam = 6;
                else if (memTotalGb > 6.5 && memTotalGb <= 9.0) nominalRam = 8;
                else if (memTotalGb > 9.0 && memTotalGb <= 13.0) nominalRam = 12;
                else if (memTotalGb > 13.0 && memTotalGb <= 18.0) nominalRam = 16;
                else if (memTotalGb > 18.0 && memTotalGb <= 26.0) nominalRam = 24;
                else if (memTotalGb > 26.0 && memTotalGb <= 34.0) nominalRam = 32;

                // 解析存储空间
                double storageTotalGb = 0;
                double storageUsedPct = 0;
                bool parsedStorage = false;

                if (!string.IsNullOrWhiteSpace(dfData))
                {
                    var lines = dfData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var lt = line.Trim();
                        if (lt.StartsWith("Filesystem", StringComparison.OrdinalIgnoreCase)) continue;

                        var tokens = Regex.Split(lt, @"\s+");
                        if (tokens.Length >= 4 && long.TryParse(tokens[1], out long total1k) && long.TryParse(tokens[2], out long used1k) && total1k > 0)
                        {
                            storageUsedPct = (used1k * 100.0) / total1k;
                            // 十进制标称存储（例如 500,432,876 kB -> 512 GB）
                            double decimalGb = (total1k * 1024.0) / 1e9;
                            double nominalRom = decimalGb;
                            if (decimalGb >= 14 && decimalGb <= 18) nominalRom = 16;
                            else if (decimalGb > 18 && decimalGb <= 36) nominalRom = 32;
                            else if (decimalGb > 36 && decimalGb <= 72) nominalRom = 64;
                            else if (decimalGb > 72 && decimalGb <= 140) nominalRom = 128;
                            else if (decimalGb > 140 && decimalGb <= 280) nominalRom = 256;
                            else if (decimalGb > 280 && decimalGb <= 550) nominalRom = 512;
                            else if (decimalGb > 550 && decimalGb <= 1100) nominalRom = 1024;
                            else if (decimalGb > 1100 && decimalGb <= 2200) nominalRom = 2048;

                            storageTotalGb = nominalRom;
                            parsedStorage = true;
                            break;
                        }
                    }
                }

                // 备用 fallback: 查询 df -h 并解析
                if (!parsedStorage)
                {
                    var dfh = await GetCommandOutput(adbPath, serialArg + "shell df -h");
                    string? targetLine = null;
                    foreach (var line in dfh.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var lt = line.Trim();
                        if (lt.EndsWith("/data") || lt.EndsWith("/storage/emulated") || lt.Contains(" /data") || lt.Contains("emulated"))
                        {
                            targetLine = lt;
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(targetLine))
                    {
                        var tokens = Regex.Split(targetLine, @"\s+");
                        if (tokens.Length >= 6)
                        {
                            var n = tokens.Length;
                            var usePctToken = tokens[n - 2];
                            var usedToken = tokens[n - 4];
                            var sizeToken = tokens[n - 5];

                            double ParseHumanGb(string t)
                            {
                                t = t.Trim();
                                if (string.IsNullOrEmpty(t)) return 0;
                                var m = Regex.Match(t, @"^([0-9]+(?:\.[0-9]+)?)\s*([KMGT])?", RegexOptions.IgnoreCase);
                                if (!m.Success) return 0;
                                var val = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                                var unit = m.Groups[2].Success ? m.Groups[2].Value.ToUpperInvariant() : "";
                                return unit switch
                                {
                                    "T" => val * 1024.0,
                                    "G" => val,
                                    "M" => val / 1024.0,
                                    "K" => val / (1024.0 * 1024.0),
                                    _ => val
                                };
                            }

                            storageTotalGb = ParseHumanGb(sizeToken);
                            var usedGb = ParseHumanGb(usedToken);
                            var pctMatch = Regex.Match(usePctToken, @"^([0-9]+)\%$");
                            if (pctMatch.Success)
                            {
                                storageUsedPct = double.Parse(pctMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                            }
                            else if (storageTotalGb > 0)
                            {
                                storageUsedPct = usedGb / storageTotalGb * 100.0;
                            }
                        }
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion))
                    {
                        return;
                    }

                    if (nominalRam > 0)
                    {
                        _storageViewModel.SetTotalMemory(nominalRam);
                        _storageViewModel.MemoryUsage = Math.Max(0, Math.Min(100, memUsedPct));
                    }
                    if (storageTotalGb > 0)
                    {
                        _storageViewModel.SetTotalStorage(storageTotalGb);
                        _storageViewModel.StorageUsage = Math.Max(0, Math.Min(100, storageUsedPct));
                    }
                });
            }
            catch
            {
            }
        }

        private void NavigateToUrl(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is TextBlock textBlock && textBlock.Tag is string url)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"打开链接失败: {ex.Message}");
            }
        }
        
        // 启动update.exe更新程序的方法
        private void StartUpdateProgram()
        {
            try
            {
                string updateExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "exe", "update.exe");
                
                if (File.Exists(updateExePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = updateExePath,
                        UseShellExecute = true
                    });
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
            }
        }
        
        // 加载提取的分区文件到DataGrid的方法
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void InitializeDeviceStatusMonitoring()
        {
            _isDeviceDetectionEnabled = true;
            unchecked
            {
                _deviceDetectionVersion++;
            }

            // 初始化设备状态监控定时器
            deviceStatusTimer = new DispatcherTimer            {
                Interval = TimeSpan.FromSeconds(3) // 每3秒检测一次
            };
            deviceStatusTimer.Tick += async (object? s, EventArgs e) => await CheckDeviceStatus();
            deviceStatusTimer.Start();
            
            _ = CheckDeviceStatus();
        }

        private bool IsDeviceDetectionCycleCurrent(int detectionVersion)
        {
            return _isDeviceDetectionEnabled && detectionVersion == _deviceDetectionVersion;
        }

        private async Task RefreshDeviceStatusImmediatelyAsync(bool terminateRunningTools)
        {
            bool detectionEnabled = _isDeviceDetectionEnabled;
            if (detectionEnabled)
            {
                // 立即使正在执行的旧检测失效，防止 Fastboot 阶段的结果在设备开机后回写主页。
                unchecked
                {
                    _deviceDetectionVersion++;
                }
            }

            if (terminateRunningTools)
            {
                await KillAllAdbAndFastbootProcesses();
            }

            if (!detectionEnabled || !_isDeviceDetectionEnabled)
            {
                return;
            }

            // 等待旧检测退出后直接执行一轮新检测，不依赖下一个三秒定时器周期。
            await CheckDeviceStatus(waitForCurrentDetection: true);
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private async void ReadAppListButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var readButton = sender as System.Windows.Controls.Button;
                if (readButton != null) readButton.IsEnabled = false;

                // 根据两个开关状态决定筛选：系统应用(-s) 或 第三方(-3)
                var thirdPartyToggle = this.FindName("AppListSwitchToggle") as System.Windows.Controls.Primitives.ToggleButton;
                var systemToggle = this.FindName("AppListSwitchToggle复制__C_") as System.Windows.Controls.Primitives.ToggleButton;
                string cmd =
                    (systemToggle?.IsChecked == true) ? "shell pm list packages -s" :
                    (thirdPartyToggle?.IsChecked == true) ? "shell pm list packages -3" :
                    "shell pm list packages";
                // 执行 adb 命令获取包列表
                string output = await ExecuteAdbCommandWithOutput(cmd);

                AppPackages.Clear();

                if (!string.IsNullOrWhiteSpace(output))
                {
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var rawLine in lines)
                    {
                        var line = rawLine.Trim();
                        if (string.IsNullOrEmpty(line)) continue;
                        if (line.StartsWith("package:")) line = line.Substring("package:".Length);
                        else if (line.StartsWith("package ")) line = line.Substring("package ".Length);

                        AppPackages.Add(new AppPackageItem { PackageName = line, AppName = line, Version = "", IsSelected = false });
                    }
                }

                if (AppPackages.Count == 0)
                {
                    AppPackages.Add(new AppPackageItem { PackageName = "未获取到包列表或设备未连接。", AppName = "未获取到包列表或设备未连接。", Version = "", IsSelected = false });
                }

                // 根据当前搜索关键词应用过滤（已改用 TextBox）
                var searchTextBox = this.FindName("AppPackageSearchComboBox") as System.Windows.Controls.TextBox;
                ApplyAppPackageFilter(searchTextBox?.Text ?? string.Empty);

                var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
                if (appListDataGrid != null)
                {
                    appListDataGrid.ItemsSource = AppPackages;
                    appListDataGrid.Items.Refresh();
                    if (AppPackages.Count > 0)
                    {
                        appListDataGrid.ScrollIntoView(AppPackages[0]);
                    }
                }

                // 在日志窗口显示读取到的应用数量
                int count = AppPackages.Count;
                if (systemToggle?.IsChecked == true)
                {
                    AppendAppManagementLog($"读取到 {count} 个系统应用");
                }
                else if (thirdPartyToggle?.IsChecked == true)
                {
                    AppendAppManagementLog($"读取到 {count} 个用户应用");
                }
                else
                {
                    AppendAppManagementLog($"读取到 {count} 个应用");
                }

                // 推送 aapt-arm-pie 到设备并读取应用名称与版本信息
                try
                {
                    AppendAppManagementLog("正在解析应用信息…");
                    // 可能的本地 aapt-arm-pie 路径
                    var possibleAaptPaths = new List<string>
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aapt-arm-pie"),
                        Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "aapt-arm-pie"),
                        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "SmartTool", "aapt-arm-pie")),
                        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "aapt-arm-pie")),
                        "C\\Users\\pcnb1\\Desktop\\Debug\\aapt-arm-pie"
                    };

                    string? aaptLocalPath = possibleAaptPaths.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
                    if (!string.IsNullOrEmpty(aaptLocalPath))
                    {
                        // 推送到设备临时目录并赋予执行权限
                        string pushResult = await ExecuteAdbCommandWithOutput($"push \"{aaptLocalPath}\" /data/local/tmp/aapt-arm-pie");
                        await ExecuteAdbCommandWithOutput("shell chmod 755 /data/local/tmp/aapt-arm-pie");

                        // 针对每个包解析名称与版本
                        for (int i = 0; i < AppPackages.Count; i++)
                        {
                            var item = AppPackages[i];
                            var pkg = item.PackageName?.Trim() ?? string.Empty;
                            // 跳过提示项或无效项
                            if (string.IsNullOrWhiteSpace(pkg) || pkg.Contains("未获取到") || pkg.StartsWith("读取失败") || pkg.StartsWith("Error"))
                                continue;

                            string pathOutput = await ExecuteAdbCommandWithOutput($"shell pm path {pkg}");
                            string deviceApkPath = string.Empty;
                            if (!string.IsNullOrWhiteSpace(pathOutput))
                            {
                                var pathLines = pathOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var raw in pathLines)
                                {
                                    var l = raw.Trim();
                                    if (l.StartsWith("package:"))
                                    {
                                        deviceApkPath = l.Substring("package:".Length);
                                        break;
                                    }
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(deviceApkPath))
                            {
                                string badgingOutput = await ExecuteAdbCommandWithOutput($"shell /data/local/tmp/aapt-arm-pie d badging {deviceApkPath}");

                                string parsedName = item.AppName;
                                string parsedVersion = item.Version;

                                try
                                {
                                    var mCn = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "application-label-zh-CN:'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    var mZh = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "application-label-zh:'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    var mDef = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "application-label:'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    var mVer = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "versionName='([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                                    if (mCn.Success) parsedName = mCn.Groups[1].Value;
                                    else if (mZh.Success) parsedName = mZh.Groups[1].Value;
                                    else if (mDef.Success) parsedName = mDef.Groups[1].Value;

                                    if (mVer.Success) parsedVersion = mVer.Groups[1].Value;
                                }
                                catch { /* 忽略解析异常 */ }

                                item.AppName = string.IsNullOrWhiteSpace(parsedName) ? item.AppName : parsedName;
                                item.Version = string.IsNullOrWhiteSpace(parsedVersion) ? item.Version : parsedVersion;

                                if (appListDataGrid != null && i % 10 == 0)
                                    appListDataGrid.Items.Refresh();
                            }
                        }

                        if (appListDataGrid != null)
                            appListDataGrid.Items.Refresh();

                        AppendAppManagementLog("应用名称与版本解析完成");
                    }
                    else
                    {
                        AppendAppManagementLog("未找到解析应用配置，跳过名称与版本解析");
                    }
                }
                catch (Exception ex2)
                {
                    AppendAppManagementLog($"解析应用信息时出现错误：{ex2.Message}");
                }
            }
            catch (Exception ex)
            {
                AppPackages.Clear();
                AppPackages.Add(new AppPackageItem { PackageName = $"读取失败: {ex.Message}", AppName = $"读取失败: {ex.Message}", Version = "", IsSelected = false });

                var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
                if (appListDataGrid != null)
                {
                    appListDataGrid.ItemsSource = AppPackages;
                    appListDataGrid.Items.Refresh();
                }
            }
            finally
            {
                var readButton = sender as System.Windows.Controls.Button;
                if (readButton != null) readButton.IsEnabled = true;
            }
        }

        // 全选/取消全选包名
        private void SelectAllAppPackagesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var item in AppPackages)
            {
                item.IsSelected = true;
            }
            var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
            if (appListDataGrid != null)
            {
                appListDataGrid.Items.Refresh();
            }
        }

        private void SelectAllAppPackagesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var item in AppPackages)
            {
                item.IsSelected = false;
            }
            var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
            if (appListDataGrid != null)
            {
                appListDataGrid.Items.Refresh();
            }
        }

        // 互斥：第三方开关被勾选时，关闭系统开关
        private void AppListThirdPartyToggle_Checked(object sender, RoutedEventArgs e)
        {
            var systemToggle = this.FindName("AppListSwitchToggle复制__C_") as System.Windows.Controls.Primitives.ToggleButton;
            if (systemToggle != null && systemToggle.IsChecked == true)
            {
                systemToggle.IsChecked = false;
            }
        }

        // 互斥：系统开关被勾选时，关闭第三方开关
        private void AppListSystemToggle_Checked(object sender, RoutedEventArgs e)
        {
            var thirdPartyToggle = this.FindName("AppListSwitchToggle") as System.Windows.Controls.Primitives.ToggleButton;
            if (thirdPartyToggle != null && thirdPartyToggle.IsChecked == true)
            {
                thirdPartyToggle.IsChecked = false;
            }
        }

        // 搜索框按键事件：根据关键字过滤包名
        private void AppPackageSearchComboBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                var textBox = sender as System.Windows.Controls.TextBox;
                string keyword = textBox?.Text ?? string.Empty;
                ApplyAppPackageFilter(keyword);
            }
            catch { }
        }

        // 在应用管理日志文本框中追加日志
        private void AppendAppManagementLog(string message)
        {
            try
            {
                var logBox = this.FindName("AppManagementLogTextBox") as System.Windows.Controls.TextBox;
                if (logBox != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
                        if (!string.IsNullOrEmpty(logBox.Text))
                        {
                            logBox.AppendText("\n");
                        }
                        logBox.AppendText(line);
                        logBox.ScrollToEnd();
                    });
                }
            }
            catch { }
        }

        // 卸载选中应用按钮点击事件：逐个卸载已勾选包名
        private async void UninstallSelectedAppsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedPackages = AppPackages
                    .Where(p => p.IsSelected && !string.IsNullOrWhiteSpace(p.PackageName))
                    .Select(p => p.PackageName.Trim())
                    .ToList();

                if (selectedPackages.Count == 0)
                {
                    AddLogMessage("警告", "未选择任何应用，请在列表中勾选需要卸载的包名。");
                    return;
                }

                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = false;

                AddLogMessage("信息", $"开始卸载 {selectedPackages.Count} 个已选应用...");
                AppendAppManagementLog($"需要卸载 {selectedPackages.Count} 个应用");

                foreach (var pkg in selectedPackages)
                {
                    AddLogMessage("信息", $"正在卸载: {pkg}");
                    string result = await ExecuteAdbCommandWithOutput($"shell pm uninstall {pkg}");

                    if (!string.IsNullOrEmpty(result) && result.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        AddLogMessage("成功", $"卸载成功: {pkg}");
                        AppendAppManagementLog($"卸载成功: {pkg}");
                        // 取消勾选，避免重复操作
                        var item = AppPackages.FirstOrDefault(x => x.PackageName.Equals(pkg, StringComparison.OrdinalIgnoreCase));
                        if (item != null) item.IsSelected = false;
                    }
                    else
                    {
                        AddLogMessage("错误", $"卸载失败: {pkg} - {result}");
                        AppendAppManagementLog($"卸载失败: {pkg} - {result}");
                    }
                }

                // 刷新 DataGrid 展示状态
                var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
                appListDataGrid?.Items.Refresh();

                AddLogMessage("成功", "已完成卸载操作。");
                AppendAppManagementLog("已完成卸载操作");

                if (button != null) button.IsEnabled = true;
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"卸载过程中发生异常: {ex.Message}");
                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = true;
            }
        }

        // 冻结选中应用按钮点击事件：逐个执行禁用并汇总结果
        private async void FreezeSelectedAppsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedPackages = AppPackages
                    .Where(p => p.IsSelected && !string.IsNullOrWhiteSpace(p.PackageName))
                    .Select(p => p.PackageName.Trim())
                    .ToList();

                if (selectedPackages.Count == 0)
                {
                    AddLogMessage("警告", "未选择任何应用，请在列表中勾选需要冻结的包名。");
                    return;
                }

                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = false;

                AddLogMessage("信息", $"开始冻结 {selectedPackages.Count} 个已选应用...");
                AppendAppManagementLog($"需要冻结 {selectedPackages.Count} 个应用");

                foreach (var pkg in selectedPackages)
                {
                    AddLogMessage("信息", $"正在冻结: {pkg}");
                    string result = await ExecuteAdbCommandWithOutput($"shell pm disable-user {pkg}");

                    // 记录命令返回，部分设备返回为空或不同信息，最终以汇总校验为准
                    if (!string.IsNullOrEmpty(result))
                    {
                        if (result.IndexOf("disabled-user", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            result.IndexOf("new state", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            result.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            AddLogMessage("成功", $"冻结命令返回成功: {pkg}");
                            AppendAppManagementLog($"冻结命令返回成功: {pkg}");
                        }
                        else
                        {
                            AddLogMessage("信息", $"冻结返回: {pkg} - {result}");
                            AppendAppManagementLog($"冻结返回: {pkg} - {result}");
                        }
                    }
                }

                // 汇总验证：读取已冻结列表，统计最终成功与失败
                string disabledOutput = await ExecuteAdbCommandWithOutput("shell pm list packages -d");
                var disabledSet = new HashSet<string>(
                    (disabledOutput ?? string.Empty)
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => l.StartsWith("package:"))
                        .Select(l => l.Substring("package:".Length))
                        .Where(p => !string.IsNullOrWhiteSpace(p)),
                    StringComparer.OrdinalIgnoreCase);

                int successCount = selectedPackages.Count(p => disabledSet.Contains(p));
                var failedPkgs = selectedPackages.Where(p => !disabledSet.Contains(p)).ToList();

                AddLogMessage("成功", $"已成功冻结 {successCount}/{selectedPackages.Count} 个应用");
                AppendAppManagementLog($"已成功冻结{successCount}/{selectedPackages.Count}个应用");

                if (failedPkgs.Count > 0)
                {
                    string failedList = string.Join(", ", failedPkgs);
                    AddLogMessage("错误", $"冻结失败的应用包名为：{failedList}");
                    AppendAppManagementLog($"冻结失败的应用包名为：{failedList}");
                }

                // 操作完成后取消勾选成功冻结的项，避免重复执行
                foreach (var pkg in selectedPackages.Where(p => disabledSet.Contains(p)))
                {
                    var item = AppPackages.FirstOrDefault(x => x.PackageName.Equals(pkg, StringComparison.OrdinalIgnoreCase));
                    if (item != null) item.IsSelected = false;
                }
                var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
                appListDataGrid?.Items.Refresh();

                if (button != null) button.IsEnabled = true;
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"冻结过程中发生异常: {ex.Message}");
                AppendAppManagementLog($"冻结过程中发生异常: {ex.Message}");
                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = true;
            }
        }

        // 解冻选中应用按钮点击事件：逐个启用并汇总结果
        private async void UnfreezeSelectedAppsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedPackages = AppPackages
                    .Where(p => p.IsSelected && !string.IsNullOrWhiteSpace(p.PackageName))
                    .Select(p => p.PackageName.Trim())
                    .ToList();

                if (selectedPackages.Count == 0)
                {
                    AddLogMessage("警告", "未选择任何应用，请在列表中勾选需要解冻的包名。");
                    return;
                }

                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = false;

                AddLogMessage("信息", $"开始解冻 {selectedPackages.Count} 个已选应用...");
                AppendAppManagementLog($"需要解冻 {selectedPackages.Count} 个应用");

                foreach (var pkg in selectedPackages)
                {
                    AddLogMessage("信息", $"正在解冻: {pkg}");
                    string result = await ExecuteAdbCommandWithOutput($"shell pm enable {pkg}");

                    if (!string.IsNullOrEmpty(result))
                    {
                        if (result.IndexOf("enabled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            result.IndexOf("new state", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            result.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            AddLogMessage("成功", $"解冻命令返回成功: {pkg}");
                            AppendAppManagementLog($"解冻命令返回成功: {pkg}");
                        }
                        else
                        {
                            AddLogMessage("信息", $"解冻返回: {pkg} - {result}");
                            AppendAppManagementLog($"解冻返回: {pkg} - {result}");
                        }
                    }
                }

                // 汇总验证：读取已冻结列表，统计最终成功与失败（成功即不再出现在禁用列表中）
                string disabledOutput = await ExecuteAdbCommandWithOutput("shell pm list packages -d");
                var disabledSet = new HashSet<string>(
                    (disabledOutput ?? string.Empty)
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => l.StartsWith("package:"))
                        .Select(l => l.Substring("package:".Length))
                        .Where(p => !string.IsNullOrWhiteSpace(p)),
                    StringComparer.OrdinalIgnoreCase);

                int successCount = selectedPackages.Count(p => !disabledSet.Contains(p));
                var failedPkgs = selectedPackages.Where(p => disabledSet.Contains(p)).ToList();

                AddLogMessage("成功", $"已成功解冻 {successCount}/{selectedPackages.Count} 个应用");
                AppendAppManagementLog($"已成功解冻{successCount}/{selectedPackages.Count}个应用");

                if (failedPkgs.Count > 0)
                {
                    string failedList = string.Join(", ", failedPkgs);
                    AddLogMessage("错误", $"解冻失败的应用包名为：{failedList}");
                    AppendAppManagementLog($"解冻失败的应用包名为：{failedList}");
                }

                // 操作完成后取消勾选成功解冻的项，避免重复执行
                foreach (var pkg in selectedPackages.Where(p => !disabledSet.Contains(p)))
                {
                    var item = AppPackages.FirstOrDefault(x => x.PackageName.Equals(pkg, StringComparison.OrdinalIgnoreCase));
                    if (item != null) item.IsSelected = false;
                }
                var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
                appListDataGrid?.Items.Refresh();

                if (button != null) button.IsEnabled = true;
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"解冻过程中发生异常: {ex.Message}");
                AppendAppManagementLog($"解冻过程中发生异常: {ex.Message}");
                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = true;
            }
        }

        // 查看冻结应用按钮点击事件：读取已禁用（冻结）的包名并统计数量
        private async void ViewDisabledAppsButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            if (button != null) button.IsEnabled = false;
            try
            {
                AppendAppManagementLog("正在读取冻结应用…");
                string output = await ExecuteAdbCommandWithOutput("shell pm list packages -d");

                var lines = (output ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var disabledPackages = lines
                    .Select(l => l.Trim())
                    .Where(l => l.StartsWith("package:"))
                    .Select(l => l.Substring("package:".Length))
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();

                // 用冻结应用填充数据网格绑定集合
                AppPackages.Clear();
                foreach (var pkg in disabledPackages)
                {
                    AppPackages.Add(new AppPackageItem { PackageName = pkg, AppName = pkg, Version = "", IsSelected = false });
                }

                if (AppPackages.Count == 0)
                {
                    AppPackages.Add(new AppPackageItem { PackageName = "未获取到冻结应用或设备未连接。", AppName = "未获取到冻结应用或设备未连接。", Version = "", IsSelected = false });
                }

                // 应用当前搜索关键字过滤
                var searchTextBox = this.FindName("AppPackageSearchComboBox") as System.Windows.Controls.TextBox;
                ApplyAppPackageFilter(searchTextBox?.Text ?? string.Empty);

                // 刷新DataGrid显示
                var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
                if (appListDataGrid != null)
                {
                    appListDataGrid.ItemsSource = AppPackages;
                    appListDataGrid.Items.Refresh();
                    if (AppPackages.Count > 0)
                    {
                        appListDataGrid.ScrollIntoView(AppPackages[0]);
                    }
                }

                // 推送 aapt-arm-pie 并解析冻结应用的名称与版本
                try
                {
                    AppendAppManagementLog("正在解析冻结应用信息…");
                    var possibleAaptPaths = new List<string>
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aapt-arm-pie"),
                        Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "aapt-arm-pie"),
                        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "SmartTool", "aapt-arm-pie")),
                        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "aapt-arm-pie")),
                        "C\\Users\\pcnb1\\Desktop\\Debug\\aapt-arm-pie"
                    };

                    string? aaptLocalPath = possibleAaptPaths.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
                    if (!string.IsNullOrEmpty(aaptLocalPath))
                    {
                        string pushResult = await ExecuteAdbCommandWithOutput($"push \"{aaptLocalPath}\" /data/local/tmp/aapt-arm-pie");
                        await ExecuteAdbCommandWithOutput("shell chmod 755 /data/local/tmp/aapt-arm-pie");

                        for (int i = 0; i < AppPackages.Count; i++)
                        {
                            var item = AppPackages[i];
                            var pkg = item.PackageName?.Trim() ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(pkg) || pkg.Contains("未获取到") || pkg.StartsWith("读取失败") || pkg.StartsWith("Error"))
                                continue;

                            string pathOutput = await ExecuteAdbCommandWithOutput($"shell pm path {pkg}");
                            string deviceApkPath = string.Empty;
                            if (!string.IsNullOrWhiteSpace(pathOutput))
                            {
                                var pathLines = pathOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var raw in pathLines)
                                {
                                    var l = raw.Trim();
                                    if (l.StartsWith("package:"))
                                    {
                                        deviceApkPath = l.Substring("package:".Length);
                                        break;
                                    }
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(deviceApkPath))
                            {
                                string badgingOutput = await ExecuteAdbCommandWithOutput($"shell /data/local/tmp/aapt-arm-pie d badging {deviceApkPath}");
                                string parsedName = item.AppName;
                                string parsedVersion = item.Version;

                                try
                                {
                                    var mCn = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "application-label-zh-CN:'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    var mZh = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "application-label-zh:'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    var mDef = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "application-label:'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    var mVer = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "versionName='([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                                    if (mCn.Success) parsedName = mCn.Groups[1].Value;
                                    else if (mZh.Success) parsedName = mZh.Groups[1].Value;
                                    else if (mDef.Success) parsedName = mDef.Groups[1].Value;

                                    if (mVer.Success) parsedVersion = mVer.Groups[1].Value;
                                }
                                catch { }

                                item.AppName = string.IsNullOrWhiteSpace(parsedName) ? item.AppName : parsedName;
                                item.Version = string.IsNullOrWhiteSpace(parsedVersion) ? item.Version : parsedVersion;

                                if (appListDataGrid != null && i % 10 == 0)
                                    appListDataGrid.Items.Refresh();
                            }
                        }

                        appListDataGrid?.Items.Refresh();
                        AppendAppManagementLog("冻结应用名称与版本解析完成");
                    }
                    else
                    {
                        AppendAppManagementLog("未找到本地 aapt-arm-pie，跳过冻结应用解析");
                    }
                }
                catch (Exception ex2)
                {
                    AppendAppManagementLog($"解析冻结应用信息时出现错误：{ex2.Message}");
                }

                AppendAppManagementLog($"读取到 {AppPackages.Count} 个已冻结应用");
            }
            catch (Exception ex)
            {
                // 显示错误信息到集合和日志
                AppPackages.Clear();
                AppPackages.Add(new AppPackageItem { PackageName = $"读取冻结应用失败: {ex.Message}", AppName = $"读取冻结应用失败: {ex.Message}", Version = "", IsSelected = false });
                var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
                if (appListDataGrid != null)
                {
                    appListDataGrid.ItemsSource = AppPackages;
                    appListDataGrid.Items.Refresh();
                }
                AppendAppManagementLog($"读取冻结应用失败: {ex.Message}");
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }

        // 提取APK安装包：选择保存目录，逐个解析路径并拉取到电脑后按包名重命名
        private async void ExtractApkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedPackages = AppPackages
                    .Where(p => p.IsSelected && !string.IsNullOrWhiteSpace(p.PackageName))
                    .Select(p => p.PackageName.Trim())
                    .ToList();

                if (selectedPackages.Count == 0)
                {
                    AddLogMessage("警告", "未选择任何应用，请在列表中勾选需要提取的包名。");
                    return;
                }

                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = false;

                // 选择保存目录
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = "选择保存APK的文件夹";
                    dialog.ShowNewFolderButton = true;
                    var result = dialog.ShowDialog();
                    if (result != System.Windows.Forms.DialogResult.OK)
                    {
                        AppendAppManagementLog("用户取消了保存路径选择");
                        if (button != null) button.IsEnabled = true;
                        return;
                    }

                    string saveDir = dialog.SelectedPath;
                    AppendAppManagementLog($"保存路径: {saveDir}");

                    int successCount = 0;
                    int failCount = 0;
                    foreach (var pkg in selectedPackages)
                    {
                        try
                        {
                            AppendAppManagementLog($"正在处理: {pkg}");
                            // 获取APK路径（可能返回多行，优先选择包含base.apk的行）
                            string pathOutput = await ExecuteAdbCommandWithOutput($"shell pm path {pkg}");
                            var lines = (pathOutput ?? string.Empty)
                                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(l => l.Trim())
                                .Where(l => l.StartsWith("package:"))
                                .Select(l => l.Substring("package:".Length))
                                .ToList();

                            string? deviceApkPath = lines.FirstOrDefault(l => l.EndsWith("/base.apk", StringComparison.OrdinalIgnoreCase))
                                                     ?? lines.FirstOrDefault();

                            if (string.IsNullOrWhiteSpace(deviceApkPath))
                            {
                                AppendAppManagementLog($"未找到安装包路径: {pkg} - {pathOutput}");
                                failCount++;
                                continue;
                            }

                            var item = AppPackages.FirstOrDefault(p => string.Equals(p.PackageName?.Trim(), pkg, StringComparison.OrdinalIgnoreCase));
                            string baseName = MakeSafeApkFileName(item?.AppName);
                            if (string.IsNullOrWhiteSpace(baseName)) baseName = MakeSafeApkFileName(pkg);
                            string destFile = System.IO.Path.Combine(saveDir, baseName + ".apk");
                            int suffix = 1;
                            while (System.IO.File.Exists(destFile))
                            {
                                destFile = System.IO.Path.Combine(saveDir, $"{baseName}({suffix}).apk");
                                suffix++;
                            }
                            AppendAppManagementLog($"拉取: {deviceApkPath} -> {destFile}");

                            string pullOutput = await ExecuteAdbCommandWithOutput($"pull \"{deviceApkPath}\" \"{destFile}\" ");

                            // 校验本地文件是否存在作为成功依据
                            if (System.IO.File.Exists(destFile))
                            {
                                AppendAppManagementLog($"提取成功: {pkg} -> {destFile}");
                                successCount++;
                                // 不自动取消勾选，避免用户后续继续其他操作；如需取消可改为 true
                            }
                            else
                            {
                                AppendAppManagementLog($"提取失败: {pkg} - {pullOutput}");
                                failCount++;
                            }
                        }
                        catch (Exception innerEx)
                        {
                            AppendAppManagementLog($"处理 {pkg} 失败: {innerEx.Message}");
                            failCount++;
                        }
                    }

                    AppendAppManagementLog($"提取完成，成功 {successCount}，失败 {failCount}");
                }
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"提取APK过程中发生异常: {ex.Message}");
            }
            finally
            {
                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = true;
            }
        }

        private static string MakeSafeApkFileName(string? name)
        {
            var n = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(n)) return string.Empty;
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var arr = n.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            var sanitized = new string(arr).Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(sanitized) ? string.Empty : sanitized;
        }

        // 小米冻结系统更新：卸载用户0的com.android.updater（保留数据）
        private async void FreezeMiuiUpdaterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = false;

                AppendAppManagementLog("正在冻结系统更新 (com.android.updater)…");
                string output = await ExecuteAdbCommandWithOutput("shell pm uninstall -k --user 0 com.android.updater");

                if (!string.IsNullOrEmpty(output) && output.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AppendAppManagementLog("冻结系统更新成功");
                    AddLogMessage("成功", "小米系统更新已冻结");
                }
                else
                {
                    AppendAppManagementLog($"冻结系统更新可能失败: {output}");
                    AddLogMessage("错误", $"冻结系统更新失败: {output}");
                }

                if (button != null) button.IsEnabled = true;
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"冻结系统更新异常: {ex.Message}");
            }
        }

        // 小米恢复系统更新：安装现有的com.android.updater到用户0
        private async void UnfreezeMiuiUpdaterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = false;

                AppendAppManagementLog("正在恢复系统更新 (com.android.updater)…");
                string output = await ExecuteAdbCommandWithOutput("shell cmd package install-existing com.android.updater");

                // 简单输出判断
                bool installed = (!string.IsNullOrEmpty(output) &&
                                  (output.IndexOf("installed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   output.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0));

                // 进一步验证：查询路径是否存在
                string pathCheck = await ExecuteAdbCommandWithOutput("shell pm path com.android.updater");
                bool hasPath = !string.IsNullOrWhiteSpace(pathCheck) && pathCheck.Contains("package:");

                if (installed || hasPath)
                {
                    AppendAppManagementLog("恢复系统更新成功");
                    AddLogMessage("成功", "小米系统更新已恢复");
                }
                else
                {
                    AppendAppManagementLog($"恢复系统更新可能失败: {output}");
                    AddLogMessage("错误", $"恢复系统更新失败: {output}");
                }

                if (button != null) button.IsEnabled = true;
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"恢复系统更新异常: {ex.Message}");
            }
        }

        // 清除应用数据：针对在表格中勾选的应用逐个执行 pm clear
        private async void ClearAppDataButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            if (button != null) button.IsEnabled = false;
            try
            {
                var selectedApps = AppPackages
                    .Where(p => p.IsSelected && !string.IsNullOrWhiteSpace(p.PackageName))
                    .ToList();

                if (selectedApps.Count == 0)
                {
                    AddLogMessage("警告", "未选择任何应用，请在列表中勾选需要清除数据的应用。");
                    return;
                }

                AppendAppManagementLog($"准备清除 {selectedApps.Count} 个应用的数据…");

                int total = selectedApps.Count;
                int success = 0;
                int failed = 0;

                for (int i = 0; i < total; i++)
                {
                    var app = selectedApps[i];
                    var pkg = app.PackageName.Trim();

                    try
                    {
                        AppendAppManagementLog($"[{i + 1}/{total}] 正在清除: {pkg}");
                        string output = await ExecuteAdbCommandWithOutput($"shell pm clear {pkg}");

                        bool ok = !string.IsNullOrEmpty(output) &&
                                  output.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (ok)
                        {
                            AppendAppManagementLog($"[{i + 1}/{total}] 清除成功: {pkg}");
                            success++;
                        }
                        else
                        {
                            AppendAppManagementLog($"[{i + 1}/{total}] 清除失败: {pkg} - {output}");
                            failed++;
                        }
                    }
                    catch (Exception inner)
                    {
                        AppendAppManagementLog($"[{i + 1}/{total}] 清除 {pkg} 异常: {inner.Message}");
                        failed++;
                    }
                }

                AppendAppManagementLog($"清除完成，成功 {success}，失败 {failed}");
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"清除应用数据过程中发生异常: {ex.Message}");
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }

        // 应用包名过滤
        private void ApplyAppPackageFilter(string keyword)
        {
            ICollectionView view = CollectionViewSource.GetDefaultView(AppPackages);
            if (view == null) return;

            if (string.IsNullOrWhiteSpace(keyword))
            {
                view.Filter = null;
            }
            else
            {
                string k = keyword.Trim();
                view.Filter = o =>
                {
                    if (o is AppPackageItem item && !string.IsNullOrEmpty(item.PackageName))
                    {
                        return item.PackageName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    return false;
                };
            }
            view.Refresh();

            // 刷新数据网格显示
            var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
            appListDataGrid?.Items.Refresh();
        }

        // 复制包名按钮点击事件：复制已勾选的包名到剪贴板，并记录日志数量
        private void CopySelectedPackagesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedPackages = AppPackages
                    .Where(p => p.IsSelected && !string.IsNullOrWhiteSpace(p.PackageName))
                    .Select(p => p.PackageName.Trim())
                    .ToList();

                if (selectedPackages.Count == 0)
                {
                    AppendAppManagementLog("未选择任何包名");
                    return;
                }

                string textToCopy = selectedPackages.Count == 1
                    ? selectedPackages[0]
                    : string.Join("|", selectedPackages);

                System.Windows.Clipboard.SetText(textToCopy);
                AppendAppManagementLog($"成功复制了 {selectedPackages.Count} 个包名");
            }
            catch (Exception ex)
            {
                AppendAppManagementLog($"复制包名失败: {ex.Message}");
            }
        }

        private async void RebootPhoneButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 执行adb reboot命令并获取输出
                string adbOutput = await ExecuteAdbCommandWithOutput("reboot");
                
                // 如果输出中包含"found"，说明设备不在adb界面，需要执行fastboot命令
                if (!string.IsNullOrEmpty(adbOutput) && adbOutput.ToLower().Contains("found"))
                {
                    // 等待一段时间让设备重启到fastboot模式
                    await Task.Delay(3000);
                    await ExecuteFastbootCommand("reboot");
                }
                // 如果输出为空，说明设备在adb界面，不需要执行fastboot命令
                
                // 在所有重启命令执行完成后终止adb.exe和fastboot.exe进程
                await KillAllAdbAndFastbootProcesses();
            }
            catch (Exception ex)
            {
            }
        }

        private async void RebootToFastbootButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 执行adb reboot bootloader命令并获取输出
                string adbOutput = await ExecuteAdbCommandWithOutput("reboot bootloader");
                
                // 如果输出中包含"found"，说明设备不在adb界面，需要执行fastboot命令
                if (!string.IsNullOrEmpty(adbOutput) && adbOutput.ToLower().Contains("found"))
                {
                    // 等待一段时间让设备重启到fastboot模式
                    await Task.Delay(3000);
                    await ExecuteFastbootCommand("reboot-bootloader");
                }
                // 如果输出为空，说明设备在adb界面，不需要执行fastboot命令
                
                // 在所有重启命令执行完成后终止adb.exe和fastboot.exe进程
                await KillAllAdbAndFastbootProcesses();
            }
            catch (Exception ex)
            {
            }
        }

        private async void RebootToFastbootDButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 执行adb reboot fastboot命令并获取输出
                string adbOutput = await ExecuteAdbCommandWithOutput("reboot fastboot");
                
                // 如果输出中包含"found"，说明设备不在adb界面，需要执行fastboot命令
                if (!string.IsNullOrEmpty(adbOutput) && adbOutput.ToLower().Contains("found"))
                {
                    // 等待一段时间让设备重启到fastboot模式
                    await Task.Delay(3000);
                    await ExecuteFastbootCommand("reboot fastboot");
                }
                // 如果输出为空，说明设备在adb界面，不需要执行fastboot命令
                
                // 在所有重启命令执行完成后终止adb.exe和fastboot.exe进程
                await KillAllAdbAndFastbootProcesses();
            }
            catch (Exception ex)
            {
            }
        }

        private async void RebootTo9008Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 执行adb reboot edl命令并获取输出
                string adbOutput = await ExecuteAdbCommandWithOutput("reboot edl");
                
                // 如果输出中包含"found"，说明设备不在adb界面，需要执行fastboot命令
                if (!string.IsNullOrEmpty(adbOutput) && adbOutput.ToLower().Contains("found"))
                {
                    // 等待一段时间让设备重启到fastboot模式
                    await Task.Delay(3000);
                    await ExecuteFastbootCommand("oem edl");
                }
                // 如果输出为空，说明设备在adb界面，不需要执行fastboot命令
                
                // 在所有重启命令执行完成后终止adb.exe和fastboot.exe进程
                await KillAllAdbAndFastbootProcesses();
            }
            catch (Exception ex)
            {
            }
        }

        private async void RebootToRecoveryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 执行adb reboot recovery命令并获取输出
                string adbOutput = await ExecuteAdbCommandWithOutput("reboot recovery");
                
                // 如果输出中包含"found"，说明设备不在adb界面，需要执行fastboot命令
                if (!string.IsNullOrEmpty(adbOutput) && adbOutput.ToLower().Contains("found"))
                {
                    // 等待一段时间让设备重启到fastboot模式
                    await Task.Delay(3000);
                    await ExecuteFastbootCommand("reboot recovery");
                }
                // 如果输出为空，说明设备在adb界面，不需要执行fastboot命令
                
                // 在所有重启命令执行完成后终止adb.exe和fastboot.exe进程
                await KillAllAdbAndFastbootProcesses();
            }
            catch (Exception ex)
            {
            }
        }

        private async void SwitchSlotButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取fastboot路径
                string fastbootPath = GetFastbootPath();
                if (string.IsNullOrEmpty(fastbootPath))
                {
                    System.Windows.MessageBox.Show("错误：未找到fastboot工具", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 检查设备是否连接
                string deviceCheckResult = await ExecuteFastbootCommand(fastbootPath, "devices");
                if (string.IsNullOrEmpty(deviceCheckResult) || !deviceCheckResult.Contains("fastboot"))
                {
                    System.Windows.MessageBox.Show("请在Fastboot模式下使用此功能", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 获取当前槽位
                string currentSlotOutput = await ExecuteFastbootCommand(fastbootPath, "getvar current-slot");
                
                if (string.IsNullOrEmpty(currentSlotOutput))
                {
                    System.Windows.MessageBox.Show("无法获取当前槽位信息", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 解析当前槽位
                string currentSlot = ExtractFastbootVar(currentSlotOutput, "current-slot");
                
                if (string.IsNullOrEmpty(currentSlot))
                {
                    System.Windows.MessageBox.Show("无法解析当前槽位信息", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 确定目标槽位
                string targetSlot;
                if (currentSlot.ToLower() == "a")
                {
                    targetSlot = "b";
                }
                else if (currentSlot.ToLower() == "b")
                {
                    targetSlot = "a";
                }
                else
                {
                    System.Windows.MessageBox.Show($"未知的槽位: {currentSlot}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 显示商业化确认对话框
                if (ShowHomeSlotSwitchConfirmationDialog(currentSlot, targetSlot))
                {
                    // 执行槽位切换命令
                    string switchResult = await ExecuteFastbootCommand(fastbootPath, $"set_active {targetSlot}");
                    
                    // 显示结果
                    if (switchResult.ToLower().Contains("okay") || switchResult.ToLower().Contains("finished"))
                    {
                        ShowHomeSlotSwitchResultDialog(
                            succeeded: true,
                            currentSlot,
                            targetSlot,
                            switchResult);
                    }
                    else
                    {
                        ShowHomeSlotSwitchResultDialog(
                            succeeded: false,
                            currentSlot,
                            targetSlot,
                            switchResult);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"切换槽位时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ShowHomeSlotSwitchConfirmationDialog(string currentSlot, string targetSlot)
        {
            string currentSlotText = $"{currentSlot.ToUpperInvariant()}槽";
            string targetSlotText = $"{targetSlot.ToUpperInvariant()}槽";

            var dialog = new System.Windows.Window
            {
                Title = "切换槽位",
                Width = 430,
                Height = 285,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.White,
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI")
            };

            var root = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(24, 20, 24, 18)
            };
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var headingPanel = new System.Windows.Controls.StackPanel();
            headingPanel.Children.Add(new TextBlock
            {
                Text = "请确认本次槽位切换",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(38, 49, 66))
            });
            headingPanel.Children.Add(new TextBlock
            {
                Text = "切换后，设备将在下次启动时使用目标槽位。",
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 12,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(100, 116, 139))
            });
            System.Windows.Controls.Grid.SetRow(headingPanel, 0);
            root.Children.Add(headingPanel);

            var slotGrid = new System.Windows.Controls.Grid();
            slotGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            slotGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(48)
            });
            slotGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            var currentPanel = new System.Windows.Controls.StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            currentPanel.Children.Add(new TextBlock
            {
                Text = "当前槽位",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                FontSize = 12,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(100, 116, 139))
            });
            currentPanel.Children.Add(new TextBlock
            {
                Text = currentSlotText,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85))
            });
            System.Windows.Controls.Grid.SetColumn(currentPanel, 0);
            slotGrid.Children.Add(currentPanel);

            var arrow = new TextBlock
            {
                Text = "→",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                FontSize = 20,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(148, 163, 184))
            };
            System.Windows.Controls.Grid.SetColumn(arrow, 1);
            slotGrid.Children.Add(arrow);

            var targetPanel = new System.Windows.Controls.StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            targetPanel.Children.Add(new TextBlock
            {
                Text = "目标槽位",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                FontSize = 12,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(100, 116, 139))
            });
            targetPanel.Children.Add(new TextBlock
            {
                Text = targetSlotText,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(124, 58, 237))
            });
            System.Windows.Controls.Grid.SetColumn(targetPanel, 2);
            slotGrid.Children.Add(targetPanel);

            var contentPanel = new System.Windows.Controls.StackPanel();
            contentPanel.Children.Add(slotGrid);
            contentPanel.Children.Add(new TextBlock
            {
                Text = "请确认目标槽位包含可正常启动的系统。",
                Margin = new Thickness(0, 14, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                FontSize = 12,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(217, 119, 6))
            });

            var contentBorder = new System.Windows.Controls.Border
            {
                Margin = new Thickness(0, 14, 0, 14),
                Padding = new Thickness(16, 15, 16, 14),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(226, 232, 240)),
                Background = new SolidColorBrush(MediaColor.FromRgb(250, 251, 253)),
                Child = contentPanel
            };
            System.Windows.Controls.Grid.SetRow(contentBorder, 1);
            root.Children.Add(contentBorder);

            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 86,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                Background = System.Windows.Media.Brushes.White,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(71, 85, 105)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(203, 213, 225)),
                IsCancel = true
            };
            var confirmButton = new System.Windows.Controls.Button
            {
                Content = "确认切换",
                Width = 96,
                Height = 32,
                Background = new SolidColorBrush(MediaColor.FromRgb(190, 112, 225)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(190, 112, 225)),
                Foreground = System.Windows.Media.Brushes.White,
                IsDefault = true
            };
            cancelButton.Click += (_, _) => dialog.DialogResult = false;
            confirmButton.Click += (_, _) => dialog.DialogResult = true;
            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(confirmButton);
            System.Windows.Controls.Grid.SetRow(buttonPanel, 2);
            root.Children.Add(buttonPanel);

            dialog.Content = root;
            return dialog.ShowDialog() == true;
        }

        private void ShowHomeSlotSwitchResultDialog(
            bool succeeded,
            string currentSlot,
            string targetSlot,
            string fastbootOutput)
        {
            string currentSlotText = $"{currentSlot.ToUpperInvariant()}槽";
            string targetSlotText = $"{targetSlot.ToUpperInvariant()}槽";

            var dialog = new System.Windows.Window
            {
                Title = succeeded ? "槽位切换完成" : "槽位切换失败",
                Width = 430,
                SizeToContent = SizeToContent.Height,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.White,
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI")
            };

            var root = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(24, 20, 24, 18)
            };
            root.Children.Add(new TextBlock
            {
                Text = succeeded ? "槽位切换成功" : "槽位切换未完成",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(38, 49, 66))
            });

            var contentPanel = new System.Windows.Controls.StackPanel();
            if (succeeded)
            {
                var slotTransition = new TextBlock
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold
                };
                slotTransition.Inlines.Add(new System.Windows.Documents.Run(currentSlotText)
                {
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85))
                });
                slotTransition.Inlines.Add(new System.Windows.Documents.Run("   →   ")
                {
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(148, 163, 184))
                });
                slotTransition.Inlines.Add(new System.Windows.Documents.Run(targetSlotText)
                {
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(124, 58, 237))
                });
                contentPanel.Children.Add(slotTransition);
                contentPanel.Children.Add(new TextBlock
                {
                    Text = $"设备将在下次启动时使用 {targetSlotText}。",
                    Margin = new Thickness(0, 12, 0, 0),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(100, 116, 139))
                });
            }
            else
            {
                contentPanel.Children.Add(new TextBlock
                {
                    Text = "Fastboot 未返回明确的切换成功结果，请检查设备连接及槽位状态。",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(217, 119, 6))
                });

                string nativeOutput = string.IsNullOrWhiteSpace(fastbootOutput)
                    ? "未返回原始输出"
                    : fastbootOutput.Trim();
                contentPanel.Children.Add(new TextBlock
                {
                    Text = nativeOutput,
                    Margin = new Thickness(0, 10, 0, 0),
                    MaxHeight = 58,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = nativeOutput,
                    FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono,Microsoft YaHei UI,Consolas"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(220, 38, 38))
                });
            }

            var contentBorder = new System.Windows.Controls.Border
            {
                Margin = new Thickness(0, 14, 0, 18),
                Padding = new Thickness(16, 17, 16, 16),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(226, 232, 240)),
                Background = new SolidColorBrush(MediaColor.FromRgb(250, 251, 253)),
                Child = contentPanel
            };
            root.Children.Add(contentBorder);

            var closeButton = new System.Windows.Controls.Button
            {
                Content = succeeded ? "完成" : "确定",
                Width = 88,
                Height = 32,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Background = new SolidColorBrush(MediaColor.FromRgb(190, 112, 225)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(190, 112, 225)),
                Foreground = System.Windows.Media.Brushes.White,
                IsDefault = true,
                IsCancel = true
            };
            closeButton.Click += (_, _) => dialog.DialogResult = true;
            root.Children.Add(closeButton);

            dialog.Content = root;
            dialog.ShowDialog();
        }

        private async void ForcePortraitButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell settings put system user_rotation 0");
                await ExecuteAdbCommand("shell settings put system accelerometer_rotation 0");
                ShowMessage("已强制设置为竖屏模式");
            }
            catch (Exception ex)
            {
                ShowMessage($"强制竖屏失败: {ex.Message}");
            }
        }

        private async void ForceLandscapeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell settings put system user_rotation 1");
                await ExecuteAdbCommand("shell settings put system accelerometer_rotation 0");
                ShowMessage("已强制设置为横屏模式");
            }
            catch (Exception ex)
            {
                ShowMessage($"强制横屏失败: {ex.Message}");
            }
        }

        private async void AutoRotateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell settings put system accelerometer_rotation 1");
                ShowMessage("已启用自动旋转");
            }
            catch (Exception ex)
            {
                ShowMessage($"启用自动旋转失败: {ex.Message}");
            }
        }

        private async void BackKeyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SendMirrorNavigationKeyAsync(4);
            }
            catch (Exception ex)
            {
                ShowMessage($"返回键发送失败: {ex.Message}");
            }
        }

        private async void HomeKeyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SendMirrorNavigationKeyAsync(3);
            }
            catch (Exception ex)
            {
                ShowMessage($"主页键发送失败: {ex.Message}");
            }
        }

        private async void RecentAppsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SendMirrorNavigationKeyAsync(187);
            }
            catch (Exception ex)
            {
                ShowMessage($"多任务键发送失败: {ex.Message}");
            }
        }

        private async Task SendMirrorNavigationKeyAsync(int keyCode, bool silent = false)
        {
            await ExecuteAdbCommand($"shell input keyevent {keyCode}");

            if (!silent)
            {
                string message = keyCode switch
                {
                    4 => "返回键已发送",
                    3 => "主页键已发送",
                    187 => "多任务键已发送",
                    _ => $"按键 {keyCode} 已发送"
                };
                ShowMessage(message);
            }
        }

        private bool IsScrcpyControlBarEnabled()
        {
            return MirrorNavigationBarToggle?.IsChecked == true;
        }

        private void MirrorNavigationBarToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (scrcpyProcess != null && !scrcpyProcess.HasExited)
            {
                InitializeScrcpyControlBarTracking(scrcpyProcess);
            }
        }

        private void MirrorNavigationBarToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            CloseScrcpyControlBar();
        }

        private void InitializeScrcpyControlBarTracking(Process process)
        {
            if (!IsScrcpyControlBarEnabled())
            {
                CloseScrcpyControlBar();
                return;
            }

            EnsureScrcpyControlBarWindow();
            _ = AttachScrcpyControlBarAsync(process);
        }

        private async Task AttachScrcpyControlBarAsync(Process process)
        {
            try
            {
                IntPtr handle = IntPtr.Zero;

                for (int i = 0; i < 30; i++)
                {
                    if (process.HasExited)
                    {
                        return;
                    }

                    process.Refresh();
                    handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero && IsWindow(handle))
                    {
                        break;
                    }

                    await Task.Delay(200);
                }

                if (handle == IntPtr.Zero || !IsWindow(handle))
                {
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    if (!IsScrcpyControlBarEnabled())
                    {
                        CloseScrcpyControlBar();
                        return;
                    }

                    scrcpyMainWindowHandle = handle;
                    RegisterScrcpyLocationChangeHook((uint)process.Id);
                    EnsureScrcpyControlBarWindow();
                    ClearScrcpyWindowTopMost();
                    UpdateScrcpyControlBarPosition();
                    scrcpyControlBarWindow?.Show();
                    StartScrcpyControlBarTimer();
                });
            }
            catch
            {
            }
        }

        private void EnsureScrcpyControlBarWindow()
        {
            if (scrcpyControlBarWindow != null)
            {
                return;
            }

            var container = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromArgb(245, 255, 255, 255)),
                BorderBrush = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#FFD8D8D8")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8)
            };

            var buttonPanel = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            buttonPanel.Children.Add(CreateScrcpyControlBarButton("多任务", "icon_多任务.svg", async () => await SendMirrorNavigationKeyAsync(187, true)));
            buttonPanel.Children.Add(CreateScrcpyControlBarButton("主页", "主页.svg", async () => await SendMirrorNavigationKeyAsync(3, true)));
            buttonPanel.Children.Add(CreateScrcpyControlBarButton("返回", "返回.svg", async () => await SendMirrorNavigationKeyAsync(4, true)));

            container.Child = buttonPanel;

            scrcpyControlBarWindow = new Window
            {
                Width = 292,
                Height = 58,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Topmost = false,
                AllowsTransparency = true,
                Background = MediaBrushes.Transparent,
                ShowActivated = false,
                Content = container
            };

            scrcpyControlBarWindow.Closed += (s, e) =>
            {
                scrcpyControlBarWindow = null;
            };
        }

        private System.Windows.Controls.Button CreateScrcpyControlBarButton(string text, string iconFileName, Func<Task> onClick)
        {
            string iconPath = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", iconFileName);
            var contentPanel = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            if (IOFile.Exists(iconPath))
            {
                contentPanel.Children.Add(new SvgViewbox
                {
                    Source = new Uri(iconPath, UriKind.Absolute),
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 6, 0)
                });
            }

            contentPanel.Children.Add(new TextBlock
            {
                Text = text,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#FF1F1F1F"))
            });

            var button = new System.Windows.Controls.Button
            {
                Content = contentPanel,
                Width = 82,
                Height = 34,
                Margin = new Thickness(4, 0, 4, 0),
                Background = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#FFF8F9FA")),
                Foreground = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#FF1F1F1F")),
                BorderBrush = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#FFE0E0E0")),
                BorderThickness = new Thickness(1),
                Cursor = WpfCursors.Hand,
                FontSize = 13
            };

            button.Click += async (s, e) =>
            {
                try
                {
                    await onClick();
                }
                catch (Exception ex)
                {
                    AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 底部控制条按键发送失败: {ex.Message}\n");
                }
            };

            button.Style = new Style(typeof(System.Windows.Controls.Button))
            {
                Setters =
                {
                    new Setter(System.Windows.Controls.Button.TemplateProperty, new ControlTemplate(typeof(System.Windows.Controls.Button))
                    {
                        VisualTree = BuildScrcpyControlBarButtonTemplate()
                    })
                }
            };

            return button;
        }

        private FrameworkElementFactory BuildScrcpyControlBarButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            border.AppendChild(presenter);

            return border;
        }

        private void StartScrcpyControlBarTimer()
        {
            if (scrcpyControlBarTimer == null)
            {
                scrcpyControlBarTimer = new DispatcherTimer
                {
                    // Keep a lightweight fallback in case some location change events are missed.
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                scrcpyControlBarTimer.Tick += (s, e) => UpdateScrcpyControlBarPosition();
            }

            scrcpyControlBarTimer.Start();
        }

        private void UpdateScrcpyControlBarPosition()
        {
            if (!IsScrcpyControlBarEnabled())
            {
                scrcpyControlBarWindow?.Hide();
                return;
            }

            if (scrcpyControlBarWindow == null)
            {
                return;
            }

            if (scrcpyMainWindowHandle == IntPtr.Zero || !IsWindow(scrcpyMainWindowHandle))
            {
                scrcpyControlBarWindow.Hide();
                return;
            }

            if (IsIconic(scrcpyMainWindowHandle) || !GetWindowRect(scrcpyMainWindowHandle, out RECT rect))
            {
                scrcpyControlBarWindow.Hide();
                return;
            }

            ClearScrcpyWindowTopMost();

            DpiScale dpi = VisualTreeHelper.GetDpi(scrcpyControlBarWindow);
            double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
            double barWidthDip = scrcpyControlBarWindow.ActualWidth > 0 ? scrcpyControlBarWindow.ActualWidth : scrcpyControlBarWindow.Width;
            int scrcpyWidthPx = Math.Max(0, rect.Right - rect.Left);
            int barWidthPx = (int)Math.Round(barWidthDip * scaleX);
            int gapPx = Math.Max(4, (int)Math.Round(8 * scaleY));
            int targetLeftPx = rect.Left + Math.Max(0, (scrcpyWidthPx - barWidthPx) / 2);
            int targetTopPx = rect.Bottom + gapPx;

            if (!scrcpyControlBarWindow.IsVisible)
            {
                scrcpyControlBarWindow.Show();
            }

            IntPtr controlBarHandle = new System.Windows.Interop.WindowInteropHelper(scrcpyControlBarWindow).Handle;
            if (controlBarHandle != IntPtr.Zero)
            {
                SetWindowPos(
                    controlBarHandle,
                    HWND_NOTOPMOST,
                    targetLeftPx,
                    targetTopPx,
                    0,
                    0,
                    SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
            }
            else
            {
                scrcpyControlBarWindow.Left = targetLeftPx / scaleX;
                scrcpyControlBarWindow.Top = targetTopPx / scaleY;
            }
        }

        private void ClearScrcpyWindowTopMost()
        {
            if (scrcpyMainWindowHandle == IntPtr.Zero || !IsWindow(scrcpyMainWindowHandle))
            {
                return;
            }

            SetWindowPos(
                scrcpyMainWindowHandle,
                HWND_NOTOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
        }

        private void CloseScrcpyControlBar()
        {
            scrcpyMainWindowHandle = IntPtr.Zero;
            scrcpyControlBarTimer?.Stop();
            UnregisterScrcpyLocationChangeHook();

            if (scrcpyControlBarWindow != null)
            {
                try
                {
                    scrcpyControlBarWindow.Close();
                }
                catch
                {
                }
                scrcpyControlBarWindow = null;
            }
        }

        private void RegisterScrcpyLocationChangeHook(uint processId)
        {
            UnregisterScrcpyLocationChangeHook();

            scrcpyLocationChangeProc = OnScrcpyLocationChanged;
            scrcpyLocationChangeHook = SetWinEventHook(
                EVENT_OBJECT_LOCATIONCHANGE,
                EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero,
                scrcpyLocationChangeProc,
                processId,
                0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }

        private void UnregisterScrcpyLocationChangeHook()
        {
            if (scrcpyLocationChangeHook != IntPtr.Zero)
            {
                UnhookWinEvent(scrcpyLocationChangeHook);
                scrcpyLocationChangeHook = IntPtr.Zero;
            }
        }

        private void OnScrcpyLocationChanged(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint idEventThread,
            uint dwmsEventTime)
        {
            if (hwnd != scrcpyMainWindowHandle || idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
            {
                return;
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(UpdateScrcpyControlBarPosition));
        }

        private async void PowerKeyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell input keyevent 26");
                ShowMessage("电源键已发送");
            }
            catch (Exception ex)
            {
                ShowMessage($"电源键发送失败: {ex.Message}");
            }
        }

        private async void VolumeUpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell input keyevent 24");
                ShowMessage("音量增加键已发送");
            }
            catch (Exception ex)
            {
                ShowMessage($"音量增加键发送失败: {ex.Message}");
            }
        }

        private async void VolumeDownButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell input keyevent 25");
                ShowMessage("音量减少键已发送");
            }
            catch (Exception ex)
            {
                ShowMessage($"音量减少键发送失败: {ex.Message}");
            }
        }

        private async void LockScreenButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell input keyevent 26");
                ShowMessage("设备已锁屏");
            }
            catch (Exception ex)
            {
                ShowMessage($"锁屏失败: {ex.Message}");
            }
        }

        private async void ScreenOnButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell input keyevent 224");
                ShowMessage("屏幕已点亮");
            }
            catch (Exception ex)
            {
                ShowMessage($"屏幕点亮失败: {ex.Message}");
            }
        }

        private async void StartMirrorButton_Click(object sender, RoutedEventArgs e)
        {
            if (isScrcpyStarting || (scrcpyProcess != null && !scrcpyProcess.HasExited))
            {
                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 投屏已在启动或运行中，请勿重复启动。\n");
                ShowMessage("投屏已在启动或运行中");
                return;
            }

            isScrcpyStarting = true;
            StartMirrorButton.IsEnabled = false;

            try
            {
                // 获取选中的设备序列号
                string selectedSerial = GetSelectedDeviceSerial();
                
                // 检查是否选择了设备
                if (string.IsNullOrEmpty(selectedSerial))
                {
                    AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 请先选择要投屏的设备！\n");
                    ShowMessage("请先选择要投屏的设备！");
                    return;
                }
                
                // 获取当前应用程序目录
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string scrcpyPath = Path.Combine(appDirectory, "platform-tools", "scrcpy.exe");
                
                // 检查scrcpy.exe是否存在
                if (File.Exists(scrcpyPath))
                {
                    // 获取帧率滑块的值
                    var maxFpsSlider = this.FindName("MaxFpsSlider") as Slider;
                    int maxFps = maxFpsSlider != null ? (int)maxFpsSlider.Value : 60; // 默认60fps
                    
                    // 获取比特率滑块的值
                    var bitrateSlider = this.FindName("BitrateSlider") as Slider;
                    int bitrate = bitrateSlider != null ? (int)bitrateSlider.Value : 8; // 默认8Mbps
                    
                    // 获取窗口大小设置
                    int maxSize = GetWindowMaxSize();
                    
                    string arguments = await BuildScrcpyLaunchArgumentsAsync(selectedSerial, bitrate, maxFps, maxSize);
                    string scrcpyWindowTitle = !string.IsNullOrWhiteSpace(selectedSerial)
                        ? await BuildScrcpyWindowTitleAsync(selectedSerial)
                        : "Yuzaki工具箱";
                    
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = scrcpyPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(scrcpyPath),
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    
                    scrcpyProcess = Process.Start(startInfo);
                    
                    if (scrcpyProcess != null)
                    {
                        InitializeScrcpyControlBarTracking(scrcpyProcess);
                        // 添加日志到文本框
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 正在启动scrcpy投屏...\n");
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 目标设备: {selectedSerial}\n");
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 标题信息: {scrcpyWindowTitle}\n");
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 命令: {scrcpyPath} {arguments}\n");
                        
                        // 异步读取标准输出
                        scrcpyProcess.OutputDataReceived += (sender, e) => ProcessScrcpyLogLine(e.Data, false);
                        
                        // 异步读取错误输出
                        scrcpyProcess.ErrorDataReceived += (sender, e) => ProcessScrcpyLogLine(e.Data, true);
                        
                        // 开始异步读取
                        scrcpyProcess.BeginOutputReadLine();
                        scrcpyProcess.BeginErrorReadLine();
                        
                        // 监听进程退出事件
                        scrcpyProcess.EnableRaisingEvents = true;
                        scrcpyProcess.Exited += (sender, e) =>
                        {
                            AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] scrcpy进程已退出\n");
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                CloseScrcpyControlBar();
                            }));
                        };
                        
                        ShowMessage($"正在启动投屏，目标设备: {selectedSerial}");
                    }
                    else
                    {
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 启动scrcpy失败！\n");
                        ShowMessage("启动投屏失败！");
                    }
                }
                else
                {
                    AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 未找到scrcpy.exe文件，请检查platform-tools目录！\n");
                    ShowMessage("未找到scrcpy.exe文件，请检查platform-tools目录！");
                }
            }
            catch (Exception ex)
            {
                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 投屏启动失败: {ex.Message}\n");
                ShowMessage($"投屏启动失败: {ex.Message}");
            }
            finally
            {
                isScrcpyStarting = false;
                StartMirrorButton.IsEnabled = true;
            }
        }

        // 处理投屏窗口大小单选按钮选择变化
        private void WindowSizeRadio_Checked(object sender, RoutedEventArgs e)
        {
            var radioButton = sender as System.Windows.Controls.RadioButton;
            var customSizePanel = this.FindName("CustomSizePanel") as StackPanel;
            
            if (radioButton != null && customSizePanel != null)
            {
                string tag = radioButton.Tag?.ToString() ?? "";
                
                // 如果选择了自定义大小，显示输入框
                if (tag == "custom")
                {
                    customSizePanel.Visibility = Visibility.Visible;
                }
                else
                {
                    customSizePanel.Visibility = Visibility.Collapsed;
                }
            }
        }

        // 获取窗口大小参数
        private int GetWindowMaxSize()
        {
            // 检查哪个 RadioButton 被选中
            var radio90 = this.FindName("RadioSize90") as System.Windows.Controls.RadioButton;
            var radio80 = this.FindName("RadioSize80") as System.Windows.Controls.RadioButton;
            var radio70 = this.FindName("RadioSize70") as System.Windows.Controls.RadioButton;
            var radio60 = this.FindName("RadioSize60") as System.Windows.Controls.RadioButton;
            var radioCustom = this.FindName("RadioSizeCustom") as System.Windows.Controls.RadioButton;
            
            if (radio90?.IsChecked == true) return 972;  // 90%
            if (radio80?.IsChecked == true) return 864;  // 80%
            if (radio70?.IsChecked == true) return 756;  // 70%
            if (radio60?.IsChecked == true) return 648;  // 60%
            if (radioCustom?.IsChecked == true)
            {
                // 自定义模式使用独立的窗口宽高参数，不再误用 --max-size。
                return 0;
            }
            
            // 默认返回90%
            return 972;
        }

        private async Task<string> BuildScrcpyWindowTitleAsync(string deviceSerial)
        {
            List<string> selectedOptions = GetMirrorTitleSelectionsInOrder();
            var titleParts = new List<string>();

            foreach (string optionName in selectedOptions)
            {
                string value = optionName switch
                {
                    nameof(MirrorTitleShowDeviceCodeCheckBox) => await GetAdbPropertyAsync("ro.product.device"),
                    nameof(MirrorTitleShowAndroidVersionCheckBox) => await GetAdbPropertyAsync("ro.build.version.release"),
                    nameof(MirrorTitleShowSlotCheckBox) => await GetCurrentBootSlotAsync(),
                    nameof(MirrorTitleShowSerialCheckBox) => deviceSerial,
                    nameof(MirrorTitleShowDeviceNameCheckBox) => await GetAdbPropertyAsync("ro.product.model"),
                    nameof(MirrorTitleShowBuildInfoCheckBox) => await GetBuildInfoAsync(),
                    nameof(MirrorTitleShowUnlockStateCheckBox) => await GetUnlockStateAsync(),
                    nameof(MirrorTitleShowBatteryTempCheckBox) => await GetBatteryTemperatureAsync(),
                    nameof(MirrorTitleShowBatteryLevelCheckBox) => await GetBatteryLevelAsync(),
                    nameof(MirrorTitleShowStorageCheckBox) => await GetStorageSummaryAsync(),
                    _ => string.Empty
                };

                value = GetTitleDisplayValue(value);
                value = FormatMirrorTitlePart(optionName, value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    titleParts.Add(value);
                }
            }

            if (titleParts.Count == 0)
            {
                titleParts.Add("Yuzaki工具箱");
            }

            string title = string.Join("_", titleParts);
            return title.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private string FormatMirrorTitlePart(string optionName, string value)
        {
            return optionName switch
            {
                nameof(MirrorTitleShowSlotCheckBox) => $"槽位{value.ToUpperInvariant()}",
                nameof(MirrorTitleShowAndroidVersionCheckBox) => $"安卓{value}",
                nameof(MirrorTitleShowSerialCheckBox) => $"序列号{value}",
                nameof(MirrorTitleShowDeviceCodeCheckBox) => $"代号{value}",
                _ => value
            };
        }

        private void ProcessScrcpyLogLine(string? line, bool isError = false)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            string prefix = isError ? "ERROR: " : "";
            AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] [scrcpy] {prefix}{line}\n");

            if (line.Contains("injectInputEvent") || line.Contains("injectInputEventToTarget") ||
                line.Contains("SecurityException") || (line.Contains("InvocationTargetException") && line.Contains("InputManager")))
            {
                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] --------------------------------------------------\n" +
                    $"[{DateTime.Now:HH:mm:ss}] 【投屏触控被拦截排查提示】检测到事件注入被手机系统拦截！\n" +
                    $"[{DateTime.Now:HH:mm:ss}] 原因：小米/HyperOS/MIUI 设备未开启模拟点击权限。\n" +
                    $"[{DateTime.Now:HH:mm:ss}] 解决方案：\n" +
                    $"[{DateTime.Now:HH:mm:ss}]  1. 手机进入【开发者选项】-> 开启【USB 调试（安全设置）】；\n" +
                    $"[{DateTime.Now:HH:mm:ss}]  2. 或在投屏设置中勾选【仅投屏不控制】即可免权限流畅投屏。\n" +
                    $"[{DateTime.Now:HH:mm:ss}] --------------------------------------------------\n");
            }
        }

        private async Task<string> BuildScrcpyLaunchArgumentsAsync(string? selectedSerial, int bitrate, int maxFps, int maxSize)
        {
            var arguments = new List<string>();

            if (!string.IsNullOrWhiteSpace(selectedSerial))
            {
                string scrcpyWindowTitle = await BuildScrcpyWindowTitleAsync(selectedSerial);
                arguments.Add($"-s {selectedSerial}");
                arguments.Add($"--window-title \"{scrcpyWindowTitle}\"");
            }

            arguments.Add($"--video-bit-rate {bitrate}M");
            arguments.Add($"--max-fps {maxFps}");

            if (maxSize > 0)
            {
                arguments.Add($"--max-size {maxSize}");
            }

            if (RadioSizeCustom?.IsChecked == true)
            {
                if (!int.TryParse(CustomWidthTextBox?.Text, out int customWidth) || customWidth <= 0 ||
                    !int.TryParse(CustomHeightTextBox?.Text, out int customHeight) || customHeight <= 0)
                {
                    throw new InvalidOperationException("自定义投屏宽度和高度必须是大于 0 的整数。");
                }

                arguments.Add($"--window-width {customWidth}");
                arguments.Add($"--window-height {customHeight}");
            }

            if (MirrorNoControlCheckBox?.IsChecked == true)
            {
                arguments.Add("--no-control");
            }

            if (MirrorClipboardSyncCheckBox?.IsChecked != true)
            {
                arguments.Add("--no-clipboard-autosync");
            }

            if (MirrorStayAwakeCheckBox?.IsChecked == true)
            {
                arguments.Add("--stay-awake");
            }

            if (MirrorFullscreenCheckBox?.IsChecked == true)
            {
                arguments.Add("--fullscreen");
            }

            if (TopmostCheckBox?.IsChecked == true)
            {
                arguments.Add("--always-on-top");
            }

            if (MirrorOrientation90RadioButton?.IsChecked == true)
            {
                arguments.Add("--capture-orientation 90");
            }
            else if (MirrorOrientation180RadioButton?.IsChecked == true)
            {
                arguments.Add("--capture-orientation 180");
            }

            return string.Join(" ", arguments);
        }

        private void MirrorTitleOptionCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox checkBox || string.IsNullOrWhiteSpace(checkBox.Name))
            {
                return;
            }

            _mirrorTitleSelectionOrder.Remove(checkBox.Name);

            if (checkBox.IsChecked == true)
            {
                _mirrorTitleSelectionOrder.Add(checkBox.Name);
            }
        }

        private List<string> GetMirrorTitleSelectionsInOrder()
        {
            string[] defaultOrder =
            {
                nameof(MirrorTitleShowDeviceNameCheckBox),
                nameof(MirrorTitleShowSlotCheckBox),
                nameof(MirrorTitleShowAndroidVersionCheckBox),
                nameof(MirrorTitleShowSerialCheckBox),
                nameof(MirrorTitleShowDeviceCodeCheckBox),
                nameof(MirrorTitleShowBuildInfoCheckBox),
                nameof(MirrorTitleShowUnlockStateCheckBox),
                nameof(MirrorTitleShowBatteryTempCheckBox),
                nameof(MirrorTitleShowBatteryLevelCheckBox),
                nameof(MirrorTitleShowStorageCheckBox)
            };

            List<string> checkedNames = defaultOrder
                .Where(IsMirrorTitleOptionChecked)
                .ToList();

            if (_mirrorTitleSelectionOrder.Count == 0)
            {
                return checkedNames;
            }

            _mirrorTitleSelectionOrder.RemoveAll(name => !checkedNames.Contains(name));

            foreach (string name in checkedNames)
            {
                if (!_mirrorTitleSelectionOrder.Contains(name))
                {
                    _mirrorTitleSelectionOrder.Add(name);
                }
            }

            return new List<string>(_mirrorTitleSelectionOrder);
        }

        private bool IsMirrorTitleOptionChecked(string checkBoxName)
        {
            return checkBoxName switch
            {
                nameof(MirrorTitleShowDeviceCodeCheckBox) => MirrorTitleShowDeviceCodeCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowAndroidVersionCheckBox) => MirrorTitleShowAndroidVersionCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowSlotCheckBox) => MirrorTitleShowSlotCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowSerialCheckBox) => MirrorTitleShowSerialCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowDeviceNameCheckBox) => MirrorTitleShowDeviceNameCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowBuildInfoCheckBox) => MirrorTitleShowBuildInfoCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowUnlockStateCheckBox) => MirrorTitleShowUnlockStateCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowBatteryTempCheckBox) => MirrorTitleShowBatteryTempCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowBatteryLevelCheckBox) => MirrorTitleShowBatteryLevelCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowStorageCheckBox) => MirrorTitleShowStorageCheckBox?.IsChecked == true,
                _ => false
            };
        }

        private async Task<string> GetAdbPropertyAsync(string propertyName)
        {
            string output = await ExecuteAdbCommandWithOutput($"shell getprop {propertyName}");
            return NormalizeSingleLineOutput(output);
        }

        private async Task<string> GetBuildInfoAsync()
        {
            string displayId = await GetAdbPropertyAsync("ro.build.display.id");
            if (!string.IsNullOrWhiteSpace(displayId))
            {
                return displayId;
            }

            return await GetAdbPropertyAsync("ro.build.version.incremental");
        }

        private async Task<string> GetCurrentBootSlotAsync()
        {
            string slotSuffix = await GetAdbPropertyAsync("ro.boot.slot_suffix");
            if (!string.IsNullOrWhiteSpace(slotSuffix))
            {
                return slotSuffix.Trim().TrimStart('_');
            }

            string slot = await GetAdbPropertyAsync("ro.boot.slot");
            if (!string.IsNullOrWhiteSpace(slot))
            {
                return slot.Trim().TrimStart('_');
            }

            return "unknown";
        }

        private async Task<string> GetUnlockStateAsync()
        {
            string flashLocked = await GetAdbPropertyAsync("ro.boot.flash.locked");
            if (flashLocked == "0")
            {
                return "已解锁";
            }

            if (flashLocked == "1")
            {
                return "未解锁";
            }

            string deviceState = await GetAdbPropertyAsync("ro.boot.vbmeta.device_state");
            if (deviceState.Equals("unlocked", StringComparison.OrdinalIgnoreCase))
            {
                return "已解锁";
            }

            if (deviceState.Equals("locked", StringComparison.OrdinalIgnoreCase))
            {
                return "未解锁";
            }

            return string.IsNullOrWhiteSpace(deviceState) ? "unknown" : deviceState;
        }

        private async Task<string> GetBatteryLevelAsync()
        {
            string batteryOutput = await ExecuteAdbCommandWithOutput("shell dumpsys battery");
            Match levelMatch = Regex.Match(batteryOutput, @"(?im)^\s*level:\s*(\d+)\s*$");
            return levelMatch.Success ? $"{levelMatch.Groups[1].Value}%" : "unknown";
        }

        private async Task<string> GetBatteryTemperatureAsync()
        {
            string batteryOutput = await ExecuteAdbCommandWithOutput("shell dumpsys battery");
            Match tempMatch = Regex.Match(batteryOutput, @"(?im)^\s*temperature:\s*(-?\d+)\s*$");
            if (tempMatch.Success &&
                double.TryParse(tempMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double temperature))
            {
                return $"{temperature / 10.0:0.#}℃";
            }

            return "unknown";
        }

        private async Task<string> GetStorageSummaryAsync()
        {
            string output = await ExecuteAdbCommandWithOutput("shell df -h /data");
            foreach (string rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (!line.Contains(" /data") && !line.EndsWith("/data", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains(" /storage/emulated") && !line.EndsWith("/storage/emulated", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] tokens = Regex.Split(line, @"\s+")
                    .Where(token => !string.IsNullOrWhiteSpace(token))
                    .ToArray();

                if (tokens.Length >= 4)
                {
                    string size = tokens[1];
                    string used = tokens[2];
                    return $"{used}/{size}";
                }
            }

            return "unknown";
        }

        private string GetTitleDisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        }

        private string NormalizeSingleLineOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return string.Empty;
            }

            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string cleaned = line.Trim();
                if (!string.IsNullOrWhiteSpace(cleaned) &&
                    !cleaned.StartsWith("error", StringComparison.OrdinalIgnoreCase))
                {
                    return cleaned;
                }
            }

            return string.Empty;
        }

        private void StopMirrorButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 正在停止scrcpy投屏...\n");
                
                // 停止全自动投屏功能
                if (isAutoMirrorEnabled)
                {
                    isAutoMirrorEnabled = false;
                    StopAutoMirrorTimer();
                    
                    // 取消勾选自动投屏复选框
                    if (AutoMirrorCheckBox != null)
                    {
                        AutoMirrorCheckBox.IsChecked = false;
                    }
                    AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 已停止全自动投屏功能\n");
                }
                
                // 清理跟踪的进程引用
                scrcpyProcess = null;
                CloseScrcpyControlBar();
                
                // 使用taskkill命令强制终止所有scrcpy相关进程
                string[] commands = {
                    "/f /im scrcpy.exe",
                    "/f /im scrcpy-server.exe",
                    "/f /im scrcpy*"
                };
                
                foreach (string args in commands)
                {
                    try
                    {
                        ProcessStartInfo taskKillInfo = new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = args,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        
                        using (Process? taskKillProcess = Process.Start(taskKillInfo))
                        {
                            taskKillProcess?.WaitForExit(5000); // 等待最多5秒
                        }
                    }
                    catch
                    {
                    }
                }
                
                // 额外尝试使用wmic命令终止进程
                try
                {
                    ProcessStartInfo wmicInfo = new ProcessStartInfo
                    {
                        FileName = "wmic",
                        Arguments = "process where \"name like '%scrcpy%'\" delete",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    
                    using (Process? wmicProcess = Process.Start(wmicInfo))
                    {
                        wmicProcess?.WaitForExit(5000);
                    }
                }
                catch
                {
                }
                
                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] scrcpy投屏已停止\n");
                ShowMessage("投屏已停止");
            }
            catch
            {
            }
        }

        private void DeviceManagerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "mmc.exe",
                    Arguments = "devmgmt.msc",
                    UseShellExecute = true,
                    Verb = "runas" // 以管理员权限运行
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                // 如果管理员权限失败，尝试普通权限
                try
                {
                    ProcessStartInfo fallbackInfo = new ProcessStartInfo
                    {
                        FileName = "mmc.exe",
                        Arguments = "devmgmt.msc",
                        UseShellExecute = true
                    };
                    Process.Start(fallbackInfo);
                }
                catch
                {
                }
            }
        }

        private void JoinQQGroupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string qqGroupUrl = "https://qun.qq.com/universal-share/share?ac=1&authKey=0%2FYo9g8At%2BoIzEUeWHD8tfpvheTN1pl0WOB3d1%2F8s44oY8R46Dh1pqutgsCRujMC&busi_data=eyJncm91cENvZGUiOiIxNTk5NzI5NTMiLCJ0b2tlbiI6Ikptcnkrb1ZONUhxQXJzQitjVHNZMGRQZVJJM29sMmdZNXJpcXZJc21ZRU9qZHorNUE1V0owQ2E2ZXZwSU9ieDQiLCJ1aW4iOiIxMjI3MzYzMzQyIn0%3D&data=XJNVJexYjP62pdpl2XDhWErxIhCBqYk2NE3CSNiRmZy5v9PcLijj2T86aWtX5Wf8FRXlb4ma8yEnVOkzVIkjYw&svctype=4&tempid=h5_group_info";
                
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = qqGroupUrl,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ShowMessage($"打开QQ群链接失败: {ex.Message}");
            }
        }

        private async void FixAdbButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 弹出ROOT权限提示窗口
                MessageBoxResult result = System.Windows.MessageBox.Show(
                    "此功能需要ROOT权限才能正常工作。\n\n请确保您的设备已获取ROOT权限，并给予Shell ROOT权限\n\n是否继续执行修复操作？",
                    "ROOT权限提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                FixAdbButton.IsEnabled = false;
                var taskStopwatch = Stopwatch.StartNew();

                string? deviceSerial = await GetAuthorizedAdbDeviceSerialAsync();
                if (string.IsNullOrEmpty(deviceSerial))
                {
                    LogSimpleStatus("错误: 未检测到已授权的 ADB 设备");
                    return;
                }

                LogSimpleStatus($"已连接 {deviceSerial} | ADB");

                string rootOutput = await ExecuteAdbCommandWithOutput("shell su -c \"echo ROOT_OK\"");
                if (!rootOutput.Contains("ROOT_OK", StringComparison.OrdinalIgnoreCase))
                {
                    LogSimpleStatus("[ERROR]设备未授予Shell ROOT权限");
                    return;
                }

                // 依次执行ADB修复命令
                string[] commands = {
                    "shell su -c \"rm -rf /data/local/tmp/\"",
                    "shell su -c \"rm -rf /data/local/Tmp/\"",
                    "shell su -c \"mkdir -p /data/local/tmp/\"",
                    "shell su -c \"chmod 777 /data/local/tmp/\"",
                    "shell su -c \"chcon -R u:object_r:shell_data_file:s0 /data/local/tmp\""
                };

                for (int i = 0; i < commands.Length; i++)
                {
                    await ExecuteAdbCommand(commands[i]);
                    await Task.Delay(500); // 每个命令之间延迟500ms
                }

                LogSimpleStatus("ADB修复完成，重新打开投屏即可.");
                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
                System.Windows.MessageBox.Show("ADB异常修复完成！\n\n现在重新打开投屏即可", "修复完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"错误: ADB 运行环境修复失败 | {ex.Message}");
            }
            finally
            {
                FixAdbButton.IsEnabled = true;
            }
        }

        private async void EnableMiuiUsbButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示ROOT权限提示
            var result = System.Windows.MessageBox.Show(
                "此功能需要ROOT权限才能正常工作，请先给予Shell ROOT权限\n\n是否继续执行强开小米USB安全设置？",
                "需要ROOT权限",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                EnableMiuiUsbButton.IsEnabled = false;
                var taskStopwatch = Stopwatch.StartNew();

                string? deviceSerial = await GetAuthorizedAdbDeviceSerialAsync();
                if (string.IsNullOrEmpty(deviceSerial))
                {
                    LogSimpleStatus("错误: 未检测到已授权的 ADB 设备");
                    return;
                }

                LogSimpleStatus($"已连接 {deviceSerial} | ADB");

                string rootOutput = await ExecuteAdbCommandWithOutput("shell su -c \"echo ROOT_OK\"");
                if (!rootOutput.Contains("ROOT_OK", StringComparison.OrdinalIgnoreCase))
                {
                    LogSimpleStatus("[ERROR]设备未授予Shell ROOT权限");
                    return;
                }

                // 执行shell脚本逻辑
                string[] commands = {
                    "shell \"echo '[1/4] 启用 USB 调试 (Security settings)'\"",
                    "shell \"su -c 'if [ \\\"$(getprop persist.security.adbinput)\\\" != \\\"1\\\" ]; then setprop persist.security.adbinput 1; echo \\\"成功：USB 调试已启用\\\"; else echo \\\"提示：USB 调试已启用，无需重复操作\\\"; fi'\"",
                    "shell \"echo '[2/4] 启用 Fastboot 模式'\"",
                    "shell \"su -c 'if [ \\\"$(getprop persist.fastboot.enable)\\\" != \\\"1\\\" ]; then setprop persist.fastboot.enable 1; echo \\\"成功：Fastboot 模式已启用\\\"; else echo \\\"提示：Fastboot 模式已启用，无需重复操作\\\"; fi'\"",
                    "shell \"echo '[3/4] 检查配置文件路径：/data/data/com.miui.securitycenter/shared_prefs/remote_provider_preferences.xml'\"",
                    "shell \"su -c 'XML_FILE=\\\"/data/data/com.miui.securitycenter/shared_prefs/remote_provider_preferences.xml\\\"; if [ -f \\\"$XML_FILE\\\" ]; then echo \\\"成功：配置文件已找到，开始修改配置\\\"; else echo \\\"错误：配置文件未找到，请确保设备已安装相关应用\\\" && exit 1; fi'\"",
                    "shell \"echo '修改配置：启用 USB 安装'\"",
                    "shell \"su -c 'XML_FILE=\\\"/data/data/com.miui.securitycenter/shared_prefs/remote_provider_preferences.xml\\\"; if grep -q \\\"security_adb_install_enable\\\" \\\"$XML_FILE\\\"; then sed -i \\\"/security_adb_install_enable/s/false/true/\\\" \\\"$XML_FILE\\\" && echo \\\"成功：USB 安装已启用\\\"; else sed -i \\\"3a \\\\\\\\    <boolean name=\\\\\\\"security_adb_install_enable\\\\\\\" value=\\\\\\\"true\\\\\\\" />\\\" \\\"$XML_FILE\\\" && echo \\\"成功：插入 USB 安装配置并启用\\\"; fi'\"",
                    "shell \"echo '修改配置：禁用安装拦截'\"",
                    "shell \"su -c 'XML_FILE=\\\"/data/data/com.miui.securitycenter/shared_prefs/remote_provider_preferences.xml\\\"; if grep -q \\\"permcenter_install_intercept_enabled\\\" \\\"$XML_FILE\\\"; then sed -i \\\"/permcenter_install_intercept_enabled/s/true/false/\\\" \\\"$XML_FILE\\\" && echo \\\"成功：安装拦截已禁用\\\"; else sed -i \\\"3a \\\\\\\\    <boolean name=\\\\\\\"permcenter_install_intercept_enabled\\\\\\\" value=\\\\\\\"false\\\\\\\" />\\\" \\\"$XML_FILE\\\" && echo \\\"成功：插入安装拦截配置并禁用\\\"; fi'\"",
                    "shell \"echo '校验配置修改结果：'\"",
                    "shell \"su -c 'XML_FILE=\\\"/data/data/com.miui.securitycenter/shared_prefs/remote_provider_preferences.xml\\\"; grep \\\"security_adb_install_enable\\\" \\\"$XML_FILE\\\"'\"",
                    "shell \"su -c 'XML_FILE=\\\"/data/data/com.miui.securitycenter/shared_prefs/remote_provider_preferences.xml\\\"; grep \\\"permcenter_install_intercept_enabled\\\" \\\"$XML_FILE\\\"'\"",
                    "shell \"echo '[4/4] 重启 com.miui.securitycenter.remote 进程'\"",
                    "shell \"su -c 'PROCESS_ID=$(pidof com.miui.securitycenter.remote); if [ -n \\\"$PROCESS_ID\\\" ]; then kill -9 \\\"$PROCESS_ID\\\" && echo \\\"成功：已重启 com.miui.securitycenter.remote 进程 (PID: $PROCESS_ID)\\\"; else echo \\\"提示：未找到 com.miui.securitycenter.remote 进程，可能不需要重启\\\"; fi'\"",
                    "shell \"echo '========================='\"",
                    "shell \"echo ' ------脚本执行完成 ------'\"",
                    "shell \"echo '========================='\""
                };

                for (int i = 0; i < commands.Length; i++)
                {
                    await ExecuteAdbCommand(commands[i]);
                    await Task.Delay(300); // 每个命令之间延迟300ms
                }

                LogSimpleStatus("强开成功，重启设备后生效.");
                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
                System.Windows.MessageBox.Show("小米USB安全设置强开完成！\n\n设置已成功修改，重启设备生效", "操作完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"错误: 小米 USB 安全设置启用失败 | {ex.Message}");
                System.Windows.MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EnableMiuiUsbButton.IsEnabled = true;
            }
        }

        // ADB读取一加DDR版本
        private async void ReadOnePlusDdrButton_Click(object sender, RoutedEventArgs e)
        {
            ReadOnePlusDdrButton.IsEnabled = false;
            var taskStopwatch = Stopwatch.StartNew();

            try
            {
                string? deviceSerial = await GetAuthorizedAdbDeviceSerialAsync();
                if (string.IsNullOrEmpty(deviceSerial))
                {
                    LogSimpleStatus("错误: 未检测到已授权的 ADB 设备");
                    return;
                }

                LogSimpleStatus($"已连接 {deviceSerial} | ADB");

                // 执行getprop命令读取DDR类型
                string getDdrCommand = "shell getprop ro.boot.ddr_type";
                string ddrOutput = await ExecuteAdbCommandWithOutput(getDdrCommand);
                string ddrValue = ddrOutput?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(ddrValue))
                {
                    LogSimpleStatus("检测结果为：设备未定义DDR，非一加789系列.");
                }
                else if (ddrValue == "0")
                {
                    LogSimpleStatus("检测结果为：DDR4.");
                }
                else if (ddrValue == "1")
                {
                    LogSimpleStatus("检测结果为：DDR5.");
                }
                else
                {
                    LogSimpleStatus("检测结果为：设备未定义DDR，非一加789系列.");
                }

                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"错误: DDR 类型读取失败 | {ex.Message}");
            }
            finally
            {
                ReadOnePlusDdrButton.IsEnabled = true;
            }
        }

        // 强开基带调试端口
        private async void EnableDiagPortButton_Click(object sender, RoutedEventArgs e)
        {
            EnableDiagPortButton.IsEnabled = false;
            var taskStopwatch = Stopwatch.StartNew();

            try
            {
                string? deviceSerial = await GetAuthorizedAdbDeviceSerialAsync();
                if (string.IsNullOrEmpty(deviceSerial))
                {
                    LogSimpleStatus("错误: 未检测到已授权的 ADB 设备");
                    return;
                }

                LogSimpleStatus($"已连接 {deviceSerial} | ADB");

                // 申请Root权限
                LogSimpleStatus("检查ROOT权限.");
                string suCommand = "shell \"su -c 'echo Root权限获取成功'\"";
                string suOutput = await ExecuteAdbCommandWithOutput(suCommand);

                if (string.IsNullOrWhiteSpace(suOutput) || !suOutput.Contains("Root权限获取成功"))
                {
                    LogSimpleStatus("[ERROR]设备未授予Shell ROOT权限");
                    return;
                }

                // 开启Diag端口
                string diagCommand = "shell \"su -c 'setprop sys.usb.config diag,adb'\"";
                string diagOutput = await ExecuteAdbCommandWithOutput(diagCommand);

                if (CommandOutputIndicatesFailure(diagOutput))
                {
                    LogSimpleStatus($"错误: 基带调试端口启用失败 | {diagOutput.Trim()}");
                    return;
                }

                LogSimpleStatus("基带调试端口已开启.");
                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"错误: 基带调试端口启用失败 | {ex.Message}");
            }
            finally
            {
                EnableDiagPortButton.IsEnabled = true;
            }
        }

        private async Task<string?> GetAuthorizedAdbDeviceSerialAsync()
        {
            string deviceOutput = await ExecuteAdbCommandWithOutput("devices");
            var deviceMatch = Regex.Match(
                deviceOutput ?? string.Empty,
                @"(?m)^(\S+)\s+device\s*$",
                RegexOptions.IgnoreCase);
            return deviceMatch.Success ? deviceMatch.Groups[1].Value : null;
        }

        private static bool CommandOutputIndicatesFailure(string? output)
        {
            return !string.IsNullOrWhiteSpace(output) &&
                   (output.Contains("ERROR_DETECTED", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("denied", StringComparison.OrdinalIgnoreCase));
        }

        // 切换文件传输模式按钮
        private async void SwitchToMtpButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchToMtpButton.IsEnabled = false;
            var taskStopwatch = Stopwatch.StartNew();

            try
            {
                string? deviceSerial = await GetAuthorizedAdbDeviceSerialAsync();
                if (string.IsNullOrEmpty(deviceSerial))
                {
                    LogSimpleStatus("错误: 未检测到已授权的 ADB 设备");
                    return;
                }

                LogSimpleStatus($"已连接 {deviceSerial} | ADB");

                // 切换到MTP模式
                string mtpCommand = "shell svc usb setFunctions mtp";
                string mtpOutput = await ExecuteAdbCommandWithOutput(mtpCommand);

                if (CommandOutputIndicatesFailure(mtpOutput))
                {
                    LogSimpleStatus($"错误: MTP 模式切换失败 | {mtpOutput.Trim()}");
                    return;
                }

                LogSimpleStatus("操作完成 | USB 文件传输模式已启用");
                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"错误: MTP 模式切换失败 | {ex.Message}");
            }
            finally
            {
                SwitchToMtpButton.IsEnabled = true;
            }
        }

        private async void ScreenOffButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell input keyevent 223");
                ShowMessage("屏幕已关闭");
            }
            catch (Exception ex)
            {
                ShowMessage($"屏幕关闭失败: {ex.Message}");
            }
        }

        private async void VolumeMuteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell input keyevent 164");
                ShowMessage("静音键已发送");
            }
            catch (Exception ex)
            {
                ShowMessage($"静音失败: {ex.Message}");
            }
        }

        private async void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell screencap -p /sdcard/screenshot.png");
                await ExecuteAdbCommand("pull /sdcard/screenshot.png");
                ShowMessage("截屏已保存到当前目录");
            }
            catch (Exception ex)
            {
                ShowMessage($"截屏失败: {ex.Message}");
            }
        }

        private async Task ExecuteAdbCommand(string command)
        {
            await Task.Run(() =>
            {
                string adbPath = GetToolPath("adb.exe");
                string selectedSerial = GetSelectedDeviceSerial();
                
                // 如果有选中的设备序列号，添加 -s 参数
                string arguments = command;
                if (!string.IsNullOrEmpty(selectedSerial))
                {
                    arguments = $"-s {selectedSerial} {command}";
                }
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
            });
        }

        private async Task ExecuteFastbootCommand(string command)
        {
            try
            {
                string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string fastbootPath = Path.Combine(programDirectory, "platform-tools", "fastboot.exe");
                string selectedSerial = GetSelectedDeviceSerial();
                
                // 如果有选中的设备序列号，添加 -s 参数
                string arguments = command;
                if (!string.IsNullOrEmpty(selectedSerial))
                {
                    arguments = $"-s {selectedSerial} {command}";
                }
                
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fastbootPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.Start();

                    // 设置10秒超时
                    if (await Task.Run(() => process.WaitForExit(10000)))
                    {
                        string output = await process.StandardOutput.ReadToEndAsync();
                        string error = await process.StandardError.ReadToEndAsync();
                        
                    }
                    else
                    {
                        process.Kill();
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }

        private async Task CheckDeviceStatus(bool waitForCurrentDetection = false)
        {
            // 防止重复检测
            if (!_isDeviceDetectionEnabled) return;

            bool detectionLockTaken;
            if (waitForCurrentDetection)
            {
                await _deviceDetectionLock.WaitAsync();
                detectionLockTaken = true;
            }
            else
            {
                detectionLockTaken = _deviceDetectionLock.Wait(0);
            }
            if (!detectionLockTaken) return;

            if (!_isDeviceDetectionEnabled)
            {
                _deviceDetectionLock.Release();
                return;
            }

            int detectionVersion = _deviceDetectionVersion;
            
            string status;
            string connectionType;
            
            // 不显示"正在检测中"状态，保持界面静默直到检测完成
            
            try
            {
                string adbPath = GetToolPath("adb.exe");
                string fastbootPath = GetToolPath("fastboot.exe");

                // 检查工具是否存在
                if (!File.Exists(adbPath) && !File.Exists(fastbootPath))
                {
                    status = "缺少ADB/Fastboot";
                    connectionType = "未找到platform-tools";
                    
                    string windowsVersion = await GetWindowsVersionAsync();
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                    if (HasDeviceInfoChanged(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateDeviceInfoUI(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion);
                            if (BuildDateText != null) BuildDateText.Text = "--";
                        });
                        UpdateLastDeviceInfo(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion);
                    }
                    return;
                }

                // 并行执行ADB和Fastboot设备检测
                var adbTask = GetCommandOutput(adbPath, "devices");
                var fastbootTask = GetCommandOutput(fastbootPath, "devices");
                
                // 等待两个任务完成
                var results = await Task.WhenAll(adbTask, fastbootTask);
                if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;

                string adbDevicesOutput = results[0];
                string fastbootDevicesOutput = results[1];
                
                bool hasAdbDevice = adbDevicesOutput.Contains("\tdevice");
                bool hasFastbootDevice = fastbootDevicesOutput.Contains("fastboot");
                bool hasUnauthorizedDevice = adbDevicesOutput.Contains("\tunauthorized");
                bool hasOfflineDevice = adbDevicesOutput.Contains("\toffline");
                
                // 如果两种设备都没有连接
                if (!hasAdbDevice && !hasFastbootDevice)
                {
                    status = hasUnauthorizedDevice ? "设备未授权" : (hasOfflineDevice ? "设备离线" : "未连接");
                    connectionType = hasUnauthorizedDevice ? "请在手机屏幕上允许USB调试" : (hasOfflineDevice ? "请重新插拔USB数据线" : "等待设备连接...");
                    
                    // 清空设备序列号列表
                    Dispatcher.Invoke(() =>
                    {
                        if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                        DeviceSerials.Clear();
                    });
                    
                    string windowsVersion = await GetWindowsVersionAsync();
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                    if (HasDeviceInfoChanged(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateDeviceInfoUI(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion);
                            if (BuildDateText != null) BuildDateText.Text = "--";
                        });
                        UpdateLastDeviceInfo(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion);
                    }
                    return;
                }

                // 收集所有设备序列号（ADB和Fastboot）
                var allDeviceSerials = new List<string>();
                
                // 收集ADB设备序列号
                if (hasAdbDevice)
                {
                    var lines = adbDevicesOutput.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("\tdevice"))
                        {
                            var serial = line.Split('\t')[0].Trim();
                            allDeviceSerials.Add($"{serial} (ADB)");
                        }
                    }
                }
                
                // 收集Fastboot设备序列号
                if (hasFastbootDevice)
                {
                    var lines = fastbootDevicesOutput.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("fastboot"))
                        {
                            var serial = line.Split('\t')[0].Trim();
                            allDeviceSerials.Add($"{serial} (Fastboot)");
                        }
                    }
                }
                
                // 更新设备序列号列表
                if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                Dispatcher.Invoke(() =>
                {
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;

                    // 保存当前选中的设备序列号
                    string currentSelectedSerial = MultiDeviceComboBox.SelectedItem as string;
                    
                    DeviceSerials.Clear();
                    foreach (var serial in allDeviceSerials)
                    {
                        DeviceSerials.Add(serial);
                    }
                    
                    // 尝试恢复之前选中的设备
                    if (!string.IsNullOrEmpty(currentSelectedSerial) && DeviceSerials.Contains(currentSelectedSerial))
                    {
                        MultiDeviceComboBox.SelectedItem = currentSelectedSerial;
                    }
                    // 如果之前选中的设备不存在或没有选中项，选中第一个
                    else if (MultiDeviceComboBox.SelectedItem == null && DeviceSerials.Count > 0)
                    {
                        MultiDeviceComboBox.SelectedIndex = 0;
                    }
                });

                // 根据当前选中的设备显示详细信息
                string selectedDevice = "";
                Dispatcher.Invoke(() =>
                {
                    selectedDevice = MultiDeviceComboBox.SelectedItem as string ?? "";
                });

                if (hasAdbDevice && (selectedDevice.Contains("(ADB)") || !selectedDevice.Contains("(Fastboot)")))
                {
                    // 获取选中的ADB设备序列号
                    string deviceSerial = "--";
                    if (selectedDevice.Contains("(ADB)"))
                    {
                        deviceSerial = selectedDevice.Replace(" (ADB)", "").Trim();
                    }
                    else
                    {
                        // 如果没有选中特定设备，使用第一个ADB设备
                        var lines = adbDevicesOutput.Split('\n');
                        foreach (var line in lines)
                        {
                            if (line.Contains("\tdevice"))
                            {
                                deviceSerial = line.Split('\t')[0].Trim();
                                break;
                            }
                        }
                    }
                    
                    // 并行获取设备信息（使用-s参数指定设备）
                    var deviceModelTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.product.model");
                    var marketNameTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.product.marketname");
                    var deviceCodeTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.product.device");
                    var androidVersionTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.build.version.release");
                    var unlockStatusTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.boot.unlocked");
                    var flashLockedTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.boot.flash.locked");
                    var slotSuffixTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.boot.slot_suffix");
                    var selinuxStatusTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getenforce");
                    var kernelVersionTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell uname -r");
                    var cpuInfoTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell cat /proc/cpuinfo");
                    var socModelTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.soc.model");
                    var socManufacturerTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.soc.manufacturer");
                    var boardPlatformTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.board.platform");
                    var hardwareTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.hardware");
                    
                    // 等待所有任务完成
                    var deviceInfoResults = await Task.WhenAll(
                        deviceModelTask, marketNameTask, deviceCodeTask, androidVersionTask, 
                        unlockStatusTask, slotSuffixTask, selinuxStatusTask, kernelVersionTask, flashLockedTask, cpuInfoTask,
                        socModelTask, socManufacturerTask, boardPlatformTask, hardwareTask
                    );
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                    
                    string deviceModel = deviceInfoResults[0];
                    string marketName = deviceInfoResults[1];
                    string deviceCode = deviceInfoResults[2];
                    string androidVersion = deviceInfoResults[3];
                    string unlockStatus = deviceInfoResults[4];
                    string slotSuffix = deviceInfoResults[5];
                    string selinuxStatus = deviceInfoResults[6];
                    string kernelVersion = deviceInfoResults[7];
                    string flashLocked = deviceInfoResults[8];
                    string cpuInfo = deviceInfoResults[9];
                    string socModel = deviceInfoResults[10];
                    string socManufacturer = deviceInfoResults[11];
                    string boardPlatform = deviceInfoResults[12];
                    string hardware = deviceInfoResults[13];

                    string rawMarket = marketName.Trim();
                    string rawModel = deviceModel.Trim();
                    if (!string.IsNullOrEmpty(rawMarket))
                    {
                        deviceModel = rawMarket;
                    }
                    else if (!string.IsNullOrEmpty(rawModel))
                    {
                        deviceModel = rawModel;
                    }

                    string procVersion = await GetCommandOutput(adbPath, $"-s {deviceSerial} shell cat /proc/version");
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;

                    string buildDate = ExtractBuildDateFromProcVersion(procVersion).Trim();
                    if (string.IsNullOrEmpty(buildDate)) buildDate = "--";
                    if (buildDate != lastBuildDate)
                    {
                        Dispatcher.Invoke(() => { if (BuildDateText != null) BuildDateText.Text = buildDate; });
                        lastBuildDate = buildDate;
                    }

                    // 解析电池信息
                    string batteryOutput = await GetCommandOutput(adbPath, $"-s {deviceSerial} shell dumpsys battery");
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                    UpdateBatteryFromDumpsysOutput(batteryOutput, detectionVersion);
                    
                    // 直接获取CPU硬件代号 (纯粹读取硬件SoC代号属性，绝不直接读取任何CPU营销名称)
                    string cpuManufacturer = "--";
                    string cpuCodeName = "--";

                    string trimmedSoc = socModel.Trim().ToUpperInvariant();
                    if (!string.IsNullOrEmpty(trimmedSoc) && trimmedSoc != "--")
                    {
                        cpuCodeName = trimmedSoc;
                    }

                    if (cpuCodeName == "--" && !string.IsNullOrEmpty(boardPlatform))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(boardPlatform.Trim(), @"(SM\d+|MT\d+|HI\d+|SDM\d+|MSM\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            cpuCodeName = match.Value.ToUpperInvariant();
                        }
                    }

                    if (cpuCodeName == "--" && !string.IsNullOrEmpty(cpuInfo))
                    {
                        var cpuInfoLines = cpuInfo.Split('\n');
                        foreach (var cpuLine in cpuInfoLines)
                        {
                            if (cpuLine.StartsWith("Hardware", StringComparison.OrdinalIgnoreCase))
                            {
                                var hardwareLine = cpuLine.Trim();
                                var match = System.Text.RegularExpressions.Regex.Match(hardwareLine, @"(SM\d+|MT\d+|HI\d+|SDM\d+|MSM\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (match.Success)
                                {
                                    cpuCodeName = match.Value.ToUpperInvariant();
                                }
                                break;
                            }
                        }
                    }

                    // 根据CPU硬件代号推断CPU厂商
                    if (cpuCodeName.StartsWith("SM") || cpuCodeName.StartsWith("SDM") || cpuCodeName.StartsWith("MSM") || cpuCodeName.StartsWith("QCM"))
                    {
                        cpuManufacturer = "高通骁龙";
                    }
                    else if (cpuCodeName.StartsWith("MT"))
                    {
                        cpuManufacturer = "联发科";
                    }
                    else if (cpuCodeName.StartsWith("HI") || cpuCodeName.Contains("KIRIN"))
                    {
                        cpuManufacturer = "华为海思";
                    }
                    else
                    {
                        string combinedSocInfo = (socManufacturer + " " + hardware + " " + boardPlatform + " " + cpuInfo).ToUpperInvariant();
                        if (combinedSocInfo.Contains("QTI") || combinedSocInfo.Contains("QUALCOMM") || combinedSocInfo.Contains("QCOM"))
                        {
                            cpuManufacturer = "高通骁龙";
                        }
                        else if (combinedSocInfo.Contains("MEDIATEK") || combinedSocInfo.Contains("MTK"))
                        {
                            cpuManufacturer = "联发科";
                        }
                    }
                    
                    // 使用多种方法检测解锁状态
                    string unlockText;
                    string trimmedUnlockStatus = unlockStatus.Trim();
                    string trimmedFlashLocked = flashLocked.Trim();
                    
                    // 调试输出：显示实际获取到的值
                    System.Diagnostics.Debug.WriteLine($"ro.boot.unlocked原始值: '{unlockStatus}'");
                    System.Diagnostics.Debug.WriteLine($"ro.boot.unlocked修剪后: '{trimmedUnlockStatus}'");
                    System.Diagnostics.Debug.WriteLine($"ro.boot.flash.locked原始值: '{flashLocked}'");
                    System.Diagnostics.Debug.WriteLine($"ro.boot.flash.locked修剪后: '{trimmedFlashLocked}'");
                    
                    // 优先使用ro.boot.unlocked
                    if (trimmedUnlockStatus == "1")
                    {
                        unlockText = "已解锁";
                    }
                    else if (trimmedUnlockStatus == "0")
                    {
                        unlockText = "未解锁";
                    }
                    // 如果ro.boot.unlocked不可用，使用ro.boot.flash.locked
                    else if (trimmedFlashLocked == "0")
                    {
                        unlockText = "已解锁";
                    }
                    else if (trimmedFlashLocked == "1")
                    {
                        unlockText = "未解锁";
                    }
                    else
                    {
                        // 两个属性都不可用
                        unlockText = "--";
                    }
                    
                    string slotText = slotSuffix.Trim() == "_a" ? "A槽位" :
                                     slotSuffix.Trim() == "_b" ? "B槽位" : "--";
                    string selinuxText = NormalizeSelinuxStatus(selinuxStatus);
                    
                    status = "已连接";
                    connectionType = "系统";
                    
                    string trimmedModel = deviceModel.Trim();
                    string trimmedCode = deviceCode.Trim();
                    string trimmedAndroidVersion = androidVersion.Trim();
                    string trimmedKernelVersion = kernelVersion.Trim();
                    
                    string windowsVersion = await GetWindowsVersionAsync();
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                    if (HasDeviceInfoChanged(status, connectionType, deviceSerial, trimmedModel, trimmedCode, trimmedAndroidVersion, unlockText, slotText, selinuxText, trimmedKernelVersion, cpuManufacturer, cpuCodeName, windowsVersion))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateDeviceInfoUI(
                                status, 
                                connectionType, 
                                deviceSerial,
                                trimmedModel, 
                                trimmedCode, 
                                trimmedAndroidVersion, 
                                unlockText, 
                                slotText, 
                                selinuxText,
                                trimmedKernelVersion,
                                cpuManufacturer,
                                cpuCodeName,
                                windowsVersion
                            );
                        });
                        UpdateLastDeviceInfo(status, connectionType, deviceSerial, trimmedModel, trimmedCode, trimmedAndroidVersion, unlockText, slotText, selinuxText, trimmedKernelVersion, cpuManufacturer, cpuCodeName, windowsVersion);
                        _ = RefreshStorageMemoryAsync(silent: true);
                    }
                    return;
                }

                // 处理Fastboot设备（使用前面已检查的结果）
                else if (hasFastbootDevice && selectedDevice.Contains("(Fastboot)"))
                {
                    // 获取选中的Fastboot设备序列号
                    string deviceSerial = selectedDevice.Replace(" (Fastboot)", "").Trim();
                    
                    // 并行获取Fastboot设备信息（使用-s参数指定设备）
                    var productInfoTask = GetCommandOutput(fastbootPath, $"-s {deviceSerial} getvar product");
                    var unlockInfoTask = GetCommandOutput(fastbootPath, $"-s {deviceSerial} getvar unlocked");
                    var deviceInfoTask = GetCommandOutput(fastbootPath, $"-s {deviceSerial} oem device-info");
                    var slotInfoTask = GetCommandOutput(fastbootPath, $"-s {deviceSerial} getvar current-slot");
                    
                    // 等待所有任务完成
                    var fastbootInfoResults = await Task.WhenAll(
                        productInfoTask, unlockInfoTask, deviceInfoTask, slotInfoTask
                    );
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                    
                    string productInfo = fastbootInfoResults[0];
                    string unlockInfo = fastbootInfoResults[1];
                    string deviceInfo = fastbootInfoResults[2];
                    string slotInfo = fastbootInfoResults[3];
                    
                    string productName = ExtractFastbootVar(productInfo, "product");
                    string unlockStatus = ExtractFastbootVar(unlockInfo, "unlocked");
                    string currentSlot = ExtractFastbootVar(slotInfo, "current-slot");
                    
                    // 调试输出：显示fastboot获取到的值
                    System.Diagnostics.Debug.WriteLine($"fastboot oem device-info输出: '{deviceInfo}'");
                    System.Diagnostics.Debug.WriteLine($"fastboot getvar unlocked输出: '{unlockInfo}'");
                    System.Diagnostics.Debug.WriteLine($"提取的unlocked值: '{unlockStatus}'");
                    
                    // 优先使用 fastboot oem device-info 检测解锁状态
                    string unlockText = "--";
                    if (deviceInfo.Contains("Device unlocked: true"))
                    {
                        unlockText = "已解锁";
                        System.Diagnostics.Debug.WriteLine("使用device-info检测到已解锁");
                    }
                    else if (deviceInfo.Contains("Device unlocked: false"))
                    {
                        unlockText = "未解锁";
                        System.Diagnostics.Debug.WriteLine("使用device-info检测到未解锁");
                    }
                    else
                    {
                        // 回退到 getvar unlocked 方法
                        System.Diagnostics.Debug.WriteLine("device-info方法失败，回退到getvar方法");
                        unlockText = unlockStatus == "yes" ? "已解锁" :
                                    unlockStatus == "no" ? "未解锁" : "--";
                        System.Diagnostics.Debug.WriteLine($"最终解锁状态: '{unlockText}'");
                    }
                    
                    string slotText = currentSlot == "a" ? "A槽位" :
                                     currentSlot == "b" ? "B槽位" : "--";
                    
                    status = "已连接";
                    connectionType = "Fastboot";
                    
                    string windowsVersion = await GetWindowsVersionAsync();
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                    if (HasDeviceInfoChanged(status, connectionType, deviceSerial, productName, productName, "--", unlockText, slotText, "--", "--", "--", "--", windowsVersion))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateDeviceInfoUI(
                                status, 
                                connectionType, 
                                deviceSerial,
                                productName, 
                                productName, 
                                "--", 
                                unlockText, 
                                slotText, 
                                "--",
                                "--",
                                "--",
                                "--",
                                windowsVersion
                            );
                            if (BuildDateText != null) BuildDateText.Text = "--";
                        });
                        UpdateLastDeviceInfo(status, connectionType, deviceSerial, productName, productName, "--", unlockText, slotText, "--", "--", "--", "--", windowsVersion);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;

                status = $"检测失败: {ex.Message}";
                connectionType = "错误";
                
                string windowsVersion = await GetWindowsVersionAsync();
                if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                if (HasDeviceInfoChanged(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion))
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateDeviceInfoUI(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion);
                        if (BuildDateText != null) BuildDateText.Text = "--";
                    });
                    UpdateLastDeviceInfo(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion);
                }
            }
            finally
            {
                _deviceDetectionLock.Release();
            }
        }

        private string NormalizeSelinuxStatus(string rawStatus)
        {
            string trimmedStatus = rawStatus.Trim();
            if (trimmedStatus.Equals("Enforcing", StringComparison.OrdinalIgnoreCase))
            {
                return "严格模式";
            }

            if (trimmedStatus.Equals("Permissive", StringComparison.OrdinalIgnoreCase))
            {
                return "宽容模式";
            }

            if (trimmedStatus.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            {
                return "已关闭";
            }

            return "--";
        }

        private void UpdateSelinuxStatusColor(string selinuxStatus)
        {
            if (SelinuxStatusText == null)
            {
                return;
            }

            if (selinuxStatus == "严格模式")
            {
                SelinuxStatusText.Foreground = System.Windows.Media.Brushes.Green;
            }
            else if (selinuxStatus == "宽容模式")
            {
                SelinuxStatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
            else if (selinuxStatus == "已关闭")
            {
                SelinuxStatusText.Foreground = System.Windows.Media.Brushes.DarkOrange;
            }
            else
            {
                SelinuxStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
            }
        }

        private void UpdateDeviceInfoUI(string status, string connectionType, string serial, string model, string code, string androidVersion, string unlockStatus, string abPartition, string selinuxStatus, string kernelVersion, string cpuManufacturer, string cpuCodeName, string windowsVersion)
        {
            SetLocalizedText(DeviceStatusText, status);
            SetLocalizedText(ConnectionTypeText, connectionType);
            DeviceSerialText.Text = serial;
            DeviceModelText.Text = model;
            DeviceCodeText.Text = code;
            AndroidVersionText.Text = androidVersion;
            SetLocalizedText(UnlockStatusText, unlockStatus);
            SetLocalizedText(ABPartitionText, abPartition);
            SetLocalizedText(SelinuxStatusText, selinuxStatus);
            KernelVersionText.Text = kernelVersion;
            CpuManufacturerText.Text = cpuManufacturer;
            CpuCodeNameText.Text = cpuCodeName;
            WindowsVersionText.Text = windowsVersion;
            UpdateSelinuxStatusColor(selinuxStatus);
            
            // 根据CPU代号更新CPU名称
            CpuNameText.Text = GetCpuNameByCode(cpuCodeName);
            
            // 更新版本信息
            if (status == "已连接" && connectionType == "系统" && !string.IsNullOrEmpty(code) && code != "--")
            {
                _ = UpdateVersionInfoAsync(code, _deviceDetectionVersion);
            }
            else
            {
                VersionInfoText.Text = "--";
            }
            
            // 更新底部的设备类型显示文本
            if (BottomConnectionTypeText != null)
            {
                SetLocalizedText(BottomConnectionTypeText, connectionType);
            }
            UpdateBottomConnectionStatusIndicator(status, connectionType);
            
            // 更新状态文字颜色
            UpdateStatusTextColor(status, connectionType);
            
            // 更新分区操作按钮状态
            UpdatePartitionButtonStates();
            UpdateXiaomiScriptOnlyOptionsState();
        }
        
        private bool HasDeviceInfoChanged(string status, string connectionType, string serial, string model, string code, string androidVersion, string unlockStatus, string abPartition, string selinuxStatus, string kernelVersion, string cpuManufacturer, string cpuCodeName, string windowsVersion)
        {
            return lastDeviceStatus != status ||
                   lastConnectionType != connectionType ||
                   lastDeviceSerial != serial ||
                   lastDeviceModel != model ||
                   lastDeviceCode != code ||
                   lastAndroidVersion != androidVersion ||
                   lastUnlockStatus != unlockStatus ||
                   lastABPartition != abPartition ||
                   lastSelinuxStatus != selinuxStatus ||
                   lastKernelVersion != kernelVersion ||
                   lastCpuManufacturer != cpuManufacturer ||
                   lastCpuCodeName != cpuCodeName ||
                   lastWindowsVersion != windowsVersion;
        }
        
        private void UpdateLastDeviceInfo(string status, string connectionType, string serial, string model, string code, string androidVersion, string unlockStatus, string abPartition, string selinuxStatus, string kernelVersion, string cpuManufacturer, string cpuCodeName, string windowsVersion)
        {
            lastDeviceStatus = status;
            lastConnectionType = connectionType;
            lastDeviceSerial = serial;
            lastDeviceModel = model;
            lastDeviceCode = code;
            lastAndroidVersion = androidVersion;
            lastUnlockStatus = unlockStatus;
            lastABPartition = abPartition;
            lastSelinuxStatus = selinuxStatus;
            lastKernelVersion = kernelVersion;
            lastCpuManufacturer = cpuManufacturer;
            lastCpuCodeName = cpuCodeName;
            lastWindowsVersion = windowsVersion;
        }
        
        private async Task UpdateVersionInfoAsync(string deviceCode, int detectionVersion)
        {
            try
            {
                string command;
                // 根据设备代号首字母大小写决定使用哪个命令
                if (!string.IsNullOrEmpty(deviceCode) && char.IsUpper(deviceCode[0]))
                {
                    // 首字母大写，使用 ro.build.display.id
                    command = "shell getprop ro.build.display.id";
                }
                else
                {
                    // 首字母小写，使用 ro.build.version.incremental
                    command = "shell getprop ro.build.version.incremental";
                }
                
                string adbPath = GetToolPath("adb.exe");
                if (string.IsNullOrEmpty(adbPath))
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (IsDeviceDetectionCycleCurrent(detectionVersion))
                        {
                            VersionInfoText.Text = "--";
                        }
                    });
                    return;
                }
                
                // 获取选中的设备序列号
                string selectedSerial = GetSelectedDeviceSerial();
                string finalCommand = command;
                
                // 如果有选中的设备序列号，添加 -s 参数
                if (!string.IsNullOrEmpty(selectedSerial))
                {
                    finalCommand = $"-s {selectedSerial} {command}";
                }
                
                string versionInfo = await GetCommandOutput(adbPath, finalCommand);
                string trimmedVersionInfo = versionInfo.Trim();
                
                // 在UI线程中更新界面
                Dispatcher.Invoke(() =>
                {
                    if (IsDeviceDetectionCycleCurrent(detectionVersion))
                    {
                        VersionInfoText.Text = string.IsNullOrEmpty(trimmedVersionInfo) ? "--" : trimmedVersionInfo;
                    }
                });
            }
            catch (Exception ex)
            {
                // 发生错误时显示未知
                Dispatcher.Invoke(() =>
                {
                    if (IsDeviceDetectionCycleCurrent(detectionVersion))
                    {
                        VersionInfoText.Text = "--";
                    }
                });
            }
        }
        
        private void UpdateStatusTextColor(string status, string connectionType)
        {
            if (DeviceStatusText != null)
            {
                if (status == "正在检测中")
                {
                    // 正在检测 - 黄色
                    DeviceStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 165, 0)); // #FFA500
                }
                else if (status == "已连接")
                {
                    // 已连接 - 绿色
                    DeviceStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // #28A745
                }
                else
                {
                    // 未连接 - 红色
                    DeviceStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // #DC3545
                }
            }
        }

        private void UpdateBottomConnectionStatusIndicator(string status, string connectionType)
        {
            if (BottomConnectionStatusIndicator == null)
            {
                return;
            }

            bool isOnline = status == "已连接" &&
                            !string.IsNullOrWhiteSpace(connectionType) &&
                            connectionType != "--";

            BottomConnectionStatusIndicator.Visibility = isOnline
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        
        private string ExtractFastbootVar(string output, string varName)
        {
            try
            {
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    string trimmedLine = line.Trim();
                    
                    // 处理多种可能的fastboot输出格式
                    // 格式1: varName: value
                    if (trimmedLine.Contains($"{varName}:"))
                    {
                        var parts = trimmedLine.Split(':');
                        if (parts.Length >= 2)
                        {
                            string value = parts[1].Trim();
                            System.Diagnostics.Debug.WriteLine($"提取变量 {varName}: '{value}' (格式1)");
                            return value;
                        }
                    }
                    
                    // 格式2: (bootloader) varName: value
                    if (trimmedLine.Contains("(bootloader)") && trimmedLine.Contains($"{varName}:"))
                    {
                        int colonIndex = trimmedLine.IndexOf(':');
                        if (colonIndex > 0 && colonIndex < trimmedLine.Length - 1)
                        {
                            string value = trimmedLine.Substring(colonIndex + 1).Trim();
                            System.Diagnostics.Debug.WriteLine($"提取变量 {varName}: '{value}' (格式2)");
                            return value;
                        }
                    }
                }
                System.Diagnostics.Debug.WriteLine($"未找到变量 {varName}");
                return "--";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"提取变量 {varName} 时出错: {ex.Message}");
                return "--";
            }
        }

        private static bool IsMissingFastbootValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ||
                   value.Equals("--", StringComparison.OrdinalIgnoreCase);
        }

        private string ExtractBuildDateFromProcVersion(string output)
        {
            // 内核版本构建时间逻辑
            try
            {
                if (string.IsNullOrWhiteSpace(output)) return string.Empty;
                var mFull = System.Text.RegularExpressions.Regex.Match(output, @"#\d+[^\n]*\d{2}:\d{2}:\d{2}[^\n]*\d{4}");
                if (mFull.Success) return mFull.Value.Trim();
                int idx = output.IndexOf('#');
                if (idx >= 0)
                {
                    string tail = output.Substring(idx).Trim();
                    var parts = tail.Split('\n');
                    if (parts.Length > 0) return parts[0].Trim();
                }
                return output.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }
        
        private async Task<string> GetCommandOutput(
            string fileName,
            string arguments,
            CancellationToken cancellationToken = default)
        {
            Process? process = null;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                linkedCts.Token.ThrowIfCancellationRequested();
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8,
                        StandardErrorEncoding = System.Text.Encoding.UTF8
                    }
                };
                process.Start();
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(linkedCts.Token);
                string output = await outputTask;
                string error = await errorTask;

                // 合并标准输出和标准错误，因为fastboot很多信息输出到stderr
                string combinedOutput = output + "\n" + error;
                System.Diagnostics.Debug.WriteLine($"命令: {fileName} {arguments}");
                System.Diagnostics.Debug.WriteLine($"完整输出: '{combinedOutput}'");
                return combinedOutput;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (process != null && !process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }
                if (cancellationToken.IsCancellationRequested) throw;
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                process?.Dispose();
            }
        }

        private string GetToolPath(string toolName)
        {
            var candidates = new List<string>
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", toolName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, toolName),
                Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "platform-tools", toolName),
                Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", toolName),
                Path.Combine(Environment.CurrentDirectory, "platform-tools", toolName),
                Path.Combine(Environment.CurrentDirectory, toolName),
                Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.FullName ?? "", "platform-tools", toolName),
                Path.Combine(Directory.GetParent(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.FullName ?? "")?.FullName ?? "", "platform-tools", toolName),
                Path.Combine(Directory.GetParent(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.FullName ?? "")?.FullName ?? "", "VioletToolBox", "platform-tools", toolName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", toolName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Android", "Sdk", "platform-tools", toolName),
                @"C:\platform-tools\" + toolName,
                @"D:\platform-tools\" + toolName
            };

            foreach (var path in candidates)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }
            }

            try
            {
                string? pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathEnv))
                {
                    foreach (var rawDir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string dir = rawDir.Trim().Trim('"');
                        if (string.IsNullOrEmpty(dir)) continue;
                        try
                        {
                            string full = Path.Combine(dir, toolName);
                            if (File.Exists(full))
                            {
                                return full;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            string defaultAppToolPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", toolName);
            if (File.Exists(defaultAppToolPath))
            {
                return defaultAppToolPath;
            }

            return toolName;
        }

        // 根据 dumpsys battery 输出更新电池控件
        private void UpdateBatteryFromDumpsysOutput(string output, int detectionVersion)
        {
            if (!IsDeviceDetectionCycleCurrent(detectionVersion))
            {
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(output)) return;

                int level = TryGetIntFromBattery(output, "level");
                int scale = TryGetIntFromBattery(output, "scale");
                int tempTenth = TryGetIntFromBattery(output, "temperature");

                // 判断是否在充电
                bool isCharging =
                    Regex.IsMatch(output, @"AC powered:\s*true", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(output, @"USB powered:\s*true", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(output, @"Wireless powered:\s*true", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(output, @"status:\s*2", RegexOptions.IgnoreCase) ||
                    (TryGetIntFromBattery(output, "plugged") > 0);

                double tempC = tempTenth > 0 ? tempTenth / 10.0 : double.NaN;
                string tempText = tempTenth > 0 ? $"{tempC:F1} °C" : "--";

                Dispatcher.Invoke(() =>
                {
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion))
                    {
                        return;
                    }

                    if (BatteryProgressBar != null)
                    {
                        BatteryProgressBar.Maximum = scale > 0 ? scale : 100;
                        BatteryProgressBar.Value = Math.Clamp(level, 0, BatteryProgressBar.Maximum);
                    }
                    if (BatteryValueText != null)
                    {
                        BatteryValueText.Text = $"{level}%";
                    }
                    if (BatteryDetailText != null)
                    {
                        string chargingStr = isCharging ? "充电中" : "未充电";
                        BatteryDetailText.Text = tempText != "--" ? $"{chargingStr} · {tempText}" : chargingStr;
                    }
                });
            }
            catch
            {
                // 忽略解析异常，避免影响主流程
            }
        }

        private static int TryGetIntFromBattery(string output, string key)
        {
            var m = Regex.Match(output, key + @":\s*(\d+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                if (int.TryParse(m.Groups[1].Value, out int v))
                    return v;
            }
            return 0;
        }

        private void ShowMessage(string message)
        {
    }

        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        private const int OBJID_WINDOW = 0;
        private const int CHILDID_SELF = 0;
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_ASYNCWINDOWPOS = 0x4000;

        private delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint idEventThread,
            uint dwmsEventTime);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void BootFilePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Boot文件路径文本框内容变化事件处理
        }

        // 选择Boot镜像文件
        private void SelectBootFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择镜像文件",
                Filter = "镜像文件 (*.img)|*.img|所有文件 (*.*)|*.*",
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                BootFilePathTextBox.Text = openFileDialog.FileName;
                LogSimpleStatus($"已选择镜像{openFileDialog.FileName}");
            }
        }
        private enum FastbootProcessorType
        {
            Unknown,
            Qualcomm,
            MediaTek
        }

        private const int MediaTekBootloaderFlowMaxAttempts = 20;

        private static bool IsGuidedBootloaderCommand(string fastbootArgs)
        {
            return string.Equals(fastbootArgs, "flashing unlock", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fastbootArgs, "flashing lock", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBootloaderUnlockCommand(string fastbootArgs)
        {
            return fastbootArgs.EndsWith("unlock", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<FastbootProcessorType> DetectFastbootProcessorTypeAsync(string fastbootPath)
        {
            try
            {
                // Fastboot 没有统一的 SoC 查询变量，因此按分区表中的 LK 分区识别 MTK。
                // 检测命令保持静默，避免 getvar all 的大量输出污染用户日志。
                string partitionTable = await ExecuteFastbootCommand(
                    fastbootPath,
                    "getvar all",
                    showNativeOutput: false,
                    parseStatusOutput: false);

                if (string.IsNullOrWhiteSpace(partitionTable) ||
                    CommandOutputIndicatesFailure(partitionTable))
                {
                    return FastbootProcessorType.Unknown;
                }

                bool hasLkPartition = Regex.IsMatch(
                    partitionTable,
                    @"(?im)\b(?:partition-size|partition-type|has-slot):lk(?:_[ab])?\s*:",
                    RegexOptions.CultureInvariant);

                return hasLkPartition
                    ? FastbootProcessorType.MediaTek
                    : FastbootProcessorType.Qualcomm;
            }
            catch
            {
                return FastbootProcessorType.Unknown;
            }
        }

        private async Task ExecuteBootloaderCommandOnceAsync(
            string fastbootPath,
            string selectedCommand,
            string fastbootArgs)
        {
            LogSimpleStatus($"> {selectedCommand}");
            string commandResult = await ExecuteFastbootCommand(
                fastbootPath,
                fastbootArgs,
                showNativeOutput: true);

            if (string.IsNullOrWhiteSpace(commandResult))
            {
                LogSimpleStatus("警告: Fastboot 未返回任何输出");
            }
        }

        private async Task ExecuteMediaTekBootloaderFlowAsync(
            string fastbootPath,
            string selectedCommand,
            string fastbootArgs)
        {
            bool isUnlock = IsBootloaderUnlockCommand(fastbootArgs);
            string expectedFlow = isUnlock ? "Start unlock flow" : "Start lock flow";
            const string operationTip = "请盯住当前手机界面，正常情况下应该是有两串小英文显示\"fastboot mode\"，当界面英文突然变多时，请连续短按两次音量上键（音量+）.";

            for (int attempt = 1; attempt <= MediaTekBootloaderFlowMaxAttempts; attempt++)
            {
                LogSimpleStatus(operationTip);
                LogSimpleStatus($"> {selectedCommand}");

                string commandResult = await ExecuteFastbootCommand(
                    fastbootPath,
                    fastbootArgs,
                    showNativeOutput: true);

                if (CommandOutputIndicatesFailure(commandResult))
                {
                    LogSimpleStatus("[ERROR]Fastboot 指令执行失败，已停止重试");
                    return;
                }

                if (commandResult.Contains(expectedFlow, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (attempt < MediaTekBootloaderFlowMaxAttempts)
                {
                    await Task.Delay(500);
                }
            }

            LogSimpleStatus($"[ERROR]连续发送 {MediaTekBootloaderFlowMaxAttempts} 次指令后仍未进入 Bootloader 操作界面，已停止重试");
        }

        private async void LockBLButton_Click(object sender, RoutedEventArgs e)
        {
            LockBLButton.IsEnabled = false;
            try
            {
                // 获取ComboBox中选择的命令
                if (UnlockBLComboBox.SelectedItem is not ComboBoxItem selectedItem ||
                    string.IsNullOrWhiteSpace(selectedItem.Content?.ToString()))
                {
                    LogSimpleStatus("请先选择要执行的命令...");
                    return;
                }

                string selectedCommand = selectedItem.Content.ToString()!;
                 
                // 先检测设备连接
                string fastbootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "fastboot.exe");
                string deviceCheckResult = await ExecuteFastbootCommand(fastbootPath, "devices");
                
                if (string.IsNullOrWhiteSpace(deviceCheckResult) || !deviceCheckResult.Contains("\t"))
                {
                    LogSimpleStatus("未检测到设备，请确保设备处于Fastboot并已安装驱动...");
                    return;
                }
                 
                // 从完整命令中提取fastboot参数
                string fastbootArgs = selectedCommand.StartsWith("fastboot ", StringComparison.OrdinalIgnoreCase)
                    ? selectedCommand["fastboot ".Length..].Trim()
                    : selectedCommand.Trim();

                if (!IsGuidedBootloaderCommand(fastbootArgs))
                {
                    await ExecuteBootloaderCommandOnceAsync(fastbootPath, selectedCommand, fastbootArgs);
                    return;
                }

                FastbootProcessorType processorType = await DetectFastbootProcessorTypeAsync(fastbootPath);
                switch (processorType)
                {
                    case FastbootProcessorType.Qualcomm:
                        LogSimpleStatus("处理器类型：高通骁龙");
                        string actionText = IsBootloaderUnlockCommand(fastbootArgs)
                            ? "UNLOCK THE BOOTLOADER"
                            : "LOCK THE BOOTLOADER";
                        LogSimpleStatus($"请按两下音量下键，选择第二个\"{actionText}\"，然后按电源键确认.");
                        await ExecuteBootloaderCommandOnceAsync(fastbootPath, selectedCommand, fastbootArgs);
                        break;

                    case FastbootProcessorType.MediaTek:
                        LogSimpleStatus("处理器类型：联发科");
                        await ExecuteMediaTekBootloaderFlowAsync(fastbootPath, selectedCommand, fastbootArgs);
                        break;

                    default:
                        LogSimpleStatus("处理器类型：检测失败（跳过）");
                        await ExecuteBootloaderCommandOnceAsync(fastbootPath, selectedCommand, fastbootArgs);
                        break;
                }
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"命令执行失败: {ex.Message}");
            }
            finally
            {
                LockBLButton.IsEnabled = true;
            }
        }

        // 刷入Boot镜像
        private async void FlashBootButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 验证文件路径
                string bootFilePath = BootFilePathTextBox.Text.Trim();
                if (string.IsNullOrEmpty(bootFilePath))
                {
                    LogSimpleStatus("错误: 请先选择镜像文件");
                    return;
                }

                if (!File.Exists(bootFilePath))
                {
                    LogSimpleStatus("错误: 选择的镜像文件不存在");
                    return;
                }

                // 获取选择的设备序列号，确保整个过程使用同一设备
                string targetDeviceSerial = GetSelectedDeviceSerial();
                
                // 如果没有勾选"等待FB设备"且没有选择设备，则报错
                if (string.IsNullOrEmpty(targetDeviceSerial) && WaitForFastbootCheckBox.IsChecked != true)
                {
                    LogSimpleStatus("错误: 请先选择目标设备或勾选'等待FB设备'");
                    return;
                }

                // 禁用按钮防止重复操作
                FlashBootButton.IsEnabled = false;
                
                // 显示并初始化进度条
                bootflash.Visibility = Visibility.Visible;
                bootflash.Value = 0;
                UpdateTransferRateText("0MB/s");

                string selectedPartition = "boot";
                if (PartitionComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    selectedPartition = selectedItem.Content?.ToString() ?? "boot";
                }

                var imageInfo = new FileInfo(bootFilePath);
                var totalStopwatch = Stopwatch.StartNew();
                LogSimpleStatus($"{imageInfo.Name} > {selectedPartition}.img | {imageInfo.Length / 1024d / 1024d:F0}MB");

                // 检查Fastboot设备连接
                // 使用程序同目录中的flash文件夹中的fastboot.exe
                string fastbootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "fastboot.exe");
                
                // 检查fastboot.exe是否存在
                if (!File.Exists(fastbootPath))
                {
                    LogSimpleStatus($"错误: 未找到fastboot.exe文件，请确保文件存在于: {fastbootPath}");
                    bootflash.Value = 0;
                    UpdateTransferRateText("0MB/s");
                    FlashBootButton.IsEnabled = true;
                    return;
                }
                
                // 检查是否需要等待Fastboot设备
                bool deviceDetected = false;
                if (WaitForFastbootCheckBox.IsChecked == true)
                {
                    AppendFlashCountdown("等待Fastboot设备...60s");
                    
                    // 等待60秒检测设备
                    for (int i = 0; i < 60; i++)
                    {
                        // 如果没有指定设备序列号，检测任意Fastboot设备
                        string deviceCheckResult;
                        if (string.IsNullOrEmpty(targetDeviceSerial))
                        {
                            deviceCheckResult = await ExecuteFastbootCommand(fastbootPath, "devices");
                        }
                        else
                        {
                            deviceCheckResult = await ExecuteFastbootCommandWithSerial(fastbootPath, "devices", targetDeviceSerial);
                        }
                        
                        if (!string.IsNullOrEmpty(deviceCheckResult) && deviceCheckResult.Contains("fastboot"))
                        {
                            deviceDetected = true;
                            
                            // 如果之前没有设备序列号，从检测结果中提取
                            if (string.IsNullOrEmpty(targetDeviceSerial))
                            {
                                var lines = deviceCheckResult.Split('\n');
                                foreach (var line in lines)
                                {
                                    if (line.Contains("fastboot"))
                                    {
                                        targetDeviceSerial = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                                        break;
                                    }
                                }
                            }
                            
                            LogSimpleStatus($"已连接 {targetDeviceSerial} | 用时 {i + 1} 秒");
                            break;
                        }

                        await Task.Delay(1000); // 等待1秒
                        UpdateFlashCountdown($"等待Fastboot设备...{59 - i}s");
                    }
                    
                    if (!deviceDetected)
                    {
                        LogSimpleStatus("[失败] 连接超时，60 秒内未检测到 Fastboot 设备");
                        bootflash.Value = 0;
                        UpdateTransferRateText("0MB/s");
                        FlashBootButton.IsEnabled = true;
                        return;
                    }

                    // 检测到设备后等待片刻，避免设备刚进入 Fastboot 时立即刷入导致连接不稳定
                    AppendFlashCountdown("等待设备连接稳定...3s");
                    for (int remaining = 2; remaining >= 0; remaining--)
                    {
                        await Task.Delay(1000);
                        UpdateFlashCountdown($"等待设备连接稳定...{remaining}s");
                    }
                }
                else
                {
                    LogSimpleStatus($"[设备] 正在验证 {targetDeviceSerial}");
                    // 不等待，直接检查设备连接
                    if (string.IsNullOrEmpty(targetDeviceSerial))
                    {
                        LogSimpleStatus("错误: 请先选择目标设备");
                        bootflash.Value = 0;
                        UpdateTransferRateText("0MB/s");
                        FlashBootButton.IsEnabled = true;
                        return;
                    }
                    
                    string deviceCheckResult = await ExecuteFastbootCommandWithSerial(fastbootPath, "devices", targetDeviceSerial);
                    
                    if (string.IsNullOrEmpty(deviceCheckResult) || !deviceCheckResult.Contains("fastboot"))
                    {
                        LogSimpleStatus($"[失败] Fastboot 设备 {targetDeviceSerial} 不可用");
                        bootflash.Value = 0;
                        UpdateTransferRateText("0MB/s");
                        FlashBootButton.IsEnabled = true;
                        return;
                    }
                    
                    deviceDetected = true;
                }

                // 检测到设备处于fastboot模式后，自动停止设备检测
                // 创建一个模拟的按钮对象来调用Button_Click_1方法（停止检测设备）
                var simulatedStopButton = new System.Windows.Controls.Button();
                simulatedStopButton.Content = "停止检测设备";
                Button_Click_1(simulatedStopButton, null);

                bootflash.Value = 10; // 设备检测完成
                bootflash.Value = 20;

                // 执行刷入命令，进度将由fastboot实时输出控制
                string flashCommand = $"flash {selectedPartition} \"{bootFilePath}\""; 
                string flashResult = await ExecuteFastbootCommandWithSerial(fastbootPath, flashCommand, targetDeviceSerial);

                if (flashResult.StartsWith("ERROR_DETECTED"))
                {
                    LogSimpleStatus($"[失败] {GetFastbootErrorSummary(flashResult)}");
                    bootflash.Value = 0;
                    UpdateTransferRateText("0MB/s");
                    // 错误时也不隐藏进度条
                }
                else if (flashResult.Contains("OKAY", StringComparison.OrdinalIgnoreCase) ||
                         flashResult.Contains("finished", StringComparison.OrdinalIgnoreCase))
                {
                    LogSimpleStatus($"Flashing {selectedPartition}.img...OK");

                    // 检查是否需要自动重启
                    if (AutoRebootCheckBox.IsChecked == true)
                    {
                        LogSimpleStatus("[Rebooting]发送重启命令...");
                        bootflash.Value = 95;
                        
                        string rebootResult = await ExecuteFastbootCommandWithSerial(fastbootPath, "reboot", targetDeviceSerial);
                        
                        if (string.IsNullOrEmpty(rebootResult) ||
                            rebootResult.Contains("finished", StringComparison.OrdinalIgnoreCase) ||
                            rebootResult.Contains("OKAY", StringComparison.OrdinalIgnoreCase))
                        {
                        }
                        else
                        {
                            // 如果重启失败，显示错误信息
                            LogSimpleStatus($"[警告] 重启命令状态未知 | {GetFastbootErrorSummary(rebootResult)}");
                        }
                        
                        // 在执行完fastboot reboot命令后终止所有fastboot.exe进程
                        await KillAllFastbootProcesses();
                    }
                    
                    // 完成进度条
                    bootflash.Value = 100;
                    totalStopwatch.Stop();
                    LogSimpleStatus($"任务结束,耗时{totalStopwatch.Elapsed.TotalSeconds:F1}秒.");
                    await Task.Delay(2000); // 显示完成状态2秒
                    
                    // 镜像刷入完成后，自动开始设备检测
                    // 创建一个模拟的按钮对象来调用Button_Click_1方法（开始检测设备）
                    var simulatedStartButton = new System.Windows.Controls.Button();
                    simulatedStartButton.Content = "开始检测设备";
                    Button_Click_1(simulatedStartButton, null);
                    
                    // 刷写完成后进度条不隐藏，只重置为0
                    bootflash.Value = 0;
                    UpdateTransferRateText("0MB/s");
                }
                else if (flashResult.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    LogSimpleStatus($"[失败] {GetFastbootErrorSummary(flashResult)}");
                    bootflash.Value = 0;
                    UpdateTransferRateText("0MB/s");
                    // 失败时也不隐藏进度条
                }
                else
                {
                    LogSimpleStatus($"[警告] Fastboot 未返回明确结果 | {GetFastbootErrorSummary(flashResult)}");
                    // 如果没有明确的成功或失败标识，假设成功
                    bootflash.Value = 100;
                    await Task.Delay(2000);
                    
                    // 镜像刷入完成后，自动开始设备检测
                    // 创建一个模拟的按钮对象来调用Button_Click_1方法（开始检测设备）
                    var simulatedStartButton = new System.Windows.Controls.Button();
                    simulatedStartButton.Content = "开始检测设备";
                    Button_Click_1(simulatedStartButton, null);
                    
                    // 完成后不隐藏进度条，只重置为0
                    bootflash.Value = 0;
                    UpdateTransferRateText("0MB/s");
                }
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"[失败] 刷写过程中发生异常 | {ex.Message}");
                //发生异常时也不隐藏进度条，只重置为0
                bootflash.Value = 0;
                UpdateTransferRateText("0MB/s");
            }
            finally
            {
                // 重新启用按钮
                FlashBootButton.IsEnabled = true;
            }
        }

        private static string GetFastbootErrorSummary(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return "Fastboot 未返回详细信息";
            }

            var lines = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !line.Equals("ERROR_DETECTED", StringComparison.OrdinalIgnoreCase))
                .ToList();

            string? detail = lines.FirstOrDefault(line =>
                line.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("cannot", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("invalid", StringComparison.OrdinalIgnoreCase));

            detail ??= lines.FirstOrDefault();
            return string.IsNullOrWhiteSpace(detail) ? "Fastboot 未返回详细信息" : detail;
        }

        // 执行Fastboot命令的辅助方法（带指定序列号）
        private async Task<string> ExecuteFastbootCommandWithSerial(string fastbootPath, string arguments, string deviceSerial)
        {
            try
            {
                bool showNativeOutput = arguments.TrimStart().StartsWith("flash ", StringComparison.OrdinalIgnoreCase);

                return await Task.Run(() =>
                {
                    object nativeOutputLock = new object();
                    bool hasNativeProgressLine = false;

                    void ShowNativeFastbootLine(string line)
                    {
                        if (!showNativeOutput) return;

                        bool isTransferProgress = Regex.IsMatch(
                            line,
                            @"\(\d+(?:\.\d+)?%\).*\b(?:B/s|KB/s|MB/s|GB/s)\b",
                            RegexOptions.IgnoreCase);

                        lock (nativeOutputLock)
                        {
                            if (isTransferProgress)
                            {
                                if (hasNativeProgressLine)
                                {
                                    UpdateFlashNativeProgress(line);
                                }
                                else
                                {
                                    AppendFlashNativeProgress(line);
                                }
                            }
                            else
                            {
                                AppendToFlashLogTextBox(line);
                            }

                            hasNativeProgressLine = isTransferProgress;
                        }
                    }

                    // 使用指定的设备序列号，添加 -s 参数
                    string finalArguments = arguments;
                    if (!string.IsNullOrEmpty(deviceSerial))
                    {
                        finalArguments = $"-s {deviceSerial} {arguments}";
                    }
                    
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = fastbootPath,
                            Arguments = finalArguments,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    
                    StringBuilder outputBuilder = new StringBuilder();
                    bool hasError = false;
                    
                    // 实时读取输出并解析百分比
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            outputBuilder.AppendLine(e.Data);
                            ShowNativeFastbootLine(e.Data);

                            // 将fastboot输出记录到全局日志中
                            fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT OUTPUT] {e.Data}");
                            
                            // 检测错误关键字
                            if (e.Data.ToLower().Contains("error"))
                            {
                                hasError = true;
                            }
                            
                            // 解析百分比并更新进度条
                            ParseProgressAndUpdateBar(e.Data);
                        }
                    };
                    
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            outputBuilder.AppendLine(e.Data);
                            ShowNativeFastbootLine(e.Data);

                            // 将fastboot错误输出记录到全局日志中
                            fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT ERROR] {e.Data}");
                            
                            // 检测是否为真正的错误信息
                            bool isRealError = e.Data.ToLower().Contains("error") || 
                                             e.Data.ToLower().Contains("failed") || 
                                             e.Data.ToLower().Contains("cannot") ||
                                             e.Data.ToLower().Contains("invalid");
                            
                            // 如果是真正的错误，添加错误前缀；否则正常显示
                            if (isRealError)
                            {
                                hasError = true;
                            }
                            
                            // 解析百分比并更新进度条
                            ParseProgressAndUpdateBar(e.Data);
                        }
                    };
                    
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    
                    string result = outputBuilder.ToString();
                    
                    // 如果检测到错误，在返回值中标记
                    if (hasError)
                    {
                        result = "ERROR_DETECTED\n" + result;
                    }
                    
                    return result;
                });
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"执行命令失败: {ex.Message}");
                return $"执行命令失败: {ex.Message}";
            }
        }

        // 执行Fastboot命令的辅助方法
        private async Task<string> ExecuteFastbootCommand(
            string fastbootPath,
            string arguments,
            bool showNativeOutput = false,
            bool parseStatusOutput = true,
            CancellationToken cancellationToken = default)
        {
            Process? process = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string selectedSerial = GetSelectedDeviceSerial();
                
                // 如果有选中的设备序列号，添加 -s 参数
                string finalArguments = arguments;
                if (!string.IsNullOrEmpty(selectedSerial))
                {
                    finalArguments = $"-s {selectedSerial} {arguments}";
                }
                
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fastbootPath,
                        Arguments = finalArguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                
                StringBuilder outputBuilder = new StringBuilder();
                bool hasError = false;
                int outputLineCount = 0;
                const int MAX_OUTPUT_LINES = 1000; // 限制输出行数，避免内存占用过大
                
                // 实时读取输出并解析百分比
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        // 【已隐藏】不再显示fastboot原生输出到日志
                        // Dispatcher.BeginInvoke(new Action(() => 
                        // {
                        //     LogToOugaFlash($"[FASTBOOT] {e.Data}", "Gray");
                        // }));
                        
                        // 限制输出缓冲区大小，只保留最近的输出
                        if (outputLineCount < MAX_OUTPUT_LINES)
                        {
                            outputBuilder.AppendLine(e.Data);
                            outputLineCount++;
                        }
                        else if (outputLineCount == MAX_OUTPUT_LINES)
                        {
                            outputBuilder.AppendLine("... (输出过多，已省略部分内容) ...");
                            outputLineCount++;
                        }

                        if (showNativeOutput)
                        {
                            AppendToFlashLogTextBox(e.Data);
                        }
                        
                        // 只记录关键日志，不记录每一行进度
                        if (!e.Data.Contains("%") && !e.Data.Contains("MB/s"))
                        {
                            fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT OUTPUT] {e.Data}");
                        }
                        
                        // 检测错误关键字
                        if (e.Data.ToLower().Contains("error"))
                        {
                            hasError = true;
                        }
                        
                        if (!showNativeOutput && parseStatusOutput)
                        {
                            // 解析百分比并更新进度条（不阻塞）
                            ParseProgressAndUpdateBar(e.Data);

                            // 只在关键状态时更新UI，减少UI消息队列压力
                            if (e.Data.Contains("Sending") || e.Data.Contains("Writing") ||
                                e.Data.Contains("OKAY") || e.Data.Contains("Finished") ||
                                e.Data.ToLower().Contains("error"))
                            {
                                Dispatcher.BeginInvoke(new Action(() => ParseAndLogSimpleStatus(e.Data)));
                            }
                        }
                    }
                };
                
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        // 【已隐藏】不再显示fastboot错误输出到日志
                        // Dispatcher.BeginInvoke(new Action(() => 
                        // {
                        //     LogToOugaFlash($"[FASTBOOT ERROR] {e.Data}", "Red");
                        // }));
                        
                        // 限制输出缓冲区大小
                        if (outputLineCount < MAX_OUTPUT_LINES)
                        {
                            outputBuilder.AppendLine(e.Data);
                            outputLineCount++;
                        }

                        if (showNativeOutput)
                        {
                            AppendToFlashLogTextBox(e.Data);
                        }
                        
                        // 记录错误日志
                        fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT ERROR] {e.Data}");
                        
                        // 检测错误关键字
                        if (e.Data.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                            e.Data.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                        {
                            hasError = true;
                            if (!showNativeOutput && parseStatusOutput)
                            {
                                Dispatcher.BeginInvoke(new Action(() => LogSimpleStatus($"错误: {e.Data}")));
                            }
                        }
                        else if (!showNativeOutput && parseStatusOutput)
                        {
                            // 只在关键状态时更新UI
                            if (e.Data.Contains("Sending") || e.Data.Contains("Writing") || 
                                e.Data.Contains("OKAY") || e.Data.Contains("Finished"))
                            {
                                Dispatcher.BeginInvoke(new Action(() => ParseAndLogSimpleStatus(e.Data)));
                            }
                        }
                        
                        if (!showNativeOutput && parseStatusOutput)
                        {
                            // 解析百分比并更新进度条
                            ParseProgressAndUpdateBar(e.Data);
                        }
                    }
                };
                
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                // 等待进程完成；停止页面操作时只终止本次命令进程。
                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                    // 确保所有异步输出都已处理完成
                    process.WaitForExit();
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                    }
                    throw;
                }
                
                // 给一点时间让所有事件处理器完成
                await Task.Delay(100, cancellationToken);
                
                string result = outputBuilder.ToString();
                
                // 如果检测到错误，在返回值中标记
                if (hasError)
                {
                    result = "ERROR_DETECTED\n" + result;
                }
                
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"执行命令失败: {ex.Message}");
                return $"执行命令失败: {ex.Message}";
            }
            finally
            {
                process?.Dispose();
            }
        }

        // 解析fastboot输出中的百分比并更新进度条
        private void ParseProgressAndUpdateBar(string output)
        {
            if (string.IsNullOrEmpty(output)) return;
            
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 尝试解析百分比
                    var percentageMatch = System.Text.RegularExpressions.Regex.Match(output, @"\((\d+(?:\.\d+)?)%\)|(\d+(?:\.\d+)?)%");
                    if (percentageMatch.Success)
                    {
                        // 获取匹配的百分比值（可能在第1组或第2组）
                        string percentageStr = percentageMatch.Groups[1].Success ? percentageMatch.Groups[1].Value : percentageMatch.Groups[2].Value;
                        
                        if (double.TryParse(percentageStr, out double percentage))
                        {
                            // 确保百分比在有效范围内
                            if (percentage >= 0 && percentage <= 100)
                            {
                                bootflash.Value = (int)Math.Round(percentage);
                                
                                // 同时更新FlashProgressBar
                                
                            }
                        }
                    }
                    
                    // 尝试解析传输速率，兼容 B/s、KB/s、MB/s、GB/s
                    var speedMatch = System.Text.RegularExpressions.Regex.Match(
                        output,
                        @"(\d+(?:\.\d+)?)\s*(B/s|KB/s|MB/s|GB/s)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (speedMatch.Success)
                    {
                        string speed = speedMatch.Groups[1].Value;
                        string unit = speedMatch.Groups[2].Value.ToUpperInvariant();
                        if (double.TryParse(speed, out double speedValue))
                        {
                            UpdateTransferRateText($"{speedValue:F2}{unit}");
                            System.Diagnostics.Debug.WriteLine($"[速率更新] {speedValue:F2}{unit}");
                        }
                    }
                    
                    // 如果没有找到百分比，则根据关键字设置固定进度
                    if (output.Contains("Writing") || output.Contains("Sending"))
                    {
                        // 根据不同的fastboot阶段更新进度条
                        if (output.Contains("Sending"))
                        {
                            // 开始发送数据阶段，设置进度为30%
                            if (bootflash.Value < 30)
                            {
                                bootflash.Value = 30;
                                if (bootflash != null && bootflash.Value < 30)
                                {
                                    
                                }
                            }
                        }
                        else if (output.Contains("Writing"))
                        {
                            // 开始写入阶段，设置进度为70%
                            if (bootflash.Value < 70)
                            {
                                bootflash.Value = 70;
                                if (bootflash != null && bootflash.Value < 70)
                                {
                                    
                                }
                            }
                        }
                    }
                    else if (output.Contains("OKAY"))
                    {
                        // 操作成功完成
                        bootflash.Value = 90; // 设置为90%，等待最终完成
                        
                    }
                    else if (output.Contains("Finished") || output.Contains("镜像刷入成功"))
                    {
                        // 刷入完全完成
                        bootflash.Value = 100;
                        
                        UpdateTransferRateText("完成");
                    }
                }));
            }
            catch (Exception ex)
            {
                // 静默处理解析错误，避免影响主流程
                System.Diagnostics.Debug.WriteLine($"解析进度时出错: {ex.Message}");
            }
        }

        // 向日志文本框添加消息的辅助方法
        private void LogToFlashTextBox(string message)
        {
            AppendStyledFlashLog(message, FlashLogDefaultBrush);
        }

        private static readonly System.Windows.Media.Brush FlashLogTimeBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
        private static readonly System.Windows.Media.Brush FlashLogDefaultBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85));
        private static readonly System.Windows.Media.Brush FlashLogInfoBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
        private static readonly System.Windows.Media.Brush FlashLogWaitingBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246));
        private static readonly System.Windows.Media.Brush FlashLogSuccessBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 163, 74));
        private static readonly System.Windows.Media.Brush FlashLogWarningBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6));
        private static readonly System.Windows.Media.Brush FlashLogRebootBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 58, 237));
        private static readonly System.Windows.Media.Brush FlashLogErrorBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));

        private Run? _flashCountdownRun;
        private Run? _flashNativeProgressTimeRun;
        private Run? _flashNativeProgressRun;

        private void AppendStyledFlashLog(
            string message,
            System.Windows.Media.Brush messageBrush,
            bool bold = false)
        {
            if (FlashLogTextBox == null) return;

            Dispatcher.Invoke(() =>
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                paragraph.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss}] ")
                {
                    Foreground = FlashLogTimeBrush
                });
                paragraph.Inlines.Add(new Run(message)
                {
                    Foreground = messageBrush,
                    FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
                });
                FlashLogTextBox.Document.Blocks.Add(paragraph);
                FlashLogTextBox.ScrollToEnd();
            });
        }

        // 简化的状态日志方法，只显示关键状态信息
        private void LogSimpleStatus(string message)
        {
            System.Windows.Media.Brush brush = FlashLogDefaultBrush;
            bool bold = false;

            if (message.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("错误", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("失败", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogErrorBrush;
                bold = true;
            }
            else if (message.Contains("警告", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogWarningBrush;
                bold = true;
            }
            else if (message.StartsWith("Flashing ", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("任务结束", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("操作完成", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("ADB修复完成", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("强开成功", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("基带调试端口已开启", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogSuccessBrush;
                bold = true;
            }
            else if (message.StartsWith("[Rebooting]", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogRebootBrush;
                bold = true;
            }
            else if (message.StartsWith("已连接", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogInfoBrush;
                bold = true;
            }
            else if (message.StartsWith("检测结果", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("处理器类型", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("检查ROOT权限", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogInfoBrush;
                bold = true;
            }
            else if (message.StartsWith("请按", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("请盯住", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogWarningBrush;
                bold = true;
            }
            else if (message.StartsWith("正在", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogInfoBrush;
            }
            else if (message.StartsWith("> fastboot", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("> adb", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogInfoBrush;
                bold = true;
            }

            AppendStyledFlashLog(message, brush, bold);
        }

        private void AppendFlashCountdown(string message)
        {
            if (FlashLogTextBox == null) return;

            Dispatcher.Invoke(() =>
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                paragraph.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss}] ")
                {
                    Foreground = FlashLogTimeBrush
                });
                _flashCountdownRun = new Run(message)
                {
                    Foreground = FlashLogWaitingBrush,
                    FontWeight = FontWeights.SemiBold
                };
                paragraph.Inlines.Add(_flashCountdownRun);
                FlashLogTextBox.Document.Blocks.Add(paragraph);
                FlashLogTextBox.ScrollToEnd();
            });
        }

        private void UpdateFlashCountdown(string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (_flashCountdownRun != null)
                {
                    _flashCountdownRun.Text = message;
                    FlashLogTextBox.ScrollToEnd();
                }
            });
        }

        private void AppendFlashNativeProgress(string message)
        {
            if (FlashLogTextBox == null) return;

            Dispatcher.Invoke(() =>
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                _flashNativeProgressTimeRun = new Run($"[{DateTime.Now:HH:mm:ss}] ")
                {
                    Foreground = FlashLogTimeBrush
                };
                _flashNativeProgressRun = new Run(message)
                {
                    Foreground = FlashLogInfoBrush,
                    FontWeight = FontWeights.SemiBold
                };
                paragraph.Inlines.Add(_flashNativeProgressTimeRun);
                paragraph.Inlines.Add(_flashNativeProgressRun);
                FlashLogTextBox.Document.Blocks.Add(paragraph);
                FlashLogTextBox.ScrollToEnd();
            });
        }

        private void UpdateFlashNativeProgress(string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (_flashNativeProgressTimeRun != null && _flashNativeProgressRun != null)
                {
                    _flashNativeProgressTimeRun.Text = $"[{DateTime.Now:HH:mm:ss}] ";
                    _flashNativeProgressRun.Text = message;
                    FlashLogTextBox.ScrollToEnd();
                }
            });
        }
        
        // 向FlashLogTextBox添加内容并自动滚动的辅助方法
        private void AppendToFlashLogTextBox(string content)
        {
            System.Windows.Media.Brush brush = FlashLogDefaultBrush;
            bool bold = false;

            if (content.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogErrorBrush;
                bold = true;
            }
            else if (content.Contains("OKAY", StringComparison.OrdinalIgnoreCase) ||
                     content.StartsWith("Finished", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogSuccessBrush;
            }
            else if (content.StartsWith("Sending", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogInfoBrush;
            }
            else if (content.StartsWith("Writing", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogRebootBrush;
            }

            AppendStyledFlashLog(content, brush, bold);
        }
        
        // 解析fastboot输出并显示简化状态信息
        private void ParseAndLogSimpleStatus(string output)
        {
            if (string.IsNullOrEmpty(output)) return;
            
            string lowerOutput = output.ToLower();
            
            // 检测分区刷入开始
            if (lowerOutput.Contains("sending") && lowerOutput.Contains("kb"))
            {
                // 提取分区名称
                var match = System.Text.RegularExpressions.Regex.Match(output, @"Sending '([^']+)'");
                if (match.Success)
                {
                    string partitionName = match.Groups[1].Value;
                    currentFlashingPartition = partitionName; // 保存当前刷入的分区名称
                    LogSimpleStatus($"准备刷入到{partitionName}分区...");
                }
                return;
            }
            
            // 检测分区刷入进行中
            if (lowerOutput.Contains("writing") && lowerOutput.Contains("%"))
            {
                // 提取分区名称和进度
                var match = System.Text.RegularExpressions.Regex.Match(output, @"([^:]+):\s*[\d\.]+\s*MB/[\d\.]+\s*MB\s*\(([\d\.]+)%\)");
                if (match.Success)
                {
                    string partitionName = match.Groups[1].Value.Trim();
                    string progress = match.Groups[2].Value;
                    currentFlashingPartition = partitionName; // 更新当前刷入的分区名称
                    LogSimpleStatus($"正在刷入到{partitionName}分区...");
                }
                else if (!string.IsNullOrEmpty(currentFlashingPartition))
                {
                    // 使用之前保存的分区名称
                    LogSimpleStatus($"正在刷入到{currentFlashingPartition}分区...");
                }
                else
                {
                    // 如果无法提取具体信息，使用通用格式
                    LogSimpleStatus("正在刷入到分区...");
                }
                return;
            }
            
            // 检测刷入完成 - 只在finished完成时记录成功，避免重复
            if (lowerOutput.Contains("finished") && lowerOutput.Contains("total time"))
            {
                // 使用当前刷入的分区名称
                if (!string.IsNullOrEmpty(currentFlashingPartition))
                {
                    LogSimpleStatus($"{currentFlashingPartition}镜像刷入成功...");
                }
                else
                {
                    LogSimpleStatus("镜像刷入成功...");
                }
                // 清空当前分区名称，为下次刷入做准备
                currentFlashingPartition = "";
                return;
            }
            
            // 检测单个操作完成但不记录成功信息，避免重复
            if (lowerOutput.Contains("okay") && !lowerOutput.Contains("finished"))
            {
                return;
            }
            
            // 检测错误信息
            if (lowerOutput.Contains("failed") || lowerOutput.Contains("error"))
            {
                LogSimpleStatus($"错误: {output}");
                return;
            }
        }
        
        // 查找父级控件的辅助方法
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            
            T parent = parentObject as T;
            if (parent != null)
                return parent;
            else
                return FindParent<T>(parentObject);
        }

        // 全自动投屏复选框选中事件
        private void AutoMirrorCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            isAutoMirrorEnabled = true;
            StartAutoMirrorTimer();
        }

        // 全自动投屏复选框取消选中事件
        private void AutoMirrorCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            isAutoMirrorEnabled = false;
            StopAutoMirrorTimer();
        }

        // 启动全自动投屏定时器
        private void StartAutoMirrorTimer()
        {
            if (autoMirrorTimer == null)
            {
                autoMirrorTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2) // 每2秒检查一次
                };
                autoMirrorTimer.Tick += AutoMirrorTimer_Tick;
            }
            autoMirrorTimer.Start();
        }

        // 停止全自动投屏定时器
        private void StopAutoMirrorTimer()
        {
            autoMirrorTimer?.Stop();
        }

        // 全自动投屏定时器事件
        private void AutoMirrorTimer_Tick(object? sender, EventArgs e)
        {
            if (isAutoMirrorEnabled && !IsScrcpyProcessRunning())
            {
                StartScrcpyProcess();
            }
        }

        private bool IsScrcpyProcessRunning()
        {
            try
            {
                var scrcpyProcesses = Process.GetProcessesByName("scrcpy");
                return scrcpyProcesses.Length > 0;
            }
            catch
            {
                return false;
            }
        }
        private async void StartScrcpyProcess()
        {
            if (isScrcpyStarting || (scrcpyProcess != null && !scrcpyProcess.HasExited))
            {
                return;
            }

            isScrcpyStarting = true;

            try
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string scrcpyPath = Path.Combine(appDirectory, "platform-tools", "scrcpy.exe");
                if (File.Exists(scrcpyPath))
                {
                    var maxFpsSlider = this.FindName("MaxFpsSlider") as Slider;
                    int maxFps = maxFpsSlider != null ? (int)maxFpsSlider.Value : 60;
                    var bitrateSlider = this.FindName("BitrateSlider") as Slider;
                    int bitrate = bitrateSlider != null ? (int)bitrateSlider.Value : 8; 
                    
                    int maxSize = GetWindowMaxSize();
                    string selectedSerial = GetSelectedDeviceSerial();

                    // 设备重连时，设备列表和选中项的恢复可能晚于自动投屏定时器。
                    // 此时必须等待，不能以空序列号启动，否则不会生成自定义窗口标题。
                    if (string.IsNullOrWhiteSpace(selectedSerial))
                    {
                        return;
                    }

                    string arguments = await BuildScrcpyLaunchArgumentsAsync(selectedSerial, bitrate, maxFps, maxSize);
                    
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = scrcpyPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(scrcpyPath),
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    scrcpyProcess = Process.Start(startInfo);
                    
                    if (scrcpyProcess != null)
                    {
                        Process startedProcess = scrcpyProcess;
                        InitializeScrcpyControlBarTracking(startedProcess);

                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 全自动投屏已触发，正在启动scrcpy...\n");
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 目标设备: {selectedSerial}\n");
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 命令: {scrcpyPath} {arguments}\n");

                        startedProcess.OutputDataReceived += (sender, e) => ProcessScrcpyLogLine(e.Data, false);
                        startedProcess.ErrorDataReceived += (sender, e) => ProcessScrcpyLogLine(e.Data, true);

                        startedProcess.EnableRaisingEvents = true;
                        startedProcess.Exited += (sender, e) =>
                        {
                            AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 自动投屏scrcpy进程已退出，等待设备重新连接...\n");
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                CloseScrcpyControlBar();
                                if (ReferenceEquals(scrcpyProcess, startedProcess))
                                {
                                    scrcpyProcess = null;
                                }
                            }));
                        };

                        startedProcess.BeginOutputReadLine();
                        startedProcess.BeginErrorReadLine();
                    }
                    else
                    {
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 自动投屏启动scrcpy失败！\n");
                    }
                }
                else
                {
                    AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 未找到scrcpy.exe，请检查platform-tools目录！\n");
                }
            }
            catch (Exception ex)
            {
                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 自动投屏启动失败: {ex.Message}\n");
            }
            finally
            {
                isScrcpyStarting = false;
            }
        }

        private bool IsValidPartitionName(string name)
        {
            // 常见的Android分区名称
            var validPartitions = new HashSet<string>
            {
                "boot", "recovery", "system", "vendor", "userdata", "cache", "persist",
                "modem", "bluetooth", "dsp", "aboot", "rpm", "sbl1", "tz", "hyp",
                "product", "odm", "vbmeta", "dtbo", "super", "metadata", "misc",
                "init_boot", "vendor_boot", "boot_a", "boot_b", "system_a", "system_b",
                "vendor_a", "vendor_b", "product_a", "product_b", "odm_a", "odm_b"
            };
            
            return validPartitions.Contains(name.ToLower()) || 
                   name.EndsWith("_a") || name.EndsWith("_b") ||
                   Regex.IsMatch(name, @"^[a-zA-Z][a-zA-Z0-9_-]*$");
        }
        
        private string FormatSize(string sizeStr)
        {
            string normalized = sizeStr.Trim();
            System.Globalization.NumberStyles style = normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? System.Globalization.NumberStyles.HexNumber
                : System.Globalization.NumberStyles.Integer;
            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(2);
            }

            if (long.TryParse(normalized, style, System.Globalization.CultureInfo.InvariantCulture, out long size))
            {
                return FormatByteSize(size);
            }
            return sizeStr;
        }

        private static string FormatByteSize(long size)
        {
            if (size >= 1024L * 1024 * 1024)
                return $"{size / (1024.0 * 1024.0 * 1024.0):F2} GB";
            if (size >= 1024L * 1024)
                return $"{size / (1024.0 * 1024.0):F2} MB";
            if (size >= 1024)
                return $"{size / 1024.0:F2} KB";
            return $"{size} B";
        }
        
        private string GetFastbootPath()
        {
            try
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                // 优先使用flash文件夹中的fastboot.exe
                string fastbootPath = Path.Combine(appDirectory, "platform-tools", "fastboot.exe");
                
                if (File.Exists(fastbootPath))
                {
                    return fastbootPath;
                }
                
                // 尝试其他可能的路径
                string[] possiblePaths = {
                    Path.Combine(appDirectory, "platform-tools", "fastboot.exe"),
                    Path.Combine(appDirectory, "fastboot.exe"),
                    "fastboot.exe" // 系统PATH中的fastboot
                };
                
                foreach (string path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
                
                return "fastboot"; // 假设在系统PATH中
            }
            catch
            {
                return "fastboot";
            }
        }
        
        private string GetAdbPath()
        {
            try
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string adbPath = Path.Combine(appDirectory, "platform-tools", "adb.exe");
                
                if (File.Exists(adbPath))
                {
                    return adbPath;
                }
                
                // 尝试其他可能的路径
                string[] possiblePaths = {
                    Path.Combine(appDirectory, "adb.exe"),
                    Path.Combine(appDirectory, "tools", "adb.exe"),
                    "adb.exe" // 系统PATH中的adb
                };
                
                foreach (string path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
                
                // 如果都找不到，仍然使用程序目录中的adb.exe路径
                return Path.Combine(appDirectory, "platform-tools", "adb.exe");
            }
            catch
            {
                // 异常情况下也返回程序目录中的adb.exe路径
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(appDirectory, "platform-tools", "adb.exe");
            }
        }

        private void SelectXiaomiFlashScriptButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择小米刷机包文件夹",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var xiaomiFlashScriptPathTextBox = this.FindName("XiaomiFlashScriptPathTextBox") as System.Windows.Controls.TextBox;
                if (xiaomiFlashScriptPathTextBox != null)
                {
                    xiaomiFlashScriptPathTextBox.Text = folderDialog.SelectedPath;
                }
                LogToFlashTextBox($"已选择小米刷机包: {folderDialog.SelectedPath}");
            }
        }

        // 开始小米线刷
        private async void StartXiaomiFlashButton_Click(object sender, RoutedEventArgs e)
        {
            // 标志变量，确保失败提示只弹出一次
            bool hasShownFailureMessage = false;
            // Missmatching检测相关变量
            bool hasMissmatchingDetected = false;
            System.Windows.Threading.DispatcherTimer? missmatchingTimer = null;
            
            try
            {
                // 验证刷机包路径
                var xiaomiFlashScriptPathTextBox = this.FindName("XiaomiFlashScriptPathTextBox") as System.Windows.Controls.TextBox;
                if (xiaomiFlashScriptPathTextBox == null)
                {
                    LogToFlashTextBox("错误: 未找到小米刷机包路径输入框控件");
                    return;
                }
                string packagePath = xiaomiFlashScriptPathTextBox.Text.Trim();
                if (string.IsNullOrEmpty(packagePath))
                {
                    LogToFlashTextBox("错误: 请先选择小米刷机包");
                    return;
                }

                if (!Directory.Exists(packagePath))
                {
                    LogToFlashTextBox("错误: 选择的刷机包文件夹不存在");
                    return;
                }

                // 检查复选框状态并确定要执行的bat文件
                var completeWipeCheckBox = this.FindName("CompleteWipeCheckBox") as System.Windows.Controls.CheckBox;
                var keepDataCheckBox = this.FindName("KeepDataCheckBox") as System.Windows.Controls.CheckBox;
                var wipeAndLockBLCheckBox = this.FindName("WipeAndLockBLCheckBox") as System.Windows.Controls.CheckBox;

                string batFileName = "";
                string operationType = "";

                if (completeWipeCheckBox?.IsChecked == true)
                {
                    batFileName = "flash_all.bat";
                    operationType = "完全清除数据刷机";
                }
                else if (keepDataCheckBox?.IsChecked == true)
                {
                    batFileName = "flash_all_except_storage.bat";
                    operationType = "保留数据刷机";
                }
                else if (wipeAndLockBLCheckBox?.IsChecked == true)
                {
                    batFileName = "flash_all_lock.bat";
                    operationType = "完全清除数据并回锁BL";
                }
                else
                {
                    LogToFlashTextBox("错误: 请选择一种刷机模式");
                    return;
                }

                string scriptPath = Path.Combine(packagePath, batFileName);
                if (!File.Exists(scriptPath))
                {
                    LogToFlashTextBox($"错误: 在刷机包中未找到 {batFileName} 文件");
                    return;
                }

                LogToFlashTextBox($"选择的刷机模式: {operationType}");
                LogToFlashTextBox($"将执行脚本: {batFileName}");

                // 禁用按钮防止重复操作
var startXiaomiFlashButton = this.FindName("StartXiaomiFlashButton") as System.Windows.Controls.Button;
if (startXiaomiFlashButton != null)
{
    startXiaomiFlashButton.IsEnabled = false;
}
                
                // 重置并显示进度条
                if (bootflash != null)
                {
                    _xiaomiFlashProgressState = null;
                    bootflash.Value = 0;
                    bootflash.Tag = "0MB/s  |  Time:0s";
                    bootflash.Visibility = Visibility.Visible;
                }
                
                LogToFlashTextBox("开始小米线刷操作...");

                // 获取程序根目录的flash文件夹
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string flashDirectory = Path.Combine(appDirectory, "platform-tools");

                // 如果flash文件夹不存在则创建
                if (!Directory.Exists(flashDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(flashDirectory);
                        LogToFlashTextBox($"已创建flash文件夹: {flashDirectory}");
                    }
                    catch (Exception createEx)
                    {
                        LogToFlashTextBox($"错误: 无法创建flash文件夹: {createEx.Message}");
                        return;
                    }
                }

                LogToFlashTextBox($"程序根目录: {appDirectory}");
                LogToFlashTextBox($"Flash目录: {flashDirectory}");
                LogToFlashTextBox($"刷机包目录: {packagePath}");
                LogToFlashTextBox($"脚本路径: {scriptPath}");

                InitializeXiaomiFlashProgress(scriptPath);

                // 在flash文件夹中通过cmd执行脚本路径
                LogToFlashTextBox("即将开始小米线刷...");
                
                await Task.Run(() =>
                {
                    try
                    {
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/c \"{scriptPath}\"",
                                WorkingDirectory = flashDirectory,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }
                        };

                        // 实时读取输出
                        process.OutputDataReceived += (s, args) =>
                        {
                            if (!string.IsNullOrEmpty(args.Data))
                            {
                                Dispatcher.Invoke(() => 
                                {
                                    LogToFlashTextBox($"[线刷] {args.Data}");
                                    
                                    // 解析进度百分比
                                    ParseXiaomiFlashProgress(args.Data);
                                    
                                    // 如果有新日志且之前检测到Missmatching，取消定时器
                                    if (hasMissmatchingDetected && missmatchingTimer != null)
                                    {
                                        missmatchingTimer.Stop();
                                        missmatchingTimer = null;
                                        hasMissmatchingDetected = false;
                                    }
                                    
                                    // 检测Missmatching字样，启动10秒定时器
                                    if (args.Data.Contains("Missmatching") && !hasMissmatchingDetected)
                                    {
                                        hasMissmatchingDetected = true;
                                        
                                        // 创建10秒定时器
                                        missmatchingTimer = new System.Windows.Threading.DispatcherTimer();
                                        missmatchingTimer.Interval = TimeSpan.FromSeconds(10);
                                        missmatchingTimer.Tick += (timerSender, timerArgs) =>
                                        {
                                            missmatchingTimer.Stop();
                                            missmatchingTimer = null;
                                            hasMissmatchingDetected = false;
                                            
                                            // 10秒后仍无新日志，弹出警告
                                            System.Windows.MessageBox.Show("检测到Fastboot蜡笔了！\n\n请按以下步骤操作：\n1. 长按电源键加音量减\n2. 重新进入Fastboot\n3. 重新点击开始线刷",
                                                          "警告", 
                                                          MessageBoxButton.OK, 
                                                          MessageBoxImage.Warning);
                                            
                                            // 重新启用按钮，让用户可以再次尝试
                                            var startXiaomiFlashButton = this.FindName("StartXiaomiFlashButton") as System.Windows.Controls.Button;
                                            if (startXiaomiFlashButton != null)
                                            {
                                                startXiaomiFlashButton.IsEnabled = true;
                                            }
                                        };
                                        missmatchingTimer.Start();
                                    }
                                    else
                                    {
                                        // 将输出数据转换为小写以便检查
                                        string lowerData = args.Data.ToLower();
                                        
                                        // 检测FAILED或error等失败关键词（只弹出一次）
                                        if (!hasShownFailureMessage && (lowerData.Contains("failed") || lowerData.Contains("error") || 
                                            lowerData.Contains("失败") || lowerData.Contains("错误")))
                                        {
                                            hasShownFailureMessage = true;
                                            System.Windows.MessageBox.Show($"线刷失败！\n\n错误信息：{args.Data}\n\n连接不稳定，设备断开了，请重新进入Fastboot再试",
                                                          "小米线刷失败", 
                                                          MessageBoxButton.OK, 
                                                          MessageBoxImage.Error);
                                        }
                                        // 检测Rebooting字样，弹出刷机完成提示
                                        else if (lowerData.Contains("rebooting"))
                                        {
                                            System.Windows.MessageBox.Show("刷机完成！\n\n设备正在重启，请等待手机重启完成。",
                                                          "小米线刷", 
                                                          MessageBoxButton.OK, 
                                                          MessageBoxImage.Information);
                                        }
                                    }
                                });
                            }
                        };

                        process.ErrorDataReceived += (s, args) =>
                        {
                            if (!string.IsNullOrEmpty(args.Data))
                            {
                                Dispatcher.Invoke(() => 
                                {
                                    LogToFlashTextBox($"[状态] {args.Data}");
                                    
                                    // 解析进度百分比
                                    ParseXiaomiFlashProgress(args.Data);
                                    
                                    // 如果有新日志且之前检测到Missmatching，取消定时器
                                    if (hasMissmatchingDetected && missmatchingTimer != null)
                                    {
                                        missmatchingTimer.Stop();
                                        missmatchingTimer = null;
                                        hasMissmatchingDetected = false;
                                    }
                                    
                                    // 检测2>&1字样，提示重新进入fastboot
                                    if (args.Data.Contains("2>&1"))
                                    {
                                        System.Windows.MessageBox.Show("检测到设备未正确进入Fastboot模式！\n\n请按以下步骤操作：\n1. 断开设备连接\n2. 重新进入Fastboot模式\n3. 连接设备后再点击开始线刷",
                                                      "需要重新进入Fastboot模式", 
                                                      MessageBoxButton.OK, 
                                                      MessageBoxImage.Warning);
                                    }
                                    else
                                    {
                                        // 将输出数据转换为小写以便检查
                                        string lowerData = args.Data.ToLower();
                                        
                                        // 检测FAILED或error等失败关键词（只弹出一次）
                                        if (!hasShownFailureMessage && (lowerData.Contains("failed") || lowerData.Contains("error") || 
                                            lowerData.Contains("失败") || lowerData.Contains("错误")))
                                        {
                                            hasShownFailureMessage = true;
                                            System.Windows.MessageBox.Show($"线刷失败！\n\n错误信息：{args.Data}\n\n请检查设备连接状态、驱动程序或刷机包是否正确。",
                                                          "小米线刷失败", 
                                                          MessageBoxButton.OK, 
                                                          MessageBoxImage.Error);
                                        }
                                        // 检测Rebooting字样，弹出刷机完成提示
                                        else if (lowerData.Contains("rebooting"))
                                        {
                                            System.Windows.MessageBox.Show("刷机完成！\n\n设备正在重启，请等待手机重启完成。",
                                                          "小米线刷完成", 
                                                          MessageBoxButton.OK, 
                                                          MessageBoxImage.Information);
                                        }
                                    }
                                });
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();

                        Dispatcher.Invoke(() =>
                        {
                            if (process.ExitCode == 0)
                            {
                                CompleteXiaomiFlashProgress(succeeded: true);
                                LogToFlashTextBox("小米线刷操作完成！");
                            }
                            else
                            {
                                CompleteXiaomiFlashProgress(succeeded: false);
                                LogToFlashTextBox($"小米线刷操作完成，退出代码: {process.ExitCode}");
                            }
                        });
                    }
                    catch (Exception processEx)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            CompleteXiaomiFlashProgress(succeeded: false);
                            LogToFlashTextBox($"执行脚本时发生错误: {processEx.Message}");
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                CompleteXiaomiFlashProgress(succeeded: false);
                LogToFlashTextBox($"小米线刷过程中发生错误: {ex.Message}");
            }
            finally
            {
                // 重新启用按钮
var startXiaomiFlashButton = this.FindName("StartXiaomiFlashButton") as System.Windows.Controls.Button;
if (startXiaomiFlashButton != null)
{
    startXiaomiFlashButton.IsEnabled = true;
}
            }
        }

        private void TextBox_TextChanged_1()
        {

        }

        private void InitializeXiaomiFlashProgress(string scriptPath)
        {
            _xiaomiFlashProgressState = CreateXiaomiFlashProgressState(scriptPath);

            if (bootflash == null)
            {
                return;
            }

            bootflash.Value = 0;
            bootflash.Tag = "0MB/s  |  Time:0s";
        }

        private static XiaomiFlashProgressState? CreateXiaomiFlashProgressState(string scriptPath)
        {
            try
            {
                string? scriptDirectory = IOPath.GetDirectoryName(scriptPath);
                if (string.IsNullOrWhiteSpace(scriptDirectory))
                {
                    return null;
                }

                var items = new List<XiaomiFlashProgressItem>();
                foreach (string line in IOFile.ReadAllLines(scriptPath, Encoding.Default))
                {
                    Match flashMatch = Regex.Match(
                        line,
                        @"(?i)\bfastboot(?:\.exe)?\b.*?\bflash\s+(?<partition>[^\s""']+)\s+(?<image>""[^""]+""|[^\s|&]+)");
                    if (!flashMatch.Success)
                    {
                        continue;
                    }

                    string partitionName = flashMatch.Groups["partition"].Value.Trim();
                    string imageToken = flashMatch.Groups["image"].Value.Trim().Trim('"', '\'');
                    string imagePath = Regex.Replace(
                        imageToken,
                        @"%~dp0",
                        _ => scriptDirectory + IOPath.DirectorySeparatorChar,
                        RegexOptions.IgnoreCase);

                    if (!IOPath.IsPathRooted(imagePath))
                    {
                        imagePath = IOPath.Combine(scriptDirectory, imagePath);
                    }

                    imagePath = IOPath.GetFullPath(imagePath);
                    if (!IOFile.Exists(imagePath))
                    {
                        return null;
                    }

                    items.Add(new XiaomiFlashProgressItem(
                        partitionName,
                        new FileInfo(imagePath).Length));
                }

                return items.Count > 0 && items.Any(item => item.Length > 0)
                    ? new XiaomiFlashProgressState(items)
                    : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"创建小米线刷总进度计划时出错: {ex.Message}");
                return null;
            }
        }

        // 解析小米线刷 fastboot 输出，按脚本内全部镜像的总字节数计算全局进度。
        private void ParseXiaomiFlashProgress(string output)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(output) || bootflash == null)
                {
                    return;
                }

                XiaomiFlashProgressState? state = _xiaomiFlashProgressState;
                if (state == null || state.TotalBytes <= 0)
                {
                    ParseXiaomiFlashPartitionProgressFallback(output);
                    return;
                }

                Match speedMatch = Regex.Match(
                    output,
                    @"(?<speed>\d+(?:\.\d+)?)\s*(?<unit>GB/s|MB/s|KB/s|B/s)",
                    RegexOptions.IgnoreCase);
                if (speedMatch.Success)
                {
                    state.TransferRate =
                        $"{speedMatch.Groups["speed"].Value}{speedMatch.Groups["unit"].Value}";
                }

                Match sendingMatch = Regex.Match(
                    output,
                    @"Sending(?:\s+sparse)?\s+'(?<partition>[^']+)'(?:\s+\d+/\d+)?\s*\((?<size>\d+(?:\.\d+)?)\s*(?<unit>GB|MB|KB|B)\)",
                    RegexOptions.IgnoreCase);
                if (sendingMatch.Success)
                {
                    string partitionName = sendingMatch.Groups["partition"].Value.Trim();
                    if (ActivateXiaomiFlashItem(state, partitionName))
                    {
                        state.CurrentChunkExpectedBytes = ConvertXiaomiSizeToBytes(
                            sendingMatch.Groups["size"].Value,
                            sendingMatch.Groups["unit"].Value);
                        state.CurrentChunkReportedBytes = 0;
                    }
                }

                Match transferMatch = Regex.Match(
                    output,
                    @"^\s*(?<partition>[^:]+):\s*(?<current>\d+(?:\.\d+)?)\s*(?<currentUnit>GB|MB|KB|B)\s*/\s*(?<total>\d+(?:\.\d+)?)\s*(?<totalUnit>GB|MB|KB|B)\s*\(",
                    RegexOptions.IgnoreCase);
                if (transferMatch.Success)
                {
                    string partitionName = transferMatch.Groups["partition"].Value.Trim();
                    if (EnsureXiaomiFlashItemActive(state, partitionName))
                    {
                        long currentBytes = ConvertXiaomiSizeToBytes(
                            transferMatch.Groups["current"].Value,
                            transferMatch.Groups["currentUnit"].Value);
                        long totalBytes = ConvertXiaomiSizeToBytes(
                            transferMatch.Groups["total"].Value,
                            transferMatch.Groups["totalUnit"].Value);

                        if (state.CurrentChunkExpectedBytes <= 0)
                        {
                            state.CurrentChunkExpectedBytes = totalBytes;
                        }

                        state.CurrentChunkReportedBytes =
                            Math.Max(state.CurrentChunkReportedBytes, currentBytes);
                        SetXiaomiCurrentItemTransferredBytes(
                            state,
                            state.CompletedChunkBytes + state.CurrentChunkReportedBytes);
                    }
                }

                if (Regex.IsMatch(output, @"^\s*Writing\s+'", RegexOptions.IgnoreCase) &&
                    state.CurrentItemIndex >= 0 &&
                    !state.CurrentCommandFinished)
                {
                    state.CurrentChunkReportedBytes = Math.Max(
                        state.CurrentChunkReportedBytes,
                        state.CurrentChunkExpectedBytes);
                    SetXiaomiCurrentItemTransferredBytes(
                        state,
                        state.CompletedChunkBytes + state.CurrentChunkReportedBytes);
                }

                if (Regex.IsMatch(output, @"^\s*Finished\.", RegexOptions.IgnoreCase))
                {
                    CompleteCurrentXiaomiFlashItem(state);
                }

                UpdateXiaomiFlashProgressUi(state);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"解析小米线刷总进度时出错: {ex.Message}");
            }
        }

        private static bool ActivateXiaomiFlashItem(
            XiaomiFlashProgressState state,
            string partitionName)
        {
            if (state.CurrentItemIndex >= 0 &&
                !state.CurrentCommandFinished &&
                string.Equals(
                    state.Items[state.CurrentItemIndex].PartitionName,
                    partitionName,
                    StringComparison.OrdinalIgnoreCase))
            {
                CompleteCurrentXiaomiFlashChunk(state);
                return true;
            }

            if (state.CurrentItemIndex >= 0 && !state.CurrentCommandFinished)
            {
                CompleteCurrentXiaomiFlashItem(state);
            }

            int searchStart = Math.Max(0, state.CurrentItemIndex + 1);
            int nextItemIndex = -1;
            for (int index = searchStart; index < state.Items.Count; index++)
            {
                if (string.Equals(
                    state.Items[index].PartitionName,
                    partitionName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    nextItemIndex = index;
                    break;
                }
            }

            if (nextItemIndex < 0)
            {
                return false;
            }

            state.CurrentItemIndex = nextItemIndex;
            state.CurrentItemTransferredBytes = 0;
            state.CompletedChunkBytes = 0;
            state.CurrentChunkExpectedBytes = 0;
            state.CurrentChunkReportedBytes = 0;
            state.CurrentCommandFinished = false;
            return true;
        }

        private static bool EnsureXiaomiFlashItemActive(
            XiaomiFlashProgressState state,
            string partitionName)
        {
            if (state.CurrentItemIndex >= 0 &&
                !state.CurrentCommandFinished &&
                string.Equals(
                    state.Items[state.CurrentItemIndex].PartitionName,
                    partitionName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ActivateXiaomiFlashItem(state, partitionName);
        }

        private static void CompleteCurrentXiaomiFlashChunk(XiaomiFlashProgressState state)
        {
            if (state.CurrentItemIndex < 0 || state.CurrentCommandFinished)
            {
                return;
            }

            long chunkBytes = Math.Max(
                state.CurrentChunkExpectedBytes,
                state.CurrentChunkReportedBytes);
            long itemLength = state.Items[state.CurrentItemIndex].Length;
            state.CompletedChunkBytes = Math.Min(
                itemLength,
                state.CompletedChunkBytes + chunkBytes);
            state.CurrentItemTransferredBytes = Math.Max(
                state.CurrentItemTransferredBytes,
                state.CompletedChunkBytes);
            state.CurrentChunkExpectedBytes = 0;
            state.CurrentChunkReportedBytes = 0;
        }

        private static void CompleteCurrentXiaomiFlashItem(XiaomiFlashProgressState state)
        {
            if (state.CurrentItemIndex < 0 || state.CurrentCommandFinished)
            {
                return;
            }

            XiaomiFlashProgressItem currentItem = state.Items[state.CurrentItemIndex];
            state.CompletedBytes = Math.Min(
                state.TotalBytes,
                state.CompletedBytes + currentItem.Length);
            state.CurrentItemTransferredBytes = 0;
            state.CompletedChunkBytes = 0;
            state.CurrentChunkExpectedBytes = 0;
            state.CurrentChunkReportedBytes = 0;
            state.CurrentCommandFinished = true;
        }

        private static void SetXiaomiCurrentItemTransferredBytes(
            XiaomiFlashProgressState state,
            long transferredBytes)
        {
            if (state.CurrentItemIndex < 0 || state.CurrentCommandFinished)
            {
                return;
            }

            long itemLength = state.Items[state.CurrentItemIndex].Length;
            state.CurrentItemTransferredBytes = Math.Max(
                state.CurrentItemTransferredBytes,
                Math.Min(itemLength, Math.Max(0, transferredBytes)));
        }

        private void UpdateXiaomiFlashProgressUi(XiaomiFlashProgressState state)
        {
            if (bootflash == null || state.TotalBytes <= 0)
            {
                return;
            }

            long transferredBytes = state.CompletedBytes;
            if (!state.CurrentCommandFinished)
            {
                transferredBytes += state.CurrentItemTransferredBytes;
            }

            state.LastReportedBytes = Math.Max(
                state.LastReportedBytes,
                Math.Min(state.TotalBytes, transferredBytes));

            bootflash.Value = Math.Clamp(
                state.LastReportedBytes * 100d / state.TotalBytes,
                0d,
                100d);
            bootflash.Tag =
                $"{state.TransferRate}  |  Time:{(int)state.Elapsed.Elapsed.TotalSeconds}s";
        }

        private void CompleteXiaomiFlashProgress(bool succeeded)
        {
            XiaomiFlashProgressState? state = _xiaomiFlashProgressState;
            if (state != null)
            {
                state.Elapsed.Stop();
                state.TransferRate = "0MB/s";
                if (succeeded)
                {
                    state.LastReportedBytes = state.TotalBytes;
                }

                UpdateXiaomiFlashProgressUi(state);
            }
            else if (bootflash != null)
            {
                if (succeeded)
                {
                    bootflash.Value = 100;
                }

                bootflash.Tag = "0MB/s  |  Time:0s";
            }
        }

        private static long ConvertXiaomiSizeToBytes(string valueText, string unit)
        {
            if (!double.TryParse(
                valueText.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value))
            {
                return 0;
            }

            double multiplier = unit.ToUpperInvariant() switch
            {
                "GB" => 1024d * 1024d * 1024d,
                "MB" => 1024d * 1024d,
                "KB" => 1024d,
                _ => 1d
            };

            return Math.Max(
                0,
                (long)Math.Round(value * multiplier, MidpointRounding.AwayFromZero));
        }

        // 解析小米线刷fastboot输出并更新bootflash进度条
        private void ParseXiaomiFlashPartitionProgressFallback(string output)
        {
            try
            {
                if (string.IsNullOrEmpty(output) || bootflash == null)
                    return;

                double currentValue = bootflash.Value;

                // 匹配百分比格式: (XX.X%) 或 (XX%)
                // 示例: "modem_ab: 3.9 MB/266.6 MB (1.5%) [raw] 38.75 MB/s"
                var percentMatch = System.Text.RegularExpressions.Regex.Match(output, @"\((\d+(?:\.\d+)?)%\)");
                if (percentMatch.Success)
                {
                    string percentStr = percentMatch.Groups[1].Value;
                    // 使用InvariantCulture确保小数点正确解析
                    if (double.TryParse(percentStr.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double percent))
                    {
                        // 检查是否是小文件（KB级别）的100%，如果是则忽略
                        // 匹配格式: "partition_name: XX KB/XX KB (100.0%)" 或 "partition_name: XXX B/XXX B (100.0%)"
                        bool isSmallFile = System.Text.RegularExpressions.Regex.IsMatch(output, @"\d+\s*(B|KB)/\d+\s*(B|KB)\s*\(100\.0%\)");
                        
                        if (percent == 100.0 && isSmallFile)
                        {
                            // 忽略小文件的100%，避免进度条过早到达100%
                            return;
                        }
                        
                        // 如果当前进度已经很高（>90%），但新的百分比很低（<50%），说明开始传输新的大文件
                        // 这时应该重置进度条
                        if (currentValue > 90 && percent < 50)
                        {
                            bootflash.Value = percent;
                            return;
                        }
                        
                        // 正常情况：只增不减
                        if (percent > currentValue)
                        {
                            bootflash.Value = Math.Min(100, percent);
                        }
                    }
                    return;
                }

                // 如果没有匹配到百分比，检测关键操作阶段
                string lowerOutput = output.ToLower();
                
                // 只在真正重启时才设置为100%（检测"Rebooting"单独出现）
                if (System.Text.RegularExpressions.Regex.IsMatch(output, @"^\s*Rebooting\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    bootflash.Value = 100;
                    return;
                }
                
                // 以下只在进度很低时才更新
                if (currentValue < 5)
                {
                    // 开始操作
                    if (lowerOutput.Contains("target reported max download size"))
                    {
                        bootflash.Value = 1;
                    }
                    // 擦除操作
                    else if (lowerOutput.Contains("erasing") && !lowerOutput.Contains("erase successfully"))
                    {
                        bootflash.Value = 3;
                    }
                    // 发送数据中
                    else if (lowerOutput.Contains("sending") && !lowerOutput.Contains("okay"))
                    {
                        bootflash.Value = 5;
                    }
                }
            }
            catch (Exception ex)
            {
                // 静默处理解析错误，不影响主流程
                System.Diagnostics.Debug.WriteLine($"解析小米线刷进度时出错: {ex.Message}");
            }
        }
        
        // 日志窗口相关方法
        
        // 添加日志消息的辅助方法
        private void AddLogMessage(string level, string message)
        {
            var logTextBlock = this.FindName("LogTextBlock") as TextBlock;
            var logScrollViewer = this.FindName("LogScrollViewer") as ScrollViewer;
            
            if (logTextBlock != null)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                
                // 在UI线程中更新日志
                Dispatcher.Invoke(() =>
                {
                    // 创建新的Run元素用于添加带颜色的文本
                    var run = new Run($"[{timestamp}] {message}\n");
                    
                    // 根据消息内容设置颜色
                    if (message.Contains("安装成功") || message.Contains("安装完成"))
                    {
                        // 绿色显示安装成功和安装完成
                        run.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // #28A745
                    }
                    else if (level == "成功")
                    {
                        // 绿色显示成功消息
                        run.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // #28A745
                    }
                    else if (level == "错误")
                    {
                        // 红色显示错误消息
                        run.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // #DC3545
                    }
                    else if (level == "警告")
                    {
                        // 橙色显示警告消息
                        run.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 165, 0)); // #FFA500
                    }
                    else if (level == "信息")
                    {
                        // 蓝色显示信息消息
                        run.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 162, 184)); // #17A2B8
                    }
                    else
                    {
                        // 默认颜色
                        run.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)); // #333
                    }
                    
                    // 将Run添加到TextBlock的Inlines集合中
                    logTextBlock.Inlines.Add(run);
                    
                    // 自动滚动到底部
                    if (logScrollViewer != null)
                    {
                        logScrollViewer.ScrollToEnd();
                    }
                });
            }
        }

        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T)
                {
                    return (T)child;
                }
                else
                {
                    T? childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                        return childOfChild;
                }
            }
            return null;
        }

        private void SocialMediaButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is string url)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"无法打开链接：{ex.Message}");
                }
            }
        }
    private string GetCpuNameByCode(string cpuCode)
    {
        if (string.IsNullOrEmpty(cpuCode) || cpuCode == "--")
        {
            return "--";
        }
        
        string cleanCode = cpuCode.Trim().ToUpperInvariant();
        var match = Regex.Match(cleanCode, @"[A-Z0-9\-]+");
        if (match.Success) cleanCode = match.Value;

        var cpuMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 高通骁龙旗舰与高端
            { "SM8850", "第二代骁龙8 至尊版 (Elite Gen 2)" },
            { "SM8850S", "第二代骁龙8 至尊版 降频版" },
            { "SM8750", "骁龙8 至尊版 (Snapdragon 8 Elite)" },
            { "SM8750-AB", "骁龙8 至尊版" },
            { "SM8650", "第三代骁龙8 (8 Gen 3)" },
            { "SM8650-AB", "第三代骁龙8 (8 Gen 3)" },
            { "SM8635", "第三代骁龙8s (8s Gen 3)" },
            { "SM8550", "第二代骁龙8 (8 Gen 2)" },
            { "SM8550-AB", "第二代骁龙8 (8 Gen 2)" },
            { "SM8550-AC", "第二代骁龙8 领先版" },
            { "SM8475", "第一代骁龙8+ (8+ Gen 1)" },
            { "SM8450", "第一代骁龙8 (8 Gen 1)" },
            { "SM8350", "骁龙888" },
            { "SM8350-AC", "骁龙888+" },
            { "SM8250", "骁龙865" },
            { "SM8250-AC", "骁龙870" },
            { "SM8150", "骁龙855" },
            { "SM8150-AC", "骁龙855+" },
            { "SDM845", "骁龙845" },
            { "MSM8998", "骁龙835" },
            { "MSM8996", "骁龙820/821" },

            // 高通骁龙 7 系列
            { "SM7675", "第三代骁龙7+ (7+ Gen 3)" },
            { "SM7550", "第三代骁龙7 (7 Gen 3)" },
            { "SM7475", "第二代骁龙7+ (7+ Gen 2)" },
            { "SM7475-AB", "第二代骁龙7+ (7+ Gen 2)" },
            { "SM7450", "第一代骁龙7 (7 Gen 1)" },
            { "SM7435-AB", "第二代骁龙7s (7s Gen 2)" },
            { "SM7325", "骁龙778G" },
            { "SM7325-AE", "骁龙778G+" },
            { "SM7315", "骁龙782G" },
            { "SM7250", "骁龙765G" },
            { "SM7250-AB", "骁龙765G" },
            { "SM7225", "骁龙750G" },
            { "SM7150", "骁龙730/730G" },
            { "SM7125", "骁龙720G" },
            { "SDM710", "骁龙710" },

            // 高通骁龙 6 / 4 系列
            { "SM6650", "第四代骁龙6 (6 Gen 4)" },
            { "SM6475", "第三代骁龙6 (6 Gen 3)" },
            { "SM6475-AB", "第三代骁龙6 (6 Gen 3)" },
            { "SM6450", "第一代骁龙6 (6 Gen 1)" },
            { "SM6375", "骁龙695" },
            { "SM6350", "骁龙690" },
            { "SM6225", "骁龙680" },
            { "SM6115", "骁龙662" },
            { "SDM660", "骁龙660" },
            { "MSM8953", "骁龙625" },
            { "SM4635", "第二代骁龙4s (4s Gen 2)" },
            { "SM4450", "第二代骁龙4 (4 Gen 2)" },
            { "SM4375", "第一代骁龙4 (4 Gen 1)" },
            { "SM4350", "骁龙480" },
            { "SM4350-AC", "骁龙480+" },

            // 联发科天玑系列
            { "MT6991", "天玑 9400" },
            { "MT6989", "天玑 9300 / 9300+" },
            { "MT6985", "天玑 9200 / 9200+" },
            { "MT6983", "天玑 9000 / 9000+" },
            { "MT6897", "天玑 8300 / 8300-Ultra" },
            { "MT6896", "天玑 8200 / 8200-Ultra" },
            { "MT6895", "天玑 8100" },
            { "MT6895T", "天玑 8100-MAX" },
            { "MT6893", "天玑 1200" },
            { "MT6893Z", "天玑 1300 / 8050" },
            { "MT6891", "天玑 1100 / 8020" },
            { "MT6879", "天玑 8000" },
            { "MT6877", "天玑 1080 / 7050" },
            { "MT6877TT", "天玑 1080 / 7050" },
            { "MT6875", "天玑 820" },
            { "MT6873", "天玑 800" },
            { "MT6853", "天玑 720" },
            { "MT6853T", "天玑 800U" },
            { "MT6835", "天玑 6100+" },
            { "MT6833", "天玑 700 / 6020" },
            { "MT6789", "Helio G99" },
            { "MT6785", "Helio G90/G95" },
            { "MT6769", "Helio G80/G85" },
            { "MT6765", "Helio P35 / G35" },

            // 华为海思麒麟
            { "HI36A0", "麒麟 9000S / 9010" },
            { "HI3690", "麒麟 990" },
            { "HI3680", "麒麟 980" },
            { "HI3670", "麒麟 970" },
            { "HI3660", "麒麟 960" },
            { "HI6260", "麒麟 9000SL / 9000E" },
            { "HI6250", "麒麟 650/655/658/659" },

            // 谷歌 Tensor
            { "GS101", "Google Tensor" },
            { "GS201", "Google Tensor G2" },
            { "ZUMA", "Google Tensor G3" },
            { "ZUMA-PRO", "Google Tensor G4" }
        };

        if (cpuMapping.TryGetValue(cleanCode, out string? name))
        {
            return name;
        }

        // 智能推断未在表中的芯片系列
        if (cleanCode.StartsWith("SM8")) return $"高通骁龙 8系列 ({cleanCode})";
        if (cleanCode.StartsWith("SM7")) return $"高通骁龙 7系列 ({cleanCode})";
        if (cleanCode.StartsWith("SM6")) return $"高通骁龙 6系列 ({cleanCode})";
        if (cleanCode.StartsWith("SM4")) return $"高通骁龙 4系列 ({cleanCode})";
        if (cleanCode.StartsWith("MT69")) return $"联发科天玑 9000系列 ({cleanCode})";
        if (cleanCode.StartsWith("MT68")) return $"联发科天玑系列 ({cleanCode})";
        if (cleanCode.StartsWith("KIRIN") || cleanCode.StartsWith("HI")) return $"海思麒麟芯片 ({cleanCode})";

        return cleanCode;
    }
    
    private async Task<string> GetWindowsVersionAsync()
    {
        try
        {
            string wmicPath = "wmic";
            var processStartInfo = new ProcessStartInfo
            {
                FileName = wmicPath,
                Arguments = "os get Caption,OSArchitecture,Version /format:csv",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            
            using (var process = new Process { StartInfo = processStartInfo })
            {
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.Contains("Microsoft Windows") && line.Contains(","))
                        {
                            var parts = line.Split(',');
                            if (parts.Length >= 4)
                            {
                                string caption = parts[1]?.Trim();
                                string architecture = parts[2]?.Trim();
                                string version = parts[3]?.Trim();
                                
                                if (!string.IsNullOrEmpty(caption))
                                {
                                    // 提取Windows版本号，去掉中文后缀（专业版、旗舰版等）
                                    var match = System.Text.RegularExpressions.Regex.Match(caption, @"Windows\s+(\d+(?:\.\d+)?)");
                                    if (match.Success)
                                    {
                                        return $"Windows {match.Groups[1].Value}";
                                    }
                                    // 如果无法匹配到版本号，返回原始caption
                                    return caption;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"获取Windows版本失败: {ex.Message}");
        }
        
        return "--";
    }

        private void DeviceDetectionToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Primitives.ToggleButton toggleButton)
            {
                return;
            }

            if (toggleButton.IsChecked == true)
            {
                HandleStartDeviceDetection();
            }
            else
            {
                HandleStopDeviceDetection();
            }
        }

        private void ToolSelfCheckToggle_Click(object sender, RoutedEventArgs e)
        {
            var toggleButton = sender as System.Windows.Controls.Primitives.ToggleButton;
            if (toggleButton != null && toggleButton.IsChecked == true)
            {
                try
                {
                    // 获取当前程序的目录
                    string currentDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                    string checkFilePath = System.IO.Path.Combine(currentDirectory, "exe", "check.com");
                    
                    if (System.IO.File.Exists(checkFilePath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = checkFilePath,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        System.Windows.MessageBox.Show($"找不到文件: {checkFilePath}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"打开文件时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private void ToolSelfCheckToggle_Checked(object sender, RoutedEventArgs e)
        {
            // 工具自检开关被选中时的处理逻辑
            // 可以在这里添加需要的功能，比如显示状态信息等
        }

        private void BilibiliButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 使用默认浏览器打开哔哩哔哩链接
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://b23.tv/wk4Qp7a",
                    UseShellExecute = true
                });
                
                // 添加日志消息
                AddLogMessage("系统", "已打开哔哩哔哩主页");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开链接时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                AddLogMessage("错误", $"打开哔哩哔哩链接失败: {ex.Message}");
            }
        }

        private void CoolApkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 使用默认浏览器打开酷安链接
                Process.Start(new ProcessStartInfo
                {
                    FileName = "http://www.coolapk.com/u/33872028",
                    UseShellExecute = true
                });
                
                // 添加日志消息
                AddLogMessage("系统", "已打开酷安主页");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开链接时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                AddLogMessage("错误", $"打开酷安链接失败: {ex.Message}");
            }
        }

        private void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 使用默认浏览器打开GitHub链接
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/Smart-Paocai/SmartTool",
                    UseShellExecute = true
                });
                
                // 添加日志消息
                AddLogMessage("系统", "已打开GitHub仓库");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开链接时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                AddLogMessage("错误", $"打开GitHub链接失败: {ex.Message}");
            }
        }

        private async void RefreshDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddLogMessage("系统", "正在检测并刷新设备状态...");
                if (DeviceStatusText != null)
                {
                    DeviceStatusText.Text = "检测中...";
                }
                if (DeviceDetectionToggle != null && DeviceDetectionToggle.IsChecked != true)
                {
                    DeviceDetectionToggle.IsChecked = true;
                    HandleStartDeviceDetection();
                }
                await RefreshDeviceStatusImmediatelyAsync(terminateRunningTools: false);
                await RefreshStorageMemoryAsync(silent: true);
                AddLogMessage("系统", "设备状态与存储内存刷新完成");
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"刷新设备状态时出错: {ex.Message}");
            }
        }

        private void WirelessDebugButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new Window1();
                win.Owner = this;
                win.Show();
                AddLogMessage("系统", "已打开无线调试窗口");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开无线调试窗口时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                AddLogMessage("错误", $"打开无线调试窗口失败: {ex.Message}");
            }
        }

        private async void RebootCommandComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = sender as System.Windows.Controls.ComboBox;
            if (comboBox == null) return;

            var selectedItem = comboBox.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return;

            string selectedCommand = selectedItem.Content?.ToString() ?? "";

            // 如果选择的是默认项，不执行任何操作
            if (selectedCommand == "更多重启指令↓")
            {
                return;
            }

            try
            {
                // 根据选择的命令执行相应操作
                switch (selectedCommand)
                {
                    case "adb reboot sideload":
                        AddLogMessage("系统", "正在执行: adb reboot sideload");
                        await ExecuteAdbCommand("reboot sideload");
                        break;

                    case "fastboot oem edl":
                        AddLogMessage("系统", "正在执行: fastboot oem edl");
                        await ExecuteFastbootCommand("oem edl");
                        break;

                    case "adb shell reboot -p":
                        AddLogMessage("系统", "正在执行: adb shell reboot -p");
                        await ExecuteAdbCommand("shell reboot -p");
                        break;
                }

                // 执行完命令后，将ComboBox重置为默认选项
                comboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"执行命令失败: {ex.Message}");
                // 出错时也重置为默认选项
                comboBox.SelectedIndex = 0;
            }
        }

        private void Usb3PatchTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            string batchPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "exe",
                "fix_usb3.bat");

            if (!File.Exists(batchPath))
            {
                System.Windows.MessageBox.Show(
                    $"未找到 USB3.0 补丁文件：\n{batchPath}",
                    "文件缺失",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                AddLogMessage("错误", "未找到USB3.0补丁文件");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{batchPath}\"\"",
                    WorkingDirectory = Path.GetDirectoryName(batchPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = true,
                    Verb = "runas"
                });

                AddLogMessage("系统", "已启动USB3.0兼容性补丁");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                AddLogMessage("系统", "用户取消运行USB3.0兼容性补丁");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"运行 USB3.0 补丁时出错：{ex.Message}",
                    "运行失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                AddLogMessage("错误", $"运行USB3.0补丁失败: {ex.Message}");
            }
        }

        private void HandleStartDeviceDetection()
        {
            try
            {
                // 如果定时器已经在运行，先停止它
                if (deviceStatusTimer != null && deviceStatusTimer.IsEnabled)
                {
                    deviceStatusTimer.Stop();
                }
                
                // 重新初始化设备状态监控
                InitializeDeviceStatusMonitoring();
                
                Dispatcher.Invoke(() =>
                {
                    if (DeviceDetectionToggle != null)
                    {
                        DeviceDetectionToggle.IsChecked = true;
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    // 错误处理
                    System.Windows.MessageBox.Show($"启动设备检测时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
        
        // 处理停止检测设备功能
        private void HandleStopDeviceDetection(bool terminateRunningTools = true)
        {
            try
            {
                _isDeviceDetectionEnabled = false;
                unchecked
                {
                    _deviceDetectionVersion++;
                }

                // 停止设备状态监控定时器
                if (deviceStatusTimer != null)
                {
                    deviceStatusTimer.Stop();
                    deviceStatusTimer = null;
                }
                
                // 某些任务（如全自动 ROOT）需要保留当前 ADB Server，避免在重启命令
                // 发出前因 daemon 重建而短暂丢失已记录的设备序列号。
                if (terminateRunningTools)
                {
                    _ = KillAllAdbAndFastbootProcesses();
                }
                
                // 清空设备状态显示
                Dispatcher.Invoke(() =>
                {
                    if (DeviceDetectionToggle != null)
                    {
                        DeviceDetectionToggle.IsChecked = false;
                    }

                    // 清空设备列表
                    DeviceSerials.Clear();
                    
                    // 清空设备状态文本
                    if (DeviceStatusText != null)
                    {
                        SetLocalizedText(DeviceStatusText, "未检测到设备");
                        DeviceStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // 红色 #DC3545
                    }
                    
                    // 清空设备选择下拉框
                    if (MultiDeviceComboBox != null)
                    {
                        MultiDeviceComboBox.SelectedItem = null;
                    }
                    
                    // 清空设备详细信息
                    if (ConnectionTypeText != null)
                    {
                        SetLocalizedText(ConnectionTypeText, "--");
                    }
                    
                    if (DeviceSerialText != null)
                    {
                        DeviceSerialText.Text = "--";
                    }
                    
                    if (DeviceModelText != null)
                    {
                        DeviceModelText.Text = "--";
                    }
                    
                    if (DeviceCodeText != null)
                    {
                        DeviceCodeText.Text = "--";
                    }
                    
                    if (UnlockStatusText != null)
                    {
                        SetLocalizedText(UnlockStatusText, "--");
                    }
                    
                    if (BottomConnectionTypeText != null)
                    {
                        SetLocalizedText(BottomConnectionTypeText, "--");
                    }
                    UpdateBottomConnectionStatusIndicator("未连接", "--");
                    
                    // 清空A/B分区信息
                    if (ABPartitionText != null)
                    {
                        SetLocalizedText(ABPartitionText, "--");
                    }

                    if (SelinuxStatusText != null)
                    {
                        SetLocalizedText(SelinuxStatusText, "--");
                        SelinuxStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
                    }

                    if (CpuManufacturerText != null) CpuManufacturerText.Text = "--";
                    if (CpuCodeNameText != null) CpuCodeNameText.Text = "--";
                    if (CpuNameText != null) CpuNameText.Text = "--";
                    if (WindowsVersionText != null) WindowsVersionText.Text = "--";
                    if (AndroidVersionText != null) AndroidVersionText.Text = "--";
                    if (VersionInfoText != null) VersionInfoText.Text = "--";
                    if (KernelVersionText != null) KernelVersionText.Text = "--";
                    if (BuildDateText != null) BuildDateText.Text = "--";

                    if (BatteryProgressBar != null)
                    {
                        BatteryProgressBar.Maximum = 100;
                        BatteryProgressBar.Value = 0;
                    }
                    if (BatteryValueText != null)
                    {
                        BatteryValueText.Text = "0%";
                    }
                    if (BatteryDetailText != null)
                    {
                        BatteryDetailText.Text = "--";
                    }

                    if (_storageViewModel != null)
                    {
                        _storageViewModel.SetTotalStorage(0);
                        _storageViewModel.StorageUsage = 0;
                        _storageViewModel.SetTotalMemory(0);
                        _storageViewModel.MemoryUsage = 0;
                    }
                    
                    // 重置最后的设备状态和详细信息
                    lastDeviceStatus = "";
                    lastConnectionType = "";
                    lastDeviceSerial = "";
                    lastDeviceModel = "";
                    lastDeviceCode = "";
                    lastAndroidVersion = "";
                    lastUnlockStatus = "";
                    lastABPartition = "";
                    lastSelinuxStatus = "";
                    lastKernelVersion = "";
                    lastBuildDate = "";
                    lastCpuManufacturer = "";
                    lastCpuCodeName = "";
                    lastWindowsVersion = "";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    // 错误处理
                    System.Windows.MessageBox.Show($"停止设备检测时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        // 检测刷机包机型的方法
        private async void FormatDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            FormatDeviceButton.IsEnabled = false;
            var taskStopwatch = Stopwatch.StartNew();

            try
            {
                // 先检测设备连接
                string fastbootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "fastboot.exe");
                string deviceCheckResult = await ExecuteFastbootCommand(
                    fastbootPath, "devices", parseStatusOutput: false);
                var deviceMatch = Regex.Match(
                    deviceCheckResult ?? string.Empty,
                    @"(?m)^(\S+)\s+fastboot\s*$",
                    RegexOptions.IgnoreCase);

                if (!deviceMatch.Success)
                {
                    LogSimpleStatus("错误: 未检测到 Fastboot 设备");
                    return;
                }

                string deviceSerial = deviceMatch.Groups[1].Value;
                LogSimpleStatus($"已连接 {deviceSerial} | Fastboot");
                LogSimpleStatus("正在清除设备数据...");

                // 执行 fastboot erase metadata
                string metadataResult = await ExecuteFastbootCommand(
                    fastbootPath, "erase metadata", parseStatusOutput: false);
                if (CommandOutputIndicatesFailure(metadataResult))
                {
                    LogSimpleStatus($"错误: metadata 分区清除失败 | {GetFastbootErrorSummary(metadataResult)}");
                    return;
                }

                // 执行 fastboot erase userdata
                string userdataResult = await ExecuteFastbootCommand(
                    fastbootPath, "erase userdata", parseStatusOutput: false);
                if (CommandOutputIndicatesFailure(userdataResult))
                {
                    LogSimpleStatus($"错误: userdata 分区清除失败 | {GetFastbootErrorSummary(userdataResult)}");
                    return;
                }

                LogSimpleStatus("操作完成.");
                LogSimpleStatus("[Rebooting]重启设备.");

                // 执行 fastboot reboot
                string rebootResult = await ExecuteFastbootCommand(
                    fastbootPath, "reboot", parseStatusOutput: false);
                if (CommandOutputIndicatesFailure(rebootResult))
                {
                    LogSimpleStatus("警告: 设备数据已清除，但自动重启失败，请手动重启设备");
                }

                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"错误: 设备数据清除失败 | {ex.Message}");
            }
            finally
            {
                FormatDeviceButton.IsEnabled = true;
            }
        }

        // 擦除谷歌锁按钮点击事件
        private async void EraseGoogleLockButton_Click(object sender, RoutedEventArgs e)
        {
            EraseGoogleLockButton.IsEnabled = false;
            var taskStopwatch = Stopwatch.StartNew();

            try
            {
                // 先检测设备连接
                string fastbootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "fastboot.exe");
                string deviceCheckResult = await ExecuteFastbootCommand(
                    fastbootPath, "devices", parseStatusOutput: false);
                var deviceMatch = Regex.Match(
                    deviceCheckResult ?? string.Empty,
                    @"(?m)^(\S+)\s+fastboot\s*$",
                    RegexOptions.IgnoreCase);

                if (!deviceMatch.Success)
                {
                    LogSimpleStatus("错误: 未检测到 Fastboot 设备");
                    return;
                }

                string deviceSerial = deviceMatch.Groups[1].Value;
                LogSimpleStatus($"已连接 {deviceSerial} | Fastboot");

                // 执行 fastboot erase frp
                string frpResult = await ExecuteFastbootCommand(
                    fastbootPath,
                    "erase frp",
                    showNativeOutput: true,
                    parseStatusOutput: false);
                if (CommandOutputIndicatesFailure(frpResult))
                {
                    LogSimpleStatus($"错误: FRP 分区擦除失败 | {GetFastbootErrorSummary(frpResult)}");
                    return;
                }

                LogSimpleStatus("[Rebooting]重启设备.");

                // 执行 fastboot reboot
                string rebootResult = await ExecuteFastbootCommand(
                    fastbootPath, "reboot", parseStatusOutput: false);
                if (CommandOutputIndicatesFailure(rebootResult))
                {
                    LogSimpleStatus("警告: FRP 分区已擦除，但自动重启失败，请手动重启设备");
                }

                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"错误: FRP 分区擦除失败 | {ex.Message}");
            }
            finally
            {
                EraseGoogleLockButton.IsEnabled = true;
            }
        }


    }

    public class DeviceInfo
    {
        public string Model { get; set; } = "";
        public string Version { get; set; } = "";
        public string Type { get; set; } = "";
        public string? DownloadUrl { get; set; }
    }

    public sealed class XiaomiFlashProgressItem
    {
        public XiaomiFlashProgressItem(string partitionName, long length)
        {
            PartitionName = partitionName;
            Length = length;
        }

        public string PartitionName { get; }
        public long Length { get; }
    }

    public sealed class XiaomiFlashProgressState
    {
        public XiaomiFlashProgressState(IReadOnlyList<XiaomiFlashProgressItem> items)
        {
            Items = items;
            TotalBytes = items.Sum(item => item.Length);
            Elapsed = Stopwatch.StartNew();
        }

        public IReadOnlyList<XiaomiFlashProgressItem> Items { get; }
        public long TotalBytes { get; }
        public Stopwatch Elapsed { get; }
        public int CurrentItemIndex { get; set; } = -1;
        public long CompletedBytes { get; set; }
        public long CurrentItemTransferredBytes { get; set; }
        public long CompletedChunkBytes { get; set; }
        public long CurrentChunkExpectedBytes { get; set; }
        public long CurrentChunkReportedBytes { get; set; }
        public long LastReportedBytes { get; set; }
        public bool CurrentCommandFinished { get; set; }
        public string TransferRate { get; set; } = "0MB/s";
    }

    public enum XiaomiFlashMode
    {
        Traditional,
        SlotA
    }
}
