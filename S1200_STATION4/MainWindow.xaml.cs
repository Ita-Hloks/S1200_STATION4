using S1200_STATION4.Components;
using S7.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace S1200_STATION4
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly PlcService _plcService = new PlcService();

        public MainWindow()
        {
            InitializeComponent();
            _plcService.ConnectionStatusChanged += PlcService_ConnectionStatusChanged;
        }

        private void PlcService_ConnectionStatusChanged(bool isConnected)
        {
            Dispatcher.Invoke(() =>
            {
                ConnectionStatusLight.Fill = isConnected ? Brushes.GreenYellow : Brushes.Red;
                ConnectButton.IsEnabled = !isConnected;
            });
        }

        private async void ConnectButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            ConnectButton.IsEnabled = false; // 防止重复点击
            int ConctWaitTime = 5;

            try
            {
                await _plcService.ConnectAsync("192.168.0.1", 0, 1).WaitAsync(TimeSpan.FromSeconds(ConctWaitTime));
                // 连接成功后 ConnectionStatusChanged 事件会自动触发更新UI
            }
            catch (TimeoutException)
            {
                MessageBox.Show($"PLC连接超时({ConctWaitTime}秒)", "超时",
                                MessageBoxButton.OK, MessageBoxImage.Warning);

                ConnectButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"连接失败：{ex.Message}", "错误",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                ConnectButton.IsEnabled = true; // 连接失败则恢复按钮
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            _plcService.Disconnect();
        }
    }
}