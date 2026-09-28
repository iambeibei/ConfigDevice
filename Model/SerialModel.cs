using LightGateway.Ultils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace LightGateway.Model
{
    public class SerialModel : INotifyPropertyChanged
    {
        private SerialPort _serial;//声明串口

        public event SerialDataReceivedEventHandler DataReceived;//声明串口数据接收事件

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyname)//通知属性变化
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        public delegate void DataEventHandle(byte[] data);
        public event DataEventHandle SendDataEvent;
        public event DataEventHandle ReciveDataEvent;
        public event DataEventHandle UnStandardReciveDataEvent;

        public event ShutdownEventHandle ShutdownEvent;
        public delegate void ShutdownEventHandle();

        private static bool ForcedExitReadFeame;
        public SerialModel()//构造函数
        {
            _serial = new SerialPort();//实例化串口对象
            _serial.DataReceived += OnDataReceived;//绑定串口数据接收事件

        }
        public void SerialDataRmove()
        {
            _serial.DataReceived -= OnDataReceived;   // 解除绑定
        }
        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)//串口数据接收事件的实现
        {
            DataReceived?.Invoke(sender, e);//调用串口数据接收事件
        }
        public void OpenSerial(string portname, int baudrate, int databit, string stopbit)//打开串口
        {
            // CloseSerial 会释放并重建底层对象，这里兜底防止极端情况下为 null。
            if (_serial == null)
            {
                _serial = new SerialPort();
                _serial.DataReceived += OnDataReceived;
            }

            _serial.PortName = portname;//设置串口名称
            _serial.BaudRate = baudrate;//设置波特率
            _serial.DataBits = databit;//设置数据位
            //设置停止位
            if (stopbit == "0")
            {
                _serial.StopBits = StopBits.None;
            }
            else if (stopbit == "1")
            {
                _serial.StopBits = StopBits.One;
            }
            else if (stopbit == "1.5")
            {
                _serial.StopBits = StopBits.OnePointFive;
            }
            else if (stopbit == "2")
            {
                _serial.StopBits = StopBits.Two;
            }

            _serial.Open();//打开串口
            IsOpen = true;


            //ReadThreadRun();
        }

        private object SPLOCK = new object();//串口对象锁

        /// <summary>
        /// 关闭串口。
        /// 关键：Close + Dispose 之后必须重建 _serial，否则串口对象处于已释放状态，
        /// 后续 OpenSerial 会抛异常（写入成功后自动关闭串口，紧接着换设备重开是常规操作）。
        /// </summary>
        public bool CloseSerial()//关闭串口
        {
            lock (SPLOCK)
            {
                if (_serial != null)
                {
                    try
                    {
                        if (_serial.IsOpen)
                        {
                            _serial.Close();//关闭串口
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                    finally
                    {
                        _serial.DataReceived -= OnDataReceived;
                        _serial.Dispose();//释放串口资源
                        // 重建底层串口对象，使同一个 SerialModel 可以被反复打开/关闭。
                        _serial = new SerialPort();
                        _serial.DataReceived += OnDataReceived;
                        IsOpen = false;
                        SerialPortHasOpen = false;
                    }
                }
            }
            return true;
        }
        public bool IsOpen
        {
            get
            {
                if (_serial == null)
                {
                    return false;
                }

                return _serial.IsOpen;
            }
            set
            {
                OnPropertyChanged("IsOpen");
            }
        }

        public string[] GetPortNames()//获取可用串口名称
        {
            return SerialPort.GetPortNames();//返回可用串口名称数组
        }
        public static byte[] ReadOneFrameData(SerialPort _serialPort, int FirstByteReadTimeOut = 300, int ReadTimeOut = 3)//读取串口数据
        {
            ForcedExitReadFeame = false;
            if (FirstByteReadTimeOut > 0)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (_serialPort != null && _serialPort.IsOpen && _serialPort.BytesToRead < 1 && FirstByteReadTimeOut > stopwatch.ElapsedMilliseconds)
                {
                    if (ForcedExitReadFeame)
                    {
                        return null;
                    }

                    Thread.Sleep(1);
                }
                stopwatch.Stop();
            }

            if (_serialPort == null || !_serialPort.IsOpen || _serialPort.BytesToRead <= 0)
            {
                return null;
            }

            int bytesToRead;
            do
            {
                bytesToRead = _serialPort.BytesToRead;
                Thread.Sleep(ReadTimeOut);
            }
            while (_serialPort != null && _serialPort.IsOpen && bytesToRead != _serialPort.BytesToRead);
            byte[] array = new byte[bytesToRead];
            int num = 0;
            do
            {
                try
                {
                    num += _serialPort.Read(array, num, bytesToRead);//读取数据
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return null;
                }
            }
            while (num != bytesToRead);

            return array;
        }
        public byte[] ReadOneFrameData(int FirstByteReadTimeOut = 300, int ReadTimeOut = 3)
        {
            return ReadOneFrameData(_serial, FirstByteReadTimeOut, ReadTimeOut);
        }

        public byte[] ReadData()//读取串口数据
        {
            int bytesToRead = _serial.BytesToRead;//获取可读字节数
            if (bytesToRead == 0) return new byte[0];//判断有没有字节，没有返回空数组
            byte[] buffer = new byte[bytesToRead];//创建一个字节数组
            _serial.Read(buffer, 0, bytesToRead);//读取字节数组，获取数据

            return buffer;//返回读取到的字节数组
        }

        /// <summary>
        /// 清空串口接收缓冲区（同时清发送缓冲区），丢弃上一次通信或开机日志留下的残留字节。
        /// 发送读命令前调用，避免把残留帧当成本次响应。
        /// 串口未打开或已释放时静默返回，不抛异常。
        /// </summary>
        public void ClearReceiveBuffer()
        {
            lock (SPLOCK)
            {
                if (_serial == null)
                {
                    return;
                }

                try
                {
                    if (_serial.IsOpen)
                    {
                        _serial.DiscardInBuffer();//清空接收缓冲区
                        _serial.DiscardOutBuffer();//清空发送缓冲区
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        private object OnlyOneSendResponesLock = new object();//串口发送和接收锁
        private bool NativeListenForSendAndResponesActive;//串口发送和接收事件监听激活状态
        public int ReadTimeout { get; set; } = 40;//读取超时时间
        public byte[] SendAndResponesAutoCRC(byte[] message, int WaitForResponeTimeOut = 500, bool VailFuncCode = true, bool VailSlaveId = true)
        {
            message = message.Concat(ComputeCRC16(message, 0, message.Length)).ToArray();
            return SendAndRespones(message, WaitForResponeTimeOut, VailFuncCode, VailSlaveId);
        }
        public byte[] SendAndRespones(byte[] message, int WaitForResponeTimeOut = 500, bool VailFuncCode = true, bool VailSlaveId = true)
        {
            lock (OnlyOneSendResponesLock)
            {
                try
                {
                    if (_serial == null)
                    {
                        return null;
                    }

                    _serial.DiscardInBuffer();//清空缓冲区
                    NativeListenForSendAndResponesActive = true;//激活串口发送和接收事件监听
                    if (!Send(message))
                    {
                        return null;
                    }

                    byte[] array = ReadOneFrameData(_serial, WaitForResponeTimeOut, ReadTimeout);
                    if (array == null)
                    {
                        return null;
                    }

                    this.ReciveDataEvent?.Invoke(array);
                    if (VailSlaveId && VailFuncCode && array.Length < 3)
                    {
                        return new byte[0];
                    }
                    if (UseCRC)
                    {
                        byte[] array2 = ComputeCRC16(array, 0, array.Length - 2);
                        if (array2[0] != array[array.Length - 2] || array2[1] != array[array.Length - 1])
                        {
                            return new byte[0];
                        }
                    }

                    if (VailSlaveId && array[0] != message[0])
                    {
                        return new byte[0];
                    }

                    if (VailFuncCode && Util.GetIntegerSomeBit(array[1], 8) == 0 && array[1] != message[1])
                    {
                        return new byte[0];
                    }

                    return array;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.StackTrace);
                    Console.WriteLine(ex.Message);
                    return null;
                }
                finally
                {
                    NativeListenForSendAndResponesActive = false;
                }
            }
        }
        public bool Send(byte[] message)//发送数据
        {
            lock (SPLOCK)
            {
                if (_serial == null)
                {
                    return false;
                }
                //清空接收缓存
                _serial.DiscardInBuffer();
                _serial.Write(message, 0, message.Length);
            }

            this.SendDataEvent?.Invoke(message);
            return true;
        }

        public void SendAutoCRC(byte[] message)
        {
            message = message.Concat(ComputeCRC16(message, 0, message.Length)).ToArray();
            Send(message);
        }

        public static bool UseCRC { get; set; } = true;

        public static byte[] BuildFunc3And4Message(int slaveId, int funcCode, int dataAddress, int registerCount)
        {
            byte[] array = new byte[8]
            {
            (byte)slaveId,
            (byte)funcCode,
            (byte)(dataAddress >> 8),//高位
            (byte)dataAddress,
            (byte)(registerCount >> 8),//高位
            (byte)registerCount,
            0,
            0
            };
            if (UseCRC)
            {
                byte[] array2 = ComputeCRC16(array, 0, array.Length - 2);//计算CRC16校验位
                array[array.Length - 2] = array2[0];//填入CRC校验位的低位
                array[array.Length - 1] = array2[1];//填入CRC校验位的高位

            }
            else
            {
                array = array.Take(6).ToArray();//去掉CRC校验位
            }
            //MessageBox.Show(string.Join(" ", array.Select(b => b.ToString("X2"))));//输出十六进制数据
            return array;
        }

        //**********************
        //CRC16计算函数
        public static byte[] ComputeCRC16(byte[] message, int offset, int length)
        {
            ushort num = ushort.MaxValue;
            byte b = byte.MaxValue;
            byte b2 = byte.MaxValue;
            for (int i = offset; i < length; i++)
            {
                num ^= message[i];
                for (int j = 0; j < 8; j++)
                {
                    ushort num2 = (ushort)(num & 1);
                    num = (ushort)((uint)(num >> 1) & 0x7FFFu);
                    if (num2 == 1)
                    {
                        num = (ushort)(num ^ 0xA001u);
                    }
                }
            }

            byte[] array = new byte[2];
            b = (array[1] = (byte)((uint)(num >> 8) & 0xFFu));//高位
            b2 = (array[0] = (byte)(num & 0xFFu));//低位
            return array;
        }
        public static bool CheckCRC16(byte[] message)//CRC16校验函数
        {
            byte[] array = ComputeCRC16(message, 0, message.Length - 2);
            if (array[0] != message[message.Length - 2] || array[1] != message[message.Length - 1])
            {
                return false;
            }
            return true;
        }
        //CRC16计算函数
        //**********************

        //**********************
        //读取线程
        private bool ReadThreadRunning = false;//读取线程运行标志
        private bool SerialPortHasOpen;//串口是否已打开
        private bool noActiveListen;//是否不进行循环读取
        public int UnStanderReadTimeout { get; set; } = 3;//非标准读取超时时间
        public bool NoActiveListen//是否不进行循环读取
        {
            get
            {
                return noActiveListen;
            }
            set
            {
                noActiveListen = value;
                OnPropertyChanged("NoActiveListen");
            }
        }
        public void ReadThreadRun()
        {

            if (ReadThreadRunning)
            {
                return;
            }

            ReadThreadRunning = true;
            Thread thread = new Thread((ParameterizedThreadStart)delegate
            {
                byte[] array = null;
                while (ReadThreadRunning)
                {
                    if (_serial == null || !_serial.IsOpen)
                    {
                        if (SerialPortHasOpen)
                        {
                            this.ShutdownEvent?.Invoke();
                        }

                        ReadThreadRunning = false;
                        break;
                    }

                    SerialPortHasOpen = true;
                    if (NoActiveListen || NativeListenForSendAndResponesActive)//如果没有主动监听，则不进行循环读取
                    {
                        Thread.Sleep(1);
                    }
                    else
                    {
                        try
                        {
                            lock (OnlyOneSendResponesLock)
                            {
                                array = ReadOneFrameData(_serial, UnStanderReadTimeout, UnStanderReadTimeout);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }

                        if (array != null)
                        {
                            this.ReciveDataEvent?.Invoke(array);//触发接收数据事件
                            if (UseCRC && array.Length > 2)//如果使用CRC校验，并且数据长度大于2字节
                            {
                                if (!CheckCRC16(array))
                                {
                                    if (array.Length > 3)//如果数据长度大于3字节，并且CRC校验失败
                                    {
                                        int num = array[2];//获取字节数
                                        if (array.Length > 4 && array[1] == 66)//如果是读取位状态，则需要将字节序反转
                                        {
                                            num = Util.ByteToShort(array.Skip(2).Take(2).ToArray(), HightFirst: true);//如果是读取位状态，则需要将字节序反转
                                        }

                                        if (num + 5 < array.Length)
                                        {
                                            while (array != null && array.Length != 0)
                                            {
                                                byte[] array2 = array.Take(num + 5).ToArray();
                                                if (!CheckCRC16(array2))
                                                {
                                                    array = null;
                                                }
                                                else
                                                {
                                                    this.UnStandardReciveDataEvent?.Invoke(array2);
                                                    array = array.Skip(num + 5).ToArray();
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        array = null;
                                    }
                                }
                                else
                                {
                                    this.UnStandardReciveDataEvent?.Invoke(array);//触发非标准接收数据事件
                                }
                            }
                        }
                    }
                }

                IsOpen = false;
            });
            thread.IsBackground = true;
            thread.Start();//启动线程
        }
        //读取线程
        //**********************

        //**********************
        //释放资源函数
        public void Dispose()
        {
            if (_serial != null)//如果串口对象不为空
            {
                _serial.DataReceived -= OnDataReceived;//移除串口数据接收事件
                if (_serial.IsOpen)//如果串口已打开
                {
                    _serial.Close();//关闭串口
                }
                _serial.Dispose();//释放串口资源
            }
        }
        //释放资源函数
        //**********************
    }
}
