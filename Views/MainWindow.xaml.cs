using Microsoft.Win32;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Stocky.Views
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

        // Configurable settings
        private string _tickerSymbol = "SWIGGY";
        private int _quantity = 10;
        private static readonly string _settingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Stocky", "settings.json");

        public MainWindow()
        {
            InitializeComponent();

            // Load saved settings before anything else
            LoadSettings();
            ApplySettingsToUI();

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

        // -------------------------------------------------------------
        // SETTINGS PERSISTENCE
        // -------------------------------------------------------------
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    using JsonDocument doc = JsonDocument.Parse(json);
                    JsonElement root = doc.RootElement;

                    if (root.TryGetProperty("TickerSymbol", out JsonElement tickerEl))
                        _tickerSymbol = tickerEl.GetString() ?? "SWIGGY";

                    if (root.TryGetProperty("Quantity", out JsonElement qtyEl))
                        _quantity = qtyEl.GetInt32();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load settings: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                string? dir = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var settings = new { TickerSymbol = _tickerSymbol, Quantity = _quantity };
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        private void ApplySettingsToUI()
        {
            TickerText.Text = _tickerSymbol.Replace(".NS", "").Replace(".BO", "").ToUpper();
            CompanyText.Text = $"{TickerText.Text}";
        }

        // -------------------------------------------------------------
        // SETTINGS PANEL EVENT HANDLERS
        // -------------------------------------------------------------
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Pre-fill inputs with current values
            TickerInput.Text = _tickerSymbol;
            QuantityInput.Text = _quantity.ToString();

            // Show settings panel, hide main content
            MainContent.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Visible;
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            string newTicker = TickerInput.Text.Trim().ToUpper();
            if (string.IsNullOrEmpty(newTicker))
            {
                MessageBox.Show("Ticker symbol cannot be empty.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(QuantityInput.Text.Trim(), out int newQty) || newQty < 0)
            {
                MessageBox.Show("Please enter a valid quantity (0 or more).", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _tickerSymbol = newTicker;
            _quantity = newQty;

            SaveSettings();
            ApplySettingsToUI();

            // Hide settings panel, show main content
            SettingsPanel.Visibility = Visibility.Collapsed;
            MainContent.Visibility = Visibility.Visible;

            // Refresh data with the new ticker
            PriceText.Text = "Loading...";
            Portfolio.Text = "Loading..";
            ChangeArrow.Text = "-";
            ChangeText.Text = "-";
            _ = FetchLiveStockPrice();
        }

        private void CancelSettings_Click(object sender, RoutedEventArgs e)
        {
            // Hide settings panel, show main content (no changes applied)
            SettingsPanel.Visibility = Visibility.Collapsed;
            MainContent.Visibility = Visibility.Visible;
        }

        // -------------------------------------------------------------
        // STOCK DATA FETCHING
        // -------------------------------------------------------------
        private async Task FetchLiveStockPrice()
        {
            try
            {
                // Append .NS if the ticker doesn't already have an exchange suffix
                string yahooTicker = _tickerSymbol;
                if (!yahooTicker.Contains('.'))
                    yahooTicker += ".NS";

                string url =
                    $"https://query1.finance.yahoo.com/v8/finance/chart/{yahooTicker}" +
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
            double GetPortfolioValue = price * _quantity;
            Portfolio.Text = $"Portfolio: ₹{GetPortfolioValue:F2}";

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
                        key.SetValue("Stocky", $"\"{exePath}\"");
                    }
                    else
                    {
                        key.DeleteValue("Stocky", false);
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

                string uninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Stocky";

                if (enable)
                {
                    using RegistryKey? key = Registry.CurrentUser.CreateSubKey(uninstallKeyPath);
                    if (key != null)
                    {
                        key.SetValue("DisplayName", "Stocky");
                        key.SetValue("DisplayVersion", "1.0.0");
                        key.SetValue("Publisher", "Rajesh Nambi");
                        key.SetValue("DisplayIcon", exePath);
                        key.SetValue("InstallLocation", AppDomain.CurrentDomain.BaseDirectory);
                        key.SetValue("UninstallString", $"cmd.exe /c taskkill /f /im Stocky.exe & reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v Stocky /f & reg delete \"HKCU\\{uninstallKeyPath}\" /f");
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
