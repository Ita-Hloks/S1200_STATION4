using S7.Net;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;

namespace S1200_STATION4.Components
{
    public class PlcService
    {
        private Plc _plc;
        private CancellationTokenSource _cts;
        private bool _lastConnectionState = false;

        public bool IsConnected => _plc?.IsConnected == true;

        public event Action<bool> ConnectionStatusChanged;

        public async Task ConnectAsync(string ip, short rack, short slot)
        {
            _plc = new Plc(CpuType.S71200, ip, rack, slot);
            await Task.Run(() => _plc.Open());

            if (!_plc.IsConnected)
                throw new InvalidOperationException("PLC连接失败，请检查IP和网络");

            _lastConnectionState = true;
            ConnectionStatusChanged?.Invoke(true);

            // 启动完全在后台执行的心跳任务
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => RunHeartbeatAsync(_cts.Token));
        }

        public void Disconnect()
        {
            _cts?.Cancel();
            _plc?.Close();

            if (_lastConnectionState)
            {
                _lastConnectionState = false;
                ConnectionStatusChanged?.Invoke(false);
            }
        }

        private async Task RunHeartbeatAsync(CancellationToken token)
        {
            // 使用 PeriodicTimer 在后台线程每秒触发一次，完全不占用 UI 线程
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    if (_plc == null || !_plc.IsConnected)
                    {
                        NotifyDisconnect();
                        break;
                    }

                    bool currentStatus = false;
                    try
                    {
                        // 已经在后台循环中，直接使用同步读取，无需再次 Task.Run()
                        _plc.ReadBytes(DataType.Memory, 0, 0, 1);
                        currentStatus = true;
                    }
                    catch
                    {
                        currentStatus = false;
                    }

                    // 仅当状态发生实际改变（断开）时，才采取行动
                    if (!currentStatus && _lastConnectionState)
                    {
                        NotifyDisconnect();
                        break; // 已断开连接，退出当前心跳循环
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Disconnect() 时主动取消任务引发的正常异常，静默处理
            }
        }

        private void NotifyDisconnect()
        {
            _lastConnectionState = false;

            try 
            {
                _plc?.Close();
            } 
            catch { /* 防止Close时再抛异常 */ }

            // 切换回 UI 主线程，执行事件派发和弹窗
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show("PLC 断开连接");
                ConnectionStatusChanged?.Invoke(false);
            });
        }
    }
}
