using LightGateway.Command;
using LightGateway.Model;
using LightGateway.Ultils;
using LightGateway.View;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WebApiClientModule;
using static LightGateway.Command.Base;

namespace LightGateway.ViewModel
{
    [AddINotifyPropertyChangedInterface]//自动添加INotifyPropertyChanged接口
    public class MainViewModel
    {
        private SerialModel serialModel;//串口对象
        private bool _suppressShelfToBaseConfig;//抑制从读取匹配反向覆盖基础配置
        private bool _suppressAutoSave;//批量同步老化架时抑制逐条落盘，统一在结束时保存一次
        private bool _isSyncingRacks;//老化架接口同步进行中，防止重入
        private readonly RackApiClient _rackApiClient = new();
        public bool ButtonIsEnabled { get; set; } = false;//按钮是否可用
        public bool ComboBoxEB { get; set; } = true;//下拉框是否可用

        /// <summary>
        /// 串口开/关按钮的文案。由 ApplySerialUiState 统一维护，
        /// 使「手动点击」与「写入成功后自动关闭串口」两条路径表现一致。
        /// </summary>
        public string SerialButtonContent { get; set; } = "打开串口";

        /// <summary>
        /// 串口开/关按钮的背景色：打开时红色（提示可关闭），关闭时绿色（提示可打开）。
        /// </summary>
        public Brush SerialButtonBackground { get; set; } = Brushes.Green;

        private static readonly Brush SerialOpenBrush = new SolidColorBrush(Colors.Red);
        private static readonly Brush SerialClosedBrush = new SolidColorBrush(Colors.Green);

        #region 点击界面显示点击界面
        public bool IsInsideWindow { get; set; }

        // 根据状态返回对应的颜色
        public Brush HighlightColor => IsInsideWindow ? Brushes.LightGreen : Brushes.LightGray;
        #endregion

        public ObservableCollection<ExDeviceModel> ExternaldeviceList { get; set; }
        public ExDeviceModel SelctedExDev { get; set; }

        public AgingFunction SelectedAgingFunction { get; set; }

        #region 老化架配置
        public ObservableCollection<AgingShelfModel> AgingShelves { get; set; }
        public ObservableCollection<AgingShelfModel> ComboBoxAgingItems { get; set; }
        public AgingShelfModel ReadAgingShelfItem { get; set; }
        public AgingShelfModel SelectedAgingShelf { get; set; }

        /// <summary>
        /// 写入后回读等待遮罩是否显示。
        /// </summary>
        public bool IsReadBackWaiting { get; set; }

        /// <summary>
        /// 等待进度条值（0-100）。
        /// </summary>
        public double WaitProgress { get; set; }

        /// <summary>
        /// 等待遮罩提示文字。
        /// </summary>
        public string WaitMessage { get; set; } = "正在等待回读，请勿操作...";

        /// <summary>
        /// 老化架接口同步状态提示。
        /// </summary>
        public string RackSyncStatus { get; set; } = "尚未同步老化架";

        /// <summary>
        /// 「同步老化架」按钮是否可用（同步过程中禁用）。
        /// </summary>
        public bool CanSyncRacks { get; set; } = true;

        #region 点位选择与设备分配

        /// <summary>
        /// 当前老化架下的可用点位。
        /// </summary>
        public ObservableCollection<RackPointItem> AgingPoints { get; set; }

        /// <summary>
        /// 当前选中的点位，value 使用点位的 id。
        /// </summary>
        public RackPointItem SelectedAgingPoint { get; set; }

        /// <summary>
        /// 点位下拉框是否可用（已选中接口同步的老化架且点位加载完成）。
        /// </summary>
        public bool PointsEnabled { get; set; }

        /// <summary>
        /// 点位列表加载中。
        /// </summary>
        public bool IsLoadingPoints { get; set; }

        /// <summary>
        /// 点位查询的「仅显示可用节点」开关，对应接口的 onlyAvailable 字段，默认 true。
        /// 勾选时只查询可用状态的节点；取消勾选时返回全部节点、跳过可用性过滤。
        /// </summary>
        public bool OnlyAvailable { get; set; } = true;

        /// <summary>
        /// 是否具备点位绑定条件（节点 ID、接口同步的老化架、点位齐全且串口已打开）。
        /// 绑定已并入「写入」链路作为后置步骤，此属性仅用于状态展示。
        /// </summary>
        public bool CanAssignDevice { get; set; }

        /// <summary>
        /// 点位分配请求进行中。
        /// </summary>
        public bool IsAssigning { get; set; }

        /// <summary>
        /// 点位加载 / 分配的状态提示。
        /// </summary>
        public string PointAssignStatus { get; set; } = "尚未选择老化架";

        /// <summary>
        /// 点位加载请求序号，用于丢弃过期响应，避免快速切换老化架时旧结果覆盖新结果。
        /// </summary>
        private int _pointsRequestSeq;

        #endregion
        #endregion

        public MainViewModel()//构造函数
        {
            ExternaldeviceList = new ObservableCollection<ExDeviceModel>();
            AgingShelves = new ObservableCollection<AgingShelfModel>();
            ComboBoxAgingItems = new ObservableCollection<AgingShelfModel>();
            AgingPoints = new ObservableCollection<RackPointItem>();
            ReadAgingShelfItem = new AgingShelfModel
            {
                Id = "__read_config__",
                IsReadConfigItem = true,
                CustomDisplayName = "读取配置"
            };
            serialModel = new SerialModel();
            CmdClick = new BaseCommand<Button>(CmdCmdClickAction);
            LoadAgingShelves();
            RefreshAvailablePorts();
            RefreshComboBoxAgingItems();

            // 启动时静默同步一次老化架列表：失败不弹窗打扰，只在状态栏提示。
            _ = SyncAgingShelvesFromApiAsync(silent: true);
        }

        #region 按钮命令
        public ICommand CmdClick { get; set; }
        private async void CmdCmdClickAction(Button obj)//按钮点击事件
        {
            switch (obj.Content?.ToString())
            {
                case "读取":
                    try
                    {
                        DoReadConfig();
                        SelectReadShelfItem();

                        if (WroteSnapshot != null && WroteSnapshot.Count > 0)
                        {
                            bool matched = CheckWriteOK(WroteSnapshot, out _);
                            ReadMatchStatus = matched ? "与最近写入配置一致" : "与最近写入配置不一致";
                            MessageBox.Show(
                                matched ? "读取配置成功，且与最近写入配置一致。" : "读取配置成功，但与最近写入配置不一致。",
                                "读取配置",
                                MessageBoxButton.OK,
                                matched ? MessageBoxImage.Information : MessageBoxImage.Warning);
                        }
                        else
                        {
                            ReadMatchStatus = "尚未比较";
                            MessageBox.Show("读取配置成功。", "读取配置", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        ReadStatusMessage = $"读取失败：{ex.Message}";
                        MessageBox.Show(ReadStatusMessage, "读取配置", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    break;
                case "写入":
                    if (IsWriting)
                    {
                        return;
                    }
                    IsWriting = true;
                    ButtonIsEnabled = false;
                    ComboBoxEB = false;
                    WriteOutcome writeOutcome;
                    try
                    {
                        // 写设备 → 等待回读 → 校验，校验一致才绑定点位（单一入口，「写入并重新读取」已移除）。
                        // 成功与失败都不再各自弹窗，统一由结果对象带到 finally 之后展示。
                        writeOutcome = await WriteVerifyAndBindAsync();
                    }
                    catch (Exception ex)
                    {
                        // 兜底：WriteVerifyAndBindAsync 内部已消化异常，这里防御未预期情况。
                        writeOutcome = WriteOutcome.Error("写入失败", "写入过程中发生未预期错误，流程已中止。", ex.Message);
                        ReadStatusMessage = $"写入或回读失败：{ex.Message}";
                    }
                    finally
                    {
                        // 遮罩必须先消失，否则会盖住随后弹出的结果窗。
                        IsReadBackWaiting = false;
                        IsWriting = false;
                        IsAssigning = false;
                        // 此处刻意不恢复 ButtonIsEnabled：结果窗弹出期间保持禁用，避免再次触发写入。
                        UpdateCanAssignDevice();
                    }

                    // 结果反馈：无论成功还是失败都弹一次，失败时给出明确原因。
                    if (writeOutcome.ShowDialog)
                    {
                        bool confirmed = ShowWriteResultDialog(writeOutcome);
                        // 只有「写入成功且绑定成功」时，用户点击确认才自动关闭串口。
                        if (confirmed && writeOutcome.CanCloseSerial)
                        {
                            CloseSerialAfterWrite();
                        }
                    }

                    ApplySerialUiState(serialModel.IsOpen);
                    break;

                case "同步老化架":
                    _ = SyncAgingShelvesFromApiAsync(silent: false);
                    break;

                case "删除老化架":
                    if (SelectedAgingShelf == null)
                    {
                        MessageBox.Show("请先选择一个老化架。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        break;
                    }
                    if (SelectedAgingShelf.IsReadConfigItem)
                    {
                        MessageBox.Show("读取配置项会随每次读取自动更新，不能手动删除。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        break;
                    }
                    if (MessageBox.Show($"确定要删除老化架“{SelectedAgingShelf.DisplayName}”吗？", "删除老化架",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    {
                        break;
                    }
                    SelectedAgingShelf!.PropertyChanged -= AutoSaveHandler;
                    AgingShelves.Remove(SelectedAgingShelf);
                    SaveAgingShelves();
                    RefreshComboBoxAgingItems();
                    break;

                case "读取并匹配老化架":
                    try
                    {
                        DoReadConfig();
                        TryMatchAgingShelfFromRead();
                        MessageBox.Show("读取配置成功。", "读取配置", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        ReadStatusMessage = $"读取失败：{ex.Message}";
                        MessageBox.Show(ReadStatusMessage, "读取配置", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    break;

                case "刷新":
                    RefreshAvailablePorts();
                    break;
                case "打开串口":
                    if (OpenSerial())
                    {
                        ApplySerialUiState(true);
                    }

                    break;

                case "关闭串口":
                    if (CloseSerial())
                    {
                        ApplySerialUiState(false);
                    }

                    break;

                case "测试":



                    break;

                case "获取协议":

                    if(ProtoID==0)
                    {
                        MessageBox.Show("请输入协议ID");
                        break;
                    }
                    protos= ProtocolCommandParser.Parse(await GetProto(ProtoID));

                    break;

                case "添加PreAction":

                    SelectedAgingFunction.Action.PreAction.Add(new PreAction { ParaName="",ParaValue=""});
                    break;
                case "删掉PreAction":
                    if (SelectedAgingFunction.Action.PreAction.Count > 0)
                    {
                        //删除最后一个设备配置
                        SelectedAgingFunction.Action.PreAction.RemoveAt(SelectedAgingFunction.Action.PreAction.Count - 1);
                    }
                    break;

                case "添加AfAction":

                    SelectedAgingFunction.Action.AfterAction.Add(new AfterAction { ParaName = "", ParaValue = "" });
                    break;
                case "删掉AfAction":
                    if (SelectedAgingFunction.Action.AfterAction.Count > 0)
                    {
                        //删除最后一个设备配置
                        SelectedAgingFunction.Action.AfterAction.RemoveAt(SelectedAgingFunction.Action.AfterAction.Count - 1);
                    }
                    break;

                case "添加读取参数":

                    SelectedAgingFunction.SampleData.Add(new SampleData { Value=""});
                    break;
                case "删掉读取参数":
                    if (SelectedAgingFunction.SampleData.Count > 0)
                    {
                        //删除最后一个设备配置
                        SelectedAgingFunction.SampleData.RemoveAt(SelectedAgingFunction.SampleData.Count - 1);
                    }
                    break;

                case "添加设备":
                    ExternaldeviceList.Add(new ExDeviceModel());
                    break;
                case "删除设备":
                    if (ExternaldeviceList.Count > 0)
                    {
                        //删除最后一个设备配置
                        ExternaldeviceList.RemoveAt(ExternaldeviceList.Count - 1);
                    }
                    break;
                default:
                    break;
            }
        }


        #endregion

        #region http获取协议函数
        public ProtocolParseResult protos { get; set; }
        public long ProtoID { get; set; } = 1253628928;
        private async Task<string>  GetProto(long protocolId)
        {
            var client = new ProtocolJsonApiClient("http://10.16.160.51:5000");

            string json11 = await client.GetJsonAsync(protocolId);

            Console.WriteLine("获取到的协议 JSON：");
            Console.WriteLine(json11);

            return json11;

        }

        #endregion

        #region 配置属性
        public bool IsWriting { get; set; } = false;
        public List<KeyValuePair<string, string>> WroteSnapshot { get; set; } = new List<KeyValuePair<string, string>>();
        #endregion

        /// <summary>
        /// 写入前的配置校验。不再自行弹窗，把缺失项收集起来交给统一的结果弹窗展示。
        /// </summary>
        private bool TryValidateConfig(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(ReadSSID) || string.IsNullOrWhiteSpace(ReadSSID1))
            {
                errors.Add("WiFi 名称未填写");
            }
            if (string.IsNullOrWhiteSpace(ReadWIFI_PS) || string.IsNullOrWhiteSpace(ReadWIFI_PS1))
            {
                errors.Add("WiFi 密码未填写");
            }
            if (string.IsNullOrWhiteSpace(ReadMesh_ID))
            {
                errors.Add("MeshID 未填写");
            }
            if (string.IsNullOrWhiteSpace(ReadMesh_PS))
            {
                errors.Add("Mesh 密码未填写");
            }
            if (string.IsNullOrWhiteSpace(ReadUDP_ID))
            {
                errors.Add("节点 ID 未填写");
            }
            //if (string.IsNullOrWhiteSpace(ReadUDPPort) || !int.TryParse(ReadUDPPort, out int udpPort) || udpPort <= 0)
            //{
            //    errors.Add("UDP 端口未填写或不是正整数");
            //}
            if (string.IsNullOrWhiteSpace(ReadSERVER_IP))
            {
                errors.Add("服务器 IP 未填写");
            }
            if (string.IsNullOrWhiteSpace(ReadSERVER_UDP_Port) || !int.TryParse(ReadSERVER_UDP_Port, out int serverPort) || serverPort <= 0)
            {
                errors.Add("服务器端口未填写或不是正整数");
            }

            return errors.Count == 0;
        }

        private void DoReadConfig()
        {
            var readconfig = new { Cmd = "ReadConfig" };
            string json = JsonSerializer.Serialize(readconfig);
            byte[] message = Encoding.UTF8.GetBytes(json);

            // 发送前先丢弃接收缓冲区里的残留数据（上一次通信的余帧、设备开机日志等），
            // 否则 ReadOneFrameData 可能把脏帧当成本次 ReadConfig 的响应。
            serialModel.ClearReceiveBuffer();
            serialModel.Send(message);
            byte[] reidata = serialModel.ReadOneFrameData(FirstByteReadTimeOut: 2000, ReadTimeOut: 30);
            if (reidata == null || reidata.Length == 0)
            {
                throw new TimeoutException("ESP32 未在规定时间内返回配置");
            }
            string readjson = Encoding.UTF8.GetString(reidata);
            JsonParse(readjson);
        }

        private List<KeyValuePair<string, string>> MakeWroteSnapshot()
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("IsRoot", ReadIsRoot ?? ""),
                new KeyValuePair<string, string>("SSID", ReadSSID ?? ""),
                new KeyValuePair<string, string>("WIFI_PS", ReadWIFI_PS ?? ""),
                new KeyValuePair<string, string>("SSID1", ReadSSID1 ?? ""),
                new KeyValuePair<string, string>("WIFI_PS1", ReadWIFI_PS1 ?? ""),
                new KeyValuePair<string, string>("Chanel", ReadChanel ?? ""),
                new KeyValuePair<string, string>("Mesh_ID", ReadMesh_ID ?? ""),
                new KeyValuePair<string, string>("Mesh_PS", ReadMesh_PS ?? ""),
                // DEVICE_ID 对应读回属性 ReadUDP_ID（节点ID），命名历史遗留
                new KeyValuePair<string, string>("DEVICE_ID", ReadUDP_ID ?? ""),
                new KeyValuePair<string, string>("UDP_Port", ReadUDPPort ?? ""),
                new KeyValuePair<string, string>("SERVER_IP", ReadSERVER_IP ?? ""),
                new KeyValuePair<string, string>("SERVER_UDP_Port", ReadSERVER_UDP_Port ?? ""),
                new KeyValuePair<string, string>("AgingNumber", ReadAgingNumber ?? "")
            };
        }

        private string GetSnapshotValue(string key)
        {
            return WroteSnapshot.First(kv => kv.Key == key).Value;
        }

        private List<KeyValuePair<string, string>> GetReadSnapshot()
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("IsRoot", ReadIsRoot ?? ""),
                new KeyValuePair<string, string>("SSID", ReadSSID ?? ""),
                new KeyValuePair<string, string>("WIFI_PS", ReadWIFI_PS ?? ""),
                new KeyValuePair<string, string>("SSID1", ReadSSID1 ?? ""),
                new KeyValuePair<string, string>("WIFI_PS1", ReadWIFI_PS1 ?? ""),
                new KeyValuePair<string, string>("Chanel", ReadChanel ?? ""),
                new KeyValuePair<string, string>("Mesh_ID", ReadMesh_ID ?? ""),
                new KeyValuePair<string, string>("Mesh_PS", ReadMesh_PS ?? ""),
                new KeyValuePair<string, string>("DEVICE_ID", ReadUDP_ID ?? ""),
                new KeyValuePair<string, string>("UDP_Port", ReadUDPPort ?? ""),
                new KeyValuePair<string, string>("SERVER_IP", ReadSERVER_IP ?? ""),
                new KeyValuePair<string, string>("SERVER_UDP_Port", ReadSERVER_UDP_Port ?? ""),
                new KeyValuePair<string, string>("AgingNumber", ReadAgingNumber ?? "")
            };
        }

        private bool CheckWriteOK(List<KeyValuePair<string, string>> expected, out List<string> diffs)
        {
            diffs = new List<string>();
            var actual = GetReadSnapshot();
            foreach (var exp in expected)
            {
                var act = actual.FirstOrDefault(kv => kv.Key == exp.Key);
                if (!string.Equals(exp.Value, act.Value, StringComparison.Ordinal))
                {
                    diffs.Add($"{exp.Key}: 期望 '{exp.Value}'，实际 '{act.Value}'");
                }
            }
            return diffs.Count == 0;
        }

        #region 老化架配置

        /// <summary>
        /// PropertyChanged.Fody 自动调用：当 SelectedAgingShelf 改变时同步到基础配置。
        /// </summary>
        /// <summary>
        /// PropertyChanged.Fody 自动调用：选中的老化架变化时同步基础配置，并重新拉取点位。
        /// </summary>
        private async void OnSelectedAgingShelfChanged()
        {
            ApplySelectedAgingShelfToBaseConfig();
            await ReloadPointsForSelectedShelfAsync();
        }

        private void ApplySelectedAgingShelfToBaseConfig()
        {
            if (SelectedAgingShelf == null || _suppressShelfToBaseConfig)
            {
                return;
            }

            ReadSSID = SelectedAgingShelf.SSID;
            ReadWIFI_PS = SelectedAgingShelf.WIFI_PS;
            ReadSSID1 = SelectedAgingShelf.SSID1;
            ReadWIFI_PS1 = SelectedAgingShelf.WIFI_PS1;
            ReadChanel = SelectedAgingShelf.Channel;
            ReadMesh_ID = SelectedAgingShelf.Mesh_ID;
            ReadMesh_PS = SelectedAgingShelf.Mesh_PS;
            ReadAgingNumber = SelectedAgingShelf.ShelfNumber;
        }

        private void TryMatchAgingShelfFromRead()
        {
            UpdateReadAgingShelfItemFromRead();

            // 命中本地模板则选中模板，否则回落到“读取配置”项。
            var match = AgingShelves.FirstOrDefault(s => s.ShelfNumber == ReadAgingNumber);
            var target = match ?? ReadAgingShelfItem;

            // 重建下拉项（不恢复读取前的旧选择），再由本方法决定最终选中项。
            // 读取后 ComboBox 总是会切到「读取配置」，因此此后再次点击任意老化架模板时
            // 选中项必然改变，从而触发 OnSelectedAgingShelfChanged 重新套用模板数据。
            RefreshComboBoxAgingItems(restoreSelection: false);

            _suppressShelfToBaseConfig = true;
            // 先置空再赋值，确保即便目标与当前项相同也会产生一次选中变化。
            SelectedAgingShelf = null!;
            SelectedAgingShelf = target;
            _suppressShelfToBaseConfig = false;

            if (match != null)
            {
                ReadStatusMessage += $"（已匹配本地老化架模板：{match.DisplayName}）";
            }
            else if (!string.IsNullOrWhiteSpace(ReadAgingNumber))
            {
                ReadStatusMessage += $"（未找到与老化架号 {ReadAgingNumber} 匹配的本地模板，已生成读取配置项）";
            }
        }

        /// <summary>
        /// 基础配置「读取」后调用：把设备返回值同步到 ComboBox 的“读取配置”项并选中它。
        /// 有意不回写基础配置，避免老化架模板覆盖刚读到的设备真实值。
        /// 由于总是由「读取配置」项切回其它项，之后再次点击同一老化架模板时选中会发生变化，
        /// 从而触发 OnSelectedAgingShelfChanged 重新应用模板数据。
        /// </summary>
        private void SelectReadShelfItem()
        {
            UpdateReadAgingShelfItemFromRead();

            var match = AgingShelves.FirstOrDefault(s => s.ShelfNumber == ReadAgingNumber);
            if (match != null)
            {
                ReadStatusMessage += $"（已匹配本地老化架模板：{match.DisplayName}）";
            }
            else if (!string.IsNullOrWhiteSpace(ReadAgingNumber))
            {
                ReadStatusMessage += $"（未找到与老化架号 {ReadAgingNumber} 匹配的本地模板）";
            }

            // 重建下拉项，不恢复读取前的旧选择；统一改为选中“读取配置”项。
            RefreshComboBoxAgingItems(restoreSelection: false);

            _suppressShelfToBaseConfig = true;
            // 先置空再赋值，确保即使目标与当前项相同也会产生一次选中变化。
            SelectedAgingShelf = null!;
            SelectedAgingShelf = ReadAgingShelfItem;
            _suppressShelfToBaseConfig = false;
        }

        private void UpdateReadAgingShelfItemFromRead()
        {
            if (ReadAgingShelfItem == null)
            {
                return;
            }

            ReadAgingShelfItem.ShelfNumber = ReadAgingNumber ?? "";
            ReadAgingShelfItem.SSID = ReadSSID ?? "";
            ReadAgingShelfItem.WIFI_PS = ReadWIFI_PS ?? "";
            ReadAgingShelfItem.SSID1 = ReadSSID1 ?? "";
            ReadAgingShelfItem.WIFI_PS1 = ReadWIFI_PS1 ?? "";
            ReadAgingShelfItem.Channel = ReadChanel ?? "";
            ReadAgingShelfItem.Mesh_ID = ReadMesh_ID ?? "";
            ReadAgingShelfItem.Mesh_PS = ReadMesh_PS ?? "";
            ReadAgingShelfItem.CustomDisplayName =
                string.IsNullOrWhiteSpace(ReadAgingNumber)
                    ? "读取配置"
                    : $"读取: 老化架 {ReadAgingNumber}";
        }

        private void RefreshComboBoxAgingItems(bool restoreSelection = true)
        {
            var previousSelected = SelectedAgingShelf;
            ComboBoxAgingItems.Clear();

            // 只有已经读取到有效数据后，才在 ComboBox 中显示“读取配置”项，
            // 避免空项被选中后清空基础配置。
            if (ReadAgingShelfItem != null &&
                (!string.IsNullOrWhiteSpace(ReadAgingShelfItem.ShelfNumber) ||
                 !string.IsNullOrWhiteSpace(ReadAgingShelfItem.SSID) ||
                 !string.IsNullOrWhiteSpace(ReadAgingShelfItem.Mesh_ID)))
            {
                ComboBoxAgingItems.Add(ReadAgingShelfItem);

            }

            foreach (var shelf in AgingShelves.Where(s => !s.IsReadConfigItem))
            {
                ComboBoxAgingItems.Add(shelf);
            }

            // 刷新后尽量恢复之前的选中项，避免界面因清空集合而丢失选择。
            // 读取设备时传 restoreSelection: false，让调用方完全决定选中项。
            _suppressShelfToBaseConfig = true;
            if (restoreSelection && previousSelected != null && ComboBoxAgingItems.Contains(previousSelected))
            {
                SelectedAgingShelf = previousSelected;
            }
            else if (!restoreSelection)
            {
                // 由调用方负责设置 SelectedAgingShelf，此处不做处理。
            }
            else if (ComboBoxAgingItems.Count > 0)
            {
                SelectedAgingShelf = ComboBoxAgingItems[0];
            }
            else
            {
                SelectedAgingShelf = null!;
            }
            _suppressShelfToBaseConfig = false;
        }

        /// <summary>
        /// 按老化架号生成 Mesh ID，形如 01:01:01:01:01:01（每段两位、共六段）。
        /// </summary>
        private static string BuildMeshId(int shelfNumber)
        {
            return string.Join(":", Enumerable.Repeat(shelfNumber.ToString("D2"), 6));
        }

        /// <summary>
        /// 从接口返回的老化架名称中提取老化架号，例如「老化架12」→ 12。
        /// 提取不到时返回空字符串，交由用户在界面上补填。
        /// </summary>
        private static string ExtractShelfNumberFromName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "";
            }

            var match = Regex.Match(name, @"\d+");
            if (!match.Success)
            {
                return "";
            }

            return int.TryParse(match.Value, out int number) ? number.ToString() : "";
        }

        /// <summary>
        /// 调用老化架列表接口并同步到本地集合：
        /// 接口新增的老化架自动添加，接口中已不存在的自动删除，已存在的更新名称等字段。
        /// 任何失败场景都不改动本地数据，仅在状态栏/弹窗中提示。
        /// </summary>
        /// <param name="silent">true 表示静默模式（启动自动同步），失败不弹窗。</param>
        private async Task SyncAgingShelvesFromApiAsync(bool silent)
        {
            if (_isSyncingRacks)
            {
                return;
            }

            _isSyncingRacks = true;
            CanSyncRacks = false;
            RackSyncStatus = "正在同步老化架...";

            try
            {
                var racks = await _rackApiClient.GetRacksAsync();

                var result = SyncAgingShelvesCore(racks);

                if (result.skipped)
                {
                    // 接口返回空列表，状态文案已在同步内部给出（本地数据保持不变）。
                    if (!silent)
                    {
                        MessageBox.Show(
                            RackSyncStatus,
                            "同步老化架",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else
                {
                    RackSyncStatus = $"上次同步 {DateTime.Now:HH:mm:ss}，共 {AgingShelves.Count(s => !s.IsReadConfigItem)} 个老化架"
                                     + $"（新增 {result.added}，更新 {result.updated}，删除 {result.removed}）";

                    if (!silent)
                    {
                        MessageBox.Show(
                            $"同步完成：新增 {result.added} 个，更新 {result.updated} 个，删除 {result.removed} 个。",
                            "同步老化架",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                string reason = ex is TimeoutException ? $"请求超时（{ex.Message}）" : ex.Message;
                RackSyncStatus = $"同步失败：{reason}（本地数据未改动）";

                if (!silent)
                {
                    MessageBox.Show(
                        $"同步老化架失败：{reason}\n\n本地已有的老化架配置保持不变。",
                        "同步老化架",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            finally
            {
                _isSyncingRacks = false;
                CanSyncRacks = true;
            }
        }

        /// <summary>
        /// 依据接口返回的老化架列表做差集同步。返回新增/更新/删除的数量。
        /// </summary>
        private (int added, int updated, int removed, bool skipped) SyncAgingShelvesCore(List<RackItem> racks)
        {
            // 按 id 建索引，跳过无 id 的脏数据，重复 id 只保留最后一条。
            var remoteMap = new Dictionary<string, RackItem>(StringComparer.Ordinal);
            foreach (var rack in racks)
            {
                if (rack == null || string.IsNullOrWhiteSpace(rack.Id))
                {
                    continue;
                }

                remoteMap[rack.Id.Trim()] = rack;
            }

            // 接口返回空列表时多半是接口异常或抖动，保守起见不删除本地模板。
            if (remoteMap.Count == 0)
            {
                RackSyncStatus = $"同步完成：接口返回 0 条有效老化架（total={racks.Count}），本地数据未改动。";
                return (0, 0, 0, true);
            }

            int added = 0;
            int updated = 0;

            _suppressAutoSave = true;
            _suppressShelfToBaseConfig = true;
            try
            {
                foreach (var pair in remoteMap)
                {
                    string remoteId = pair.Key;
                    var rack = pair.Value;
                    string displayName = string.IsNullOrWhiteSpace(rack.Name) ? $"老化架 {remoteId}" : rack.Name!.Trim();

                    var existing = AgingShelves.FirstOrDefault(
                        s => !s.IsReadConfigItem && string.Equals(s.RemoteId, remoteId, StringComparison.Ordinal));

                    if (existing == null)
                    {
                        string shelfNumber = ExtractShelfNumberFromName(rack.Name);
                        var shelf = new AgingShelfModel
                        {
                            RemoteId = remoteId,
                            RemoteName = displayName,
                            CustomDisplayName = displayName,
                            Remark = rack.Remark ?? "",
                            MasterControlId = rack.MasterControlId ?? "",
                            ShelfNumber = shelfNumber,
                            Channel = "0",
                            Mesh_PS = "88888888"
                        };

                        if (int.TryParse(shelfNumber, out int number))
                        {
                            shelf.Mesh_ID = BuildMeshId(number);
                        }

                        AttachAutoSave(shelf);
                        AgingShelves.Add(shelf);
                        added++;
                    }
                    else
                    {
                        // 已存在：刷新服务端字段，下拉框显示名始终使用接口返回的 name。
                        existing.RemoteName = displayName;
                        existing.CustomDisplayName = displayName;
                        existing.Remark = rack.Remark ?? "";
                        existing.MasterControlId = rack.MasterControlId ?? "";

                        // 已填写的老化架号 / Mesh ID 属于用户资产，不覆盖。
                        if (string.IsNullOrWhiteSpace(existing.ShelfNumber))
                        {
                            existing.ShelfNumber = ExtractShelfNumberFromName(rack.Name);
                        }

                        if (string.IsNullOrWhiteSpace(existing.Mesh_ID) &&
                            int.TryParse(existing.ShelfNumber, out int existingNumber))
                        {
                            existing.Mesh_ID = BuildMeshId(existingNumber);
                        }

                        updated++;
                    }
                }

                // 删除：本地由接口同步生成、但本次接口已不再返回的老化架。
                // 没有 RemoteId 的历史遗留项无法与接口对应，保留以免误删。
                var toRemove = AgingShelves
                    .Where(s => !s.IsReadConfigItem && !string.IsNullOrWhiteSpace(s.RemoteId) && !remoteMap.ContainsKey(s.RemoteId))
                    .ToList();

                bool selectedRemoved = SelectedAgingShelf != null && toRemove.Contains(SelectedAgingShelf);
                foreach (var shelf in toRemove)
                {
                    shelf.PropertyChanged -= AutoSaveHandler;
                    AgingShelves.Remove(shelf);
                }

                if (selectedRemoved)
                {
                    SelectedAgingShelf = null!;
                }

                RefreshComboBoxAgingItems();
                SaveAgingShelves();

                return (added, updated, toRemove.Count, false);
            }
            finally
            {
                _suppressAutoSave = false;
                _suppressShelfToBaseConfig = false;
            }
        }

        private async Task RunReadBackDelayAsync()
        {
            const int totalSteps = 80;
            const int stepDelayMs = 100;
            for (int i = 0; i < totalSteps; i++)
            {
                await Task.Delay(stepDelayMs);
                WaitProgress = (i + 1) * 100.0 / totalSteps;
            }
        }

        private void LoadAgingShelves()
        {
            try
            {
                string path = GetAgingShelvesPath();
                if (!File.Exists(path))
                {
                    AgingShelves = new ObservableCollection<AgingShelfModel>();
                    return;
                }

                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<ObservableCollection<AgingShelfModel>>(json);
                AgingShelves = loaded ?? new ObservableCollection<AgingShelfModel>();
                // 过滤掉可能是历史遗留的读取项，确保模板集合只包含用户定义的数据。
                var staleItems = AgingShelves.Where(s => s.IsReadConfigItem).ToList();
                foreach (var stale in staleItems)
                {
                    AgingShelves.Remove(stale);
                }
                foreach (var shelf in AgingShelves)
                {
                    shelf.IsReadConfigItem = false;
                    AttachAutoSave(shelf);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载老化架配置失败：{ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                AgingShelves = new ObservableCollection<AgingShelfModel>();
            }
            finally
            {
                RefreshComboBoxAgingItems();
            }
        }

        private void SaveAgingShelves()
        {
            try
            {
                string path = GetAgingShelvesPath();
                var shelvesToSave = AgingShelves.Where(s => !s.IsReadConfigItem).ToList();
                string json = JsonSerializer.Serialize(shelvesToSave);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存老化架配置失败：{ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AttachAutoSave(AgingShelfModel shelf)
        {
            shelf.PropertyChanged += AutoSaveHandler;
        }

        private void AutoSaveHandler(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressAutoSave)
            {
                return;
            }

            SaveAgingShelves();
        }

        private string GetAgingShelvesPath()
        {
            return Path.Combine(AppContext.BaseDirectory, "AgingShelves.json");
        }

        #endregion

        #region 点位选择与设备分配

        /// <summary>
        /// 依据当前选中的老化架重新加载点位。未选中老化架或老化架非接口同步项时清空点位。
        /// 页码在请求体中固定为首页，因此每次刷新都等价于回到第一页。
        /// </summary>
        private async Task ReloadPointsForSelectedShelfAsync()
        {
            UpdateCanAssignDevice();

            string? rackId = GetSelectedRemoteRackId();
            if (string.IsNullOrWhiteSpace(rackId))
            {
                _pointsRequestSeq++;
                ClearAgingPoints("请选择由接口同步生成的老化架后再选择点位");
                return;
            }

            await LoadAvailablePointsAsync(rackId, OnlyAvailable);
        }

        /// <summary>
        /// 「仅显示可用节点」开关变化时立即重新查询点位（回到首页）。
        /// </summary>
        private void OnOnlyAvailableChanged()
        {
            _ = ReloadPointsForSelectedShelfAsync();
        }

        /// <summary>
        /// 当前选中老化架对应的服务端 id；「读取配置」项等本地项返回 null。
        /// </summary>
        private string? GetSelectedRemoteRackId()
        {
            if (SelectedAgingShelf == null || SelectedAgingShelf.IsReadConfigItem)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(SelectedAgingShelf.RemoteId) ? null : SelectedAgingShelf.RemoteId.Trim();
        }

        /// <summary>
        /// 调用点位列表接口渲染点位下拉框。失败时清空并保持禁用。
        /// </summary>
        /// <summary>
        /// 调用点位列表接口渲染点位下拉框。失败时清空并保持禁用。
        /// </summary>
        /// <param name="rackId">老化架 id。</param>
        /// <param name="onlyAvailable">是否只查询可用节点，默认 true。</param>
        private async Task LoadAvailablePointsAsync(string rackId, bool onlyAvailable = true)
        {
            int seq = ++_pointsRequestSeq;

            IsLoadingPoints = true;
            PointsEnabled = false;
            SelectedAgingPoint = null!;
            AgingPoints.Clear();
            PointAssignStatus = "正在加载点位...";

            try
            {
                if (!long.TryParse(rackId, out long agingRackId))
                {
                    throw new RackApiException($"老化架 ID“{rackId}”不是有效数字。");
                }

                var points = await _rackApiClient.GetAvailablePointsAsync(agingRackId, onlyAvailable);

                // 已被更新的请求取代，丢弃本次结果。
                if (seq != _pointsRequestSeq)
                {
                    return;
                }

                AgingPoints.Clear();
                foreach (var point in points.OrderBy(p => p.LayerNumber).ThenBy(p => p.PointSequence))
                {
                    AgingPoints.Add(point);
                }

                if (AgingPoints.Count > 0)
                {
                    PointsEnabled = true;
                    PointAssignStatus = onlyAvailable
                        ? $"已加载 {AgingPoints.Count} 个可用点位"
                        : $"已加载 {AgingPoints.Count} 个节点（含已绑定）";
                }
                else
                {
                    PointAssignStatus = onlyAvailable ? "该老化架暂无可用点位" : "该老化架暂无节点";
                }
            }
            catch (Exception ex)
            {
                if (seq != _pointsRequestSeq)
                {
                    return;
                }

                string reason = ex is TimeoutException ? $"请求超时（{ex.Message}）" : ex.Message;
                ClearAgingPoints($"点位加载失败：{reason}");
            }
            finally
            {
                if (seq == _pointsRequestSeq)
                {
                    IsLoadingPoints = false;
                }

                UpdateCanAssignDevice();
            }
        }

        /// <summary>
        /// 清空点位下拉框并禁用。
        /// </summary>
        private void ClearAgingPoints(string status)
        {
            AgingPoints.Clear();
            SelectedAgingPoint = null!;
            PointsEnabled = false;
            PointAssignStatus = status;
            UpdateCanAssignDevice();
        }

        /// <summary>
        /// 「写入」的完整编排：写设备 → 等待回读 → 自动校验 → 校验一致才绑定点位。
        /// </summary>
        private async Task<WriteOutcome> WriteVerifyAndBindAsync()
        {
            ReadUDPPort = "1";

            if (!TryValidateConfig(out var validationErrors))
            {
                ReadStatusMessage = "写入未开始：" + string.Join("；", validationErrors);
                PointAssignStatus = "配置不完整，未写入、未绑定点位";
                return WriteOutcome.Error("写入失败", "配置不完整，未向设备写入任何数据。", string.Join("\n", validationErrors));
            }

            // 绑定参数必须在写入前快照：回读后的 UI 回显会改写 SelectedAgingShelf / SelectedAgingPoint。
            BindContext? bindContext = CaptureBindContext();

            WroteSnapshot = MakeWroteSnapshot();
            var baseConfig = new ChangeConfigValue
            {
                IsRoot = GetSnapshotValue("IsRoot"),
                SSID = GetSnapshotValue("SSID"),
                WIFI_PS = GetSnapshotValue("WIFI_PS"),
                SSID1 = GetSnapshotValue("SSID1"),
                WIFI_PS1 = GetSnapshotValue("WIFI_PS1"),
                Chanel = GetSnapshotValue("Chanel"),
                Mesh_ID = GetSnapshotValue("Mesh_ID"),
                Mesh_PS = GetSnapshotValue("Mesh_PS"),
                DEVICE_ID = GetSnapshotValue("DEVICE_ID"),
                UDP_Port = GetSnapshotValue("UDP_Port"),
                SERVER_IP = GetSnapshotValue("SERVER_IP"),
                SERVER_UDP_Port = GetSnapshotValue("SERVER_UDP_Port"),
                AgingNumber = GetSnapshotValue("AgingNumber")
            };

            var externalDevices = ExternaldeviceList.Select(d => new
            {
                d.Name,
                d.ProtoID,
                d.Communication,
                d.Proto,
                d.AgingSteps
            }).ToList();

            var config = new
            {
                Cmd = "ChangeConfig",
                Value = baseConfig,
                ExternalDevices = externalDevices,
            };

            string json = JsonSerializer.Serialize(config);
            byte[] message = Encoding.UTF8.GetBytes(json);

            // 一次确认同时覆盖「写设备」与「写服务端绑定」两个副作用。
            string mes = string.Join("\n", WroteSnapshot.Select(kv => $"{kv.Key}: {kv.Value}"));
            mes += "\n\n" + DescribeBindPlan(bindContext);
            if (MessageBox.Show(mes + "\n\n确定要写入吗？", "提示", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                // 用户主动取消，不算失败，静默结束即可。
                return WriteOutcome.NoDialog();
            }

            try
            {
                serialModel.Send(message);
            }
            catch (Exception ex)
            {
                ReadStatusMessage = $"写入失败：{ex.Message}";
                PointAssignStatus = "写入未成功，未绑定点位";
                return WriteOutcome.Error("写入失败", "写入命令未能发送到串口，设备未收到配置。", ex.Message);
            }

            // 显示等待回读遮罩
            IsReadBackWaiting = true;
            WaitProgress = 0;
            WaitMessage = "已发送写入命令，正在等待设备处理...";

            // 等待动画，同时执行回读比对
            await RunReadBackDelayAsync();

            WaitMessage = "正在回读设备配置...";
            try
            {
                await Task.Run(DoReadConfig);
            }
            catch (Exception ex)
            {
                ReadStatusMessage = $"写入失败：设备未返回配置（{ex.Message}）";
                PointAssignStatus = "写入结果未知，未绑定点位";
                return WriteOutcome.Error(
                    "写入失败",
                    "设备未在规定时间内返回配置，写入结果未知。",
                    ex.Message + "\n请检查串口连接与设备供电后重试。");
            }

            ApplyReadBackShelfSelection();

            if (!CheckWriteOK(WroteSnapshot, out var diffs))
            {
                string diffText = string.Join("；", diffs);
                ReadMatchStatus = "写入并回读不一致";
                ReadStatusMessage = "回读比对不一致：" + diffText + "（未调用绑定接口）";
                PointAssignStatus = "回读不一致，未绑定点位";
                return WriteOutcome.Error(
                    "写入失败",
                    "已发送写入命令，但设备回读值与写入值不一致。",
                    diffText + "\n未调用点位绑定接口。");
            }

            ReadMatchStatus = "写入并回读一致";

            if (bindContext == null)
            {
                ReadStatusMessage = "回读比对一致，写入成功。（未选择接口同步的老化架或点位，已跳过点位绑定）";
                PointAssignStatus = "未选择老化架或点位，跳过绑定";
                return WriteOutcome.Warning(
                    "写入成功（未绑定点位）",
                    "配置已写入并通过回读校验；未选择接口同步的老化架或点位，已跳过点位绑定。",
                    "如需绑定，请在基础配置上方选择一个由接口同步生成的老化架及其点位后再次写入。");
            }

            ReadStatusMessage = "回读比对一致，写入成功，正在绑定老化架点位...";
            var (bound, bindMessage) = await BindPointAsync(bindContext);
            if (!bound)
            {
                // 写入已成功，只是服务端绑定失败：不算“写入+绑定都成功”，因此不允许关闭串口。
                return WriteOutcome.Warning(
                    "写入成功，点位绑定失败",
                    "配置已写入设备并通过回读校验，但点位绑定失败。",
                    bindMessage + "\n可重新选择点位后再次写入。");
            }

            string roleText = bindContext.DeviceType == 2 ? "根节点" : "非根节点";
            return WriteOutcome.OkAndCloseSerial(
                "写入成功",
                $"配置写入成功，已绑定 {bindContext.RackName} → {bindContext.PointName}。",
                $"节点 ID：{bindContext.DeviceId}（{roleText}）\n老化架：{bindContext.RackName}\n点位：{bindContext.PointName}\n可用点位列表已刷新。");
        }

        /// <summary>
        /// 回读后决定老化架下拉框的选中项：用户已明确选中某个接口同步的老化架时保留其选择，
        /// 避免回读把下拉框切到「读取配置」项并连带清空已选点位；否则沿用原来的自动匹配行为。
        /// </summary>
        private void ApplyReadBackShelfSelection()
        {
            UpdateReadAgingShelfItemFromRead();

            bool keepSelection = SelectedAgingShelf != null
                                 && !SelectedAgingShelf.IsReadConfigItem
                                 && !string.IsNullOrWhiteSpace(SelectedAgingShelf.RemoteId);

            if (keepSelection)
            {
                return;
            }

            TryMatchAgingShelfFromRead();
        }

        /// <summary>
        /// 点位绑定的输入快照。回读与 UI 回显会改写 SelectedAgingShelf / SelectedAgingPoint，
        /// 因此绑定接口只能依赖写入前捕获的这份数据。
        /// </summary>
        private sealed class BindContext
        {
            public long DeviceId { get; set; }
            public int DeviceType { get; set; }
            public long AgingRackId { get; set; }
            public long PointId { get; set; }
            public string RackName { get; set; } = "";
            public string PointName { get; set; } = "";
        }

        /// <summary>
        /// 写入前捕获绑定所需的节点 ID / 老化架 ID / 点位 ID。缺任一项或格式不合法时返回 null。
        /// 必须在 serialModel.Send 之前调用，否则拿到的是被回读流程改写后的值。
        /// </summary>
        private BindContext? CaptureBindContext()
        {
            string? rackId = GetSelectedRemoteRackId();
            if (string.IsNullOrWhiteSpace(rackId)
                || SelectedAgingPoint == null
                || string.IsNullOrWhiteSpace(SelectedAgingPoint.Id)
                || string.IsNullOrWhiteSpace(ReadUDP_ID))
            {
                return null;
            }

            if (!long.TryParse(rackId, out long agingRackId)
                || !long.TryParse(SelectedAgingPoint.Id.Trim(), out long pointId)
                || !long.TryParse(ReadUDP_ID.Trim(), out long deviceId))
            {
                return null;
            }

            return new BindContext
            {
                DeviceId = deviceId,
                // 根节点 deviceType=2，非根节点 deviceType=1。
                DeviceType = ReadIsRoot == "1" ? 2 : 1,
                AgingRackId = agingRackId,
                PointId = pointId,
                RackName = SelectedAgingShelf?.DisplayName ?? rackId,
                PointName = SelectedAgingPoint.DisplayName
            };
        }

        /// <summary>
        /// 生成写入确认框里的绑定说明，让一次确认覆盖「写设备」与「写服务端绑定」两个副作用。
        /// </summary>
        private static string DescribeBindPlan(BindContext? context)
        {
            if (context == null)
            {
                return "写入成功后不会绑定点位（当前未选择接口同步的老化架或点位）。";
            }

            string roleText = context.DeviceType == 2 ? "根节点" : "非根节点";
            return $"写入成功且回读一致后，将绑定点位：{context.RackName} → {context.PointName}（节点 ID {context.DeviceId}，{roleText}）。";
        }

        /// <summary>
        /// 把设备分配到指定点位。只在「写入 → 自动回读 → 校验一致」之后被调用（WriteVerifyAndBindAsync）。
        /// 成功后重新拉取该老化架的可用点位，因为已分配的点位不再可用。
        /// </summary>
        private async Task<(bool success, string message)> BindPointAsync(BindContext context)
        {
            if (IsAssigning)
            {
                return (false, "上一次点位绑定尚未结束。");
            }

            IsAssigning = true;
            CanAssignDevice = false;
            IsReadBackWaiting = true;
            WaitMessage = "正在绑定老化架点位，请勿操作...";

            try
            {
                var (success, message) = await _rackApiClient.AssignDeviceAsync(
                    context.DeviceId, context.DeviceType, context.AgingRackId, context.PointId);

                if (!success)
                {
                    // 绑定失败不回滚、不清空已写入的配置，让用户改点位后重试。
                    ReadStatusMessage = $"写入成功且回读一致，但点位绑定失败：{message}";
                    PointAssignStatus = "点位绑定失败，可重新选择点位后再次写入";
                    return (false, $"服务端返回：{message}");
                }

                ReadStatusMessage = $"写入成功，已绑定到 {context.RackName} → {context.PointName}。";
                PointAssignStatus = "点位绑定成功";

                WaitMessage = "正在刷新可用点位...";
                // 绑定成功后沿用当前「仅显示可用节点」开关，并从首页重新拉取。
                await LoadAvailablePointsAsync(context.AgingRackId.ToString(), OnlyAvailable);

                return (true, $"已绑定 {context.RackName} → {context.PointName}");
            }
            catch (Exception ex)
            {
                string reason = ex is TimeoutException ? $"请求超时（{ex.Message}）" : ex.Message;
                ReadStatusMessage = $"写入成功且回读一致，但点位绑定失败：{reason}";
                PointAssignStatus = "点位绑定失败";
                return (false, reason);
            }
            finally
            {
                IsAssigning = false;
                UpdateCanAssignDevice();
            }
        }

        /// <summary>
        /// 汇总计算点位绑定所需的条件是否具备（绑定只是写入链路的可选后置步骤）。
        /// </summary>
        private void UpdateCanAssignDevice()
        {
            CanAssignDevice = !IsAssigning
                              && serialModel.IsOpen
                              && GetSelectedRemoteRackId() != null
                              && SelectedAgingPoint != null
                              && !string.IsNullOrWhiteSpace(SelectedAgingPoint.Id)
                              && !string.IsNullOrWhiteSpace(ReadUDP_ID);
        }

        private void OnSelectedAgingPointChanged()
        {
            UpdateCanAssignDevice();
        }

        private void OnReadUDP_IDChanged()
        {
            UpdateCanAssignDevice();
        }

        private void OnButtonIsEnabledChanged()
        {
            UpdateCanAssignDevice();
        }

        #endregion

        #region 串口相关配置
        public string[] PortNameList { get; set; }
        public string SelectedComPort { get; set; }
        private void RefreshAvailablePorts()//刷新可用端口列表
        {
            PortNameList = serialModel.GetPortNames();
            if (!string.IsNullOrEmpty(SelectedComPort))
            {
                bool flag = false;
                string[] portNameList = PortNameList;
                for (int i = 0; i < portNameList.Length; i++)
                {
                    if (portNameList[i].ToLower() == SelectedComPort.ToLower())
                    {
                        flag = true;
                        break;
                    }
                }
                if (!flag && PortNameList != null && PortNameList.Length != 0)
                {
                    SelectedComPort = PortNameList[0];
                }
            }

        }
        public int[] BaudRateList { get; } = new int[] { 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };
        public int SelectedBaudRate { get; set; } = 115200;
        public int[] DataBits { get; } = { 5, 6, 7, 8 };
        public int SelectedDataBits { get; set; } = 8;
        public string[] StopBits { get; } = { "0", "1", "1.5", "2" };
        public string SelectedStopBits { get; set; } = "1";
        private bool OpenSerial()
        {
            try
            {
                if (string.IsNullOrEmpty(SelectedComPort))
                {
                    MessageBox.Show("请选择串口", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                serialModel.OpenSerial(SelectedComPort, SelectedBaudRate, SelectedDataBits, SelectedStopBits);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            return true;

        }
        private bool CloseSerial()
        {
            return serialModel.CloseSerial();
        }

        /// <summary>
        /// 串口状态的唯一出口：同步「打开/关闭串口」按钮外观、读写类按钮可用性与端口下拉可用性。
        /// 手动开闭串口与写入成功后自动关闭串口都必须走这里，避免两条路径表现不一致。
        /// </summary>
        private void ApplySerialUiState(bool isOpen)
        {
            SerialButtonContent = isOpen ? "关闭串口" : "打开串口";
            SerialButtonBackground = isOpen ? SerialOpenBrush : SerialClosedBrush;
            ButtonIsEnabled = isOpen;
            ComboBoxEB = !isOpen;
            UpdateCanAssignDevice();
        }

        /// <summary>
        /// 展示写入结果弹窗。返回用户是否点击了主按钮（标题栏关闭视为未确认）。
        /// </summary>
        private bool ShowWriteResultDialog(WriteOutcome outcome)
        {
            var dialog = new WriteResultDialog(outcome)
            {
                Owner = Application.Current?.MainWindow
            };

            return dialog.ShowDialog() == true;
        }

        /// <summary>
        /// 写入成功且绑定成功后，由用户在结果窗点击确认触发的自动关闭串口。
        /// 串口已断开时幂等返回；关闭失败时保留"已打开"的界面状态，不假装成功。
        /// </summary>
        private void CloseSerialAfterWrite()
        {
            try
            {
                if (serialModel.IsOpen)
                {
                    serialModel.CloseSerial();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"串口关闭失败：{ex.Message}", "串口", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            ApplySerialUiState(serialModel.IsOpen);
        }
        #endregion

        #region Json
        //Json数据发送
        public class ChangeConfigValue
        {
            public string IsRoot { get; set; }
            public string SSID { get; set; }
            public string WIFI_PS { get; set; }
            public string SSID1 { get; set; }
            public string WIFI_PS1 { get; set; }
            public string Chanel { get; set; }
            public string Mesh_ID { get; set; }
            public string Mesh_PS { get; set; }
            public string DEVICE_ID { get; set; }
            public string UDP_Port { get; set; }
            public string SERVER_IP { get; set; }
            public string SERVER_UDP_Port { get; set; }
            public string AgingNumber { get; set; }


        }
        public class ReadConfigResponse
        {
            public string Cmd { get; set; }
            public ChangeConfigValue Value { get; set; }
            public ObservableCollection<ReadExternalDeviceModel> ExternalDevices { get; set; } = new();
        }

        public ObservableCollection<ReadExternalDeviceModel> ReadExternalDevices { get; set; } = new();
        public string LastReadTime { get; set; } = "尚未读取";
        public string ReadStatusMessage { get; set; } = "等待读取设备配置";
        public bool IsReadConfigMatched { get; set; }
        public string ReadMatchStatus { get; set; } = "尚未比较";
        public string ReadIsRoot { get; set; } = "";
        public string ReadSSID { get; set; } = "";
        public string ReadWIFI_PS { get; set; } = "";
        public string ReadSSID1 { get; set; } = "";
        public string ReadWIFI_PS1 { get; set; } = "";
        public string ReadChanel { get; set; } = "";
        public string ReadMesh_ID { get; set; } = "";
        public string ReadMesh_PS { get; set; } = "";
        public string ReadUDP_ID { get; set; } = "";
        public string ReadUDPPort { get; set; } = "1";
        public string ReadSERVER_IP { get; set; } = "";
        public string ReadSERVER_UDP_Port { get; set; } = "";
        public string ReadAgingNumber { get; set; } = "1";

        private void JsonParse(string inputjson)
        {
            if (string.IsNullOrWhiteSpace(inputjson))
            {
                throw new InvalidOperationException("ESP32 返回内容为空");
            }

            ReadConfigResponse response;
            try
            {
                response = JsonSerializer.Deserialize<ReadConfigResponse>(inputjson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"ESP32 返回的 JSON 无效：{ex.Message}", ex);
            }

            if (response == null)
                throw new InvalidOperationException("ESP32 返回数据无法解析");
            if (!string.Equals(response.Cmd, "ReadConfig", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"响应命令不匹配：{response.Cmd ?? "空"}");
            if (response.Value == null)
                throw new InvalidOperationException("响应中缺少 Value 基础配置");

            var normalizedDevices = NormalizeReadDevices(response.ExternalDevices);
            ChangeConfigValue value = response.Value;

            // 所有校验完成后再一次性替换界面数据，失败时保留上次成功结果。
            ReadIsRoot = value.IsRoot ?? "";
            ReadSSID = value.SSID ?? "";
            ReadWIFI_PS = value.WIFI_PS ?? "";
            ReadSSID1 = value.SSID1 ?? "";
            ReadWIFI_PS1 = value.WIFI_PS1 ?? "";
            ReadChanel = value.Chanel ?? "";
            ReadMesh_ID = value.Mesh_ID ?? "";
            ReadMesh_PS = value.Mesh_PS ?? "";
            ReadUDP_ID = value.DEVICE_ID ?? "";
            ReadUDPPort = value.UDP_Port ?? "";
            ReadSERVER_IP = value.SERVER_IP ?? "";
            ReadSERVER_UDP_Port = value.SERVER_UDP_Port ?? "";
            ReadAgingNumber = value.AgingNumber ?? "";
            ReadExternalDevices = normalizedDevices;
            LastReadTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            ReadStatusMessage = $"读取成功，共识别 {ReadExternalDevices.Count} 个外接设备";
        }

        private static ObservableCollection<ReadExternalDeviceModel> NormalizeReadDevices(
            ObservableCollection<ReadExternalDeviceModel> devices)
        {
            var result = devices ?? new ObservableCollection<ReadExternalDeviceModel>();
            foreach (var device in result)
            {
                device.Name ??= "";
                device.Communication ??= "";
                device.Proto ??= "";
                device.AgingSteps ??= new ReadAgingSteps();
                device.AgingSteps.AgingFunctionList ??= new ObservableCollection<ReadAgingFunction>();

                foreach (var function in device.AgingSteps.AgingFunctionList)
                {
                    function.Method ??= "";
                    function.SampleData ??= new ObservableCollection<SampleData>();
                    function.Action ??= new ReadAction();
                    function.Action.PreAction ??= new ObservableCollection<PreAction>();
                    function.Action.AfterAction ??= new ObservableCollection<AfterAction>();

                    foreach (var sample in function.SampleData)
                        sample.Value ??= "";
                    foreach (var action in function.Action.PreAction)
                    {
                        action.ParaName ??= "";
                        action.ParaValue ??= "";
                    }
                    foreach (var action in function.Action.AfterAction)
                    {
                        action.ParaName ??= "";
                        action.ParaValue ??= "";
                    }
                }
            }
            return result;
        }
        #endregion

    }
}
