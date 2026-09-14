using Microsoft.Win32;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace StockWidget
{
    public partial class MainWindow : Window
    {
        // -------------------------------------------------------------
        // WIN32 API IMPORTS 
        // -------------------------------------------------------------
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        // -------------------------------------------------------------

        private DispatcherTimer _timer;
        private static readonly HttpClient _client = new HttpClient();

        public MainWindow()
        {
            InitializeComponent();

            _client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/151.0.0.0 Safari/537.36"
            );

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(6000)
            };

            _timer.Tick += async (s, e) => await FetchLiveStockPrice();
            _timer.Start();

            // Refresh stock data immediately on wakeup from sleep
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;

            // Register auto-start on Windows boot/login
            ConfigureAutoStart(true);

            // Register in Windows Control Panel (Programs & Features)
            RegisterInControlPanel(true);

            _ = FetchLiveStockPrice();
        }
        private async Task FetchLiveStockPrice()
        {
            try
            {
                const string url =
                    "https://query1.finance.yahoo.com/v8/finance/chart/SWIGGY.NS" +
                    "?range=1d&interval=1m";

                HttpResponseMessage response = await _client.GetAsync(url);

                string jsonResponse = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"HTTP Status: {(int)response.StatusCode}");
                Console.WriteLine(jsonResponse);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception(
                        $"Yahoo returned {(int)response.StatusCode}: {response.ReasonPhrase}"
                    );
                }

                using JsonDocument doc = JsonDocument.Parse(jsonResponse);

                JsonElement result = doc.RootElement
                    .GetProperty("chart")
                    .GetProperty("result")[0];

                JsonElement meta = result.GetProperty("meta");

                double currentPrice =
                    meta.GetProperty("regularMarketPrice").GetDouble();

                double previousClose;

                // Yahoo can expose previous close under different fields.
                if (meta.TryGetProperty("previousClose", out JsonElement pc))
                {
                    previousClose = pc.GetDouble();
                }
                else
                {
                    previousClose =
                        meta.GetProperty("chartPreviousClose").GetDouble();
                }

                double percentChange =
                    previousClose == 0
                        ? 0
                        : ((currentPrice - previousClose) / previousClose) * 100;

                List<double> prices = new List<double>();
                if (result.TryGetProperty("indicators", out JsonElement indicators) &&
                    indicators.TryGetProperty("quote", out JsonElement quoteArr) &&
                    quoteArr.GetArrayLength() > 0 &&
                    quoteArr[0].TryGetProperty("close", out JsonElement closeArr))
                {
                    foreach (JsonElement val in closeArr.EnumerateArray())
                    {
                        if (val.ValueKind == JsonValueKind.Number)
                        {
                            prices.Add(val.GetDouble());
                        }
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    UpdateUI(currentPrice, percentChange, prices);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("STOCK API ERROR:");
                Console.WriteLine(ex.ToString());

                Dispatcher.Invoke(() =>
                {
                    PriceText.Text = "Error";
                    ChangeText.Text = "API";
                });
            }
        }

        private void UpdateUI(double price, double percentChange, List<double> prices)
        {
            // Format price to 2 decimal places with Rupee symbol
            PriceText.Text = $"₹{price:F2}";

            // Absolute value for text (we use the arrow for direction)
            ChangeText.Text = $"{Math.Abs(percentChange):F2}%";

            //Portfolio Calculation
            double GetPortfolioValue = price * 10;
            Portfolio.Text = $"Portfolio: ₹{GetPortfolioValue}";

            // Define UI Colors
            var greenText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#137333"));
            var greenBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6F4EA"));

            var redText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A50E0E"));
            var redBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCE8E6"));

            // Apply colors and arrows based on market direction
            if (percentChange >= 0)
            {
                ChangeArrow.Text = "↑ ";
                ChangeArrow.Foreground = greenText;
                ChangeText.Foreground = greenText;
                ChangeBadge.Background = greenBg;
            }
            else
            {
                ChangeArrow.Text = "↓ ";
                ChangeArrow.Foreground = redText;
                ChangeText.Foreground = redText;
                ChangeBadge.Background = redBg;
            }

            UpdateChart(prices, percentChange >= 0);
        }

        private void UpdateChart(List<double> prices, bool isPositive)
        {
            var strokeBrush = isPositive
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#188038"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D93025"));

            StockChart.Stroke = strokeBrush;

            if (prices == null || prices.Count < 2)
            {
                return;
            }

            const double width = 240.0;
            const double height = 40.0;
            const double padding = 4.0;

            double minPrice = prices.Min();
            double maxPrice = prices.Max();
            double range = maxPrice - minPrice;

            PointCollection points = new PointCollection();
            int count = prices.Count;

            for (int i = 0; i < count; i++)
            {
                double x = (double)i / (count - 1) * width;
                double y;

                if (range == 0)
                {
                    y = height / 2.0;
                }
                else
                {
                    // Map price to vertical position (higher price = smaller Y in screen coordinates)
                    double normalized = (prices[i] - minPrice) / range;
                    y = padding + (height - 2 * padding) * (1.0 - normalized);
                }

                points.Add(new Point(x, y));
            }

            StockChart.Points = points;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Position the widget in the top right corner of the primary display's working area
            var desktopWorkingArea = SystemParameters.WorkArea;
            this.Left = desktopWorkingArea.Right - this.Width - 10;
            this.Top = desktopWorkingArea.Top + 10;

            var interopHelper = new WindowInteropHelper(this);
            IntPtr hwnd = interopHelper.Handle;

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                Console.WriteLine("System resumed from sleep - refreshing stock price...");
                _ = FetchLiveStockPrice();
            }
        }

        private void ConfigureAutoStart(bool enable)
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return;

                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (enable)
                    {
                        key.SetValue("StockWidget", $"\"{exePath}\"");
                    }
                    else
                    {
                        key.DeleteValue("StockWidget", false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AutoStart Registry Error: {ex.Message}");
            }
        }

        private void RegisterInControlPanel(bool enable)
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return;

                string uninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\StockWidget";

                if (enable)
                {
                    using RegistryKey? key = Registry.CurrentUser.CreateSubKey(uninstallKeyPath);
                    if (key != null)
                    {
                        key.SetValue("DisplayName", "Stock Widget");
                        key.SetValue("DisplayVersion", "1.0.0");
                        key.SetValue("Publisher", "Rajesh Nambi");
                        key.SetValue("DisplayIcon", exePath);
                        key.SetValue("InstallLocation", AppDomain.CurrentDomain.BaseDirectory);
                        key.SetValue("UninstallString", $"cmd.exe /c taskkill /f /im StockWidget.exe & reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v StockWidget /f & reg delete \"HKCU\\{uninstallKeyPath}\" /f");
                        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }
                else
                {
                    Registry.CurrentUser.DeleteSubKeyTree(uninstallKeyPath, false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Control Panel Registry Error: {ex.Message}");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        }
    }
}