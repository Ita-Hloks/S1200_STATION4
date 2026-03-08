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
        private readonly object _plcLock = new object();
        private int _failCount = 0;

        public bool IsConnected => _plc?.IsConnected == true;
        private CancellationTokenSource _monitorCts;

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

        public object Read(string variable)
        {
            if (!IsConnected) throw new InvalidOperationException("PLC未连接");
            lock (_plcLock)
            {
                return _plc.Read(variable);
            }
        }

        public void Write(string variable, object value)
        {
            if (!IsConnected) throw new InvalidOperationException("PLC未连接");
            lock (_plcLock)
            {
                _plc.Write(variable, value);
            }
        }

        private async Task RunHeartbeatAsync(CancellationToken token)
        {
            // 使用 PeriodicTimer 在后台线程每秒触发一次，完全不占用 UI 线程
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            _failCount = 0;

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
                        lock (_plcLock)
                        {
                            // 读取通用的 M 区（M0.0）作为心跳包
                            // 即使该地址无权限或越界引发 PlcException，也足以证明底层TCP网络仍在正常通信
                            _plc.ReadBytes(DataType.Memory, 0, 0, 1);
                        }
                        currentStatus = true;
                        _failCount = 0; // 成功则重置计数
                    }
                    catch (PlcException)
                    {
                        // PlcException 表示网络正常、PLC已响应，只是地址无效或被拒绝(比如权限)，说明连接仍在！
                        currentStatus = true;
                        _failCount = 0;
                    }
                    catch (Exception)
                    {
                        _failCount++;
                        // 若由于偶尔网络抖动或某些访问拒绝，允许指定次数的失败容忍
                        currentStatus = _failCount < 3;
                    }

                    // 仅当状态发生实际改变（断开）即连续出错且原本是连接状态时，才采取行动
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

        public async Task StartMonitorAsync(Action<float> onDataReceived)
        {
            _monitorCts = new CancellationTokenSource();

            await Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));

                while (await timer.WaitForNextTickAsync(_monitorCts.Token).ConfigureAwait(false))
                {
                    lock (_plcLock)
                    {
                        var raw = _plc.Read("DB2.DBD0");  // Real类型在S7.Net里是DBD
                        float value = BitConverter.ToSingle(BitConverter.GetBytes((uint)raw), 0);
                        onDataReceived(value);  // 把数据传回去
                    }
                }
            });
        }

        public void StopMonitor()
        {
            _monitorCts?.Cancel();
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
