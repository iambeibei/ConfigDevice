# LightGateway 项目梳理与三项改造计划

> 输出目标：仅做现状分析 + 改动计划（改动点 / 实现要点 / 注意事项 / 验收口径），不含具体代码实现。
> 编制日期：2026-09-22
> 代码基线：仓库当前工作副本（`ViewModel/MainViewModel.cs` 1485 行、`View/MainWindow.xaml` 1241 行）

---

## 一、技术栈识别

| 层面 | 结论 |
| --- | --- |
| 运行时 / 框架 | .NET 10 WPF（`net10.0-windows10.0.17763.0`），`OutputType=WinExe`，`Nullable=enable`，`ImplicitUsings=enable` |
| 语言 | C#（无预览特性依赖） |
| 第三方包 | Fody 6.9.3 + PropertyChanged.Fody 4.1.0（编译期织入 INotifyPropertyChanged）、System.IO.Ports 10.0.7、System.Text.Json 10.0.7 |
| 架构模式 | 手写 MVVM：**无 DI 容器、无 Prism/MVVM Light/CommunityToolkit、无日志框架、无单元测试、无 CI** |
| 数据绑定 | 大量依赖 PropertyChanged.Fody 的自动通知；另有 `Command/Base.cs` 里手工的 `BaseNotify`（基本已被 Fody 取代） |
| 设备通信 | 串口 JSON 协议（UTF-8 文本帧），`SerialModel` 基于 `System.IO.Ports.SerialPort`；仍保留 Modbus/CRC16 遗留工具代码（不参与 JSON 流程） |
| 网络接口 | 内网 HTTPS 老化架服务 `https://192.168.40.116:7088/DeviceServiceCallback/*`（自签名证书，白名单放行）；HTTP 协议 JSON 服务 `http://10.16.160.51:5000/Login/GetJson` |
| 持久化现状 | **只有一份** `AgingShelves.json`，写在 `AppContext.BaseDirectory`（即 exe 输出目录） |
| 已知构建噪音 | `NU1510`（显式引用 System.Text.Json）、大量既有 nullable 警告；`dotnet build` 应维持 0 错误 |

---

## 二、目录结构与模块职责

```
LightGateway/
├─ App.xaml / App.xaml.cs      仅 StartupUri=View/MainWindow.xaml；无 OnExit、无异常处理、无全局状态
├─ Command/Base.cs             BaseNotify（手工 INPC）+ BaseCommand<T>（Param-less CanExecute 走 CommandManager.RequerySuggested）
├─ Model/
│  ├─ SerialModel.cs           串口生命周期（Open/Close/Send/ReadOneFrameData/GetPortNames）+ 遗留 CRC/Modbus 线程
│  ├─ AgingShelfModel.cs       老化架模板；手工 INPC，含 RemoteId/RemoteName/ShelfNumber/SSID/Mesh 等
│  ├─ ExDeviceModel.cs         可编辑外接设备模型 + 回读模型（ReadExternalDeviceModel 系列，与可编辑侧隔离）
│  └─ ProtocolParserModule.cs  协议 JSON → 命令列表解析
├─ Ultils/
│  ├─ RackApiClient.cs         三个老化架接口客户端 + RackPointItem/RackItem + RackApiException + 证书白名单
│  ├─ WebApiClientModule.cs    协议 JSON 拉取（ApiResponse<T> 语义为 success 布尔）
│  ├─ RootStatusToBrushConverter.cs  「是否根节点」字符串 → 醒目颜色（仅单向转换）
│  ├─ WindowActivationHelper.cs      HwndSource.AddHook + 附加属性，判定鼠标是否在窗口内
│  └─ Util.cs                  字节/大小端/CRC 工具
├─ View/MainWindow.xaml(.cs)   单窗口 3 个 Tab；cs 里几乎空（只有一个空 SelectionChanged 处理器）
├─ ViewModel/MainViewModel.cs  唯一 ViewModel，命令/状态/解析/持久化全在里面（约 1485 行）
└─ bin/.../AgingShelves.json   运行期数据文件（另有已废弃的 NodeHistory.json 残留在输出目录）
```

**八个核心模块（按调用热度）**

1. **命令派发**：所有按钮共用 `CmdClick`，`CmdCmdClickAction` 对 `Button.Content.ToString()` 做 switch（读取 / 写入 / 写入并重新读取 / 刷新 / 打开串口 / 关闭串口 / 获取协议 / 同步老化架 / 删除老化架 / 读取并匹配老化架 / 各 PreAction·AfAction·读取参数·设备增删）。
2. **串口**：`SerialModel.OpenSerial/CloseSerial/Send/ReadOneFrameData`；`GetPortNames()` 直接转调 `SerialPort.GetPortNames()`。
3. **配置读写**：`DoReadConfig()`（发 `{"Cmd":"ReadConfig"}` → 读一帧 → `JsonParse`）写入 `Read*` 系列属性；写入侧组装 `{"Cmd":"ChangeConfig","Value":{...},"ExternalDevices":[...]}`。
4. **写后回读比对**：`WroteSnapshot` / `GetReadSnapshot` / `CheckWriteOK`。
5. **老化架同步**：`SyncAgingShelvesFromApiAsync` → `SyncAgingShelvesCore`（按 RemoteId 差集），`AttachAutoSave → AutoSaveHandler` 即时落盘。
6. **点位与分配**：`AvailablePointList` / `AssignDevice`，`_pointsRequestSeq` 丢弃过期响应。
7. **协议拉取**：`GetProto` + `ProtocolCommandParser.Parse`（当前 tab 已隐藏）。
8. **UI 状态机**：`IsReadBackWaiting` 全屏遮罩 + `WaitProgress/WaitMessage`；`ButtonIsEnabled`/`ComboBoxEB`/`CanAssignDevice`/`PointsEnabled` 互锁。

**三个 Tab 的真实形态**：① 设备读取结果（主工作区）② 外接设备设置（`Visibility="Collapsed"`，当前不可见）③ 老化架设置。底部还有一个 `Visibility="Collapsed"` 的按钮组（测试 / 写入 / 读取 三枚旧按钮），仍参与 switch 派发。

---

## 三、需求一：两个写入按钮合并（写入 = 写设备 → 自动回读校验 → 校验通过再调绑定 API）

> 口径调整（2026-09-22 二次修订）：**只合并两个写入按钮**，不触碰 `读取` / `读取并匹配老化架`。
> 绑定 API 的位置按用户要求放在**写入之后、自动回读校验成功之后**再调用。

### 3.1 现状分析（逐行核对 `MainViewModel.cs` 181–289、1142–1215 后的结论）

**重要：链路其实已经接了一半。** 当前 `写入`（line 181）的内部顺序是：

```
ReadUDPPort="1" → CheckConfigOnRead() → MakeWroteSnapshot() → 组装 ChangeConfig
→ Yes/No 确认 → Send → IsReadBackWaiting 遮罩 → RunReadBackDelayAsync()
→ DoReadConfig() → TryMatchAgingShelfFromRead()
→ CheckWriteOK(...)：一致 → await AssignDeviceAndRereadAsync()   ← 已经在写后自动绑了
                    不一致 → 只提示 diff，不调 API
```

而 `写入并重新读取`（line 287）单独存在的唯一价值，是**手动补跑一次绑定**（写入时没选老化架/点位，或上次绑定失败要重试）。它没有独立的功能诉求，所以可以直接合掉。

两者的事实对照：

| | `写入` | `写入并重新读取` |
| --- | --- | --- |
| 写设备（ChangeConfig） | 是 | **否（名不副实）** |
| 回读 + CheckWriteOK 校验 | 是 | 否（只在成功后再读一次） |
| 调 AssignDevice 绑定服务端 | **是（校验通过后自动调）** | 是 |
| 可用条件 | `ButtonIsEnabled`（串口已开即可） | `CanAssignDevice`（架+点位+串口齐备） |
| 定位 | 主力链路 | 补绑 / 重试 |

**两个必须写进计划的真实缺陷（这是合并的技术动因，不只是 UI 收敛）：**

1. **绑定上下文在回读过程中被自己清空**。`写入` 里第 6 步 `TryMatchAgingShelfFromRead()`（line 604）会重建下拉项并把 `SelectedAgingShelf` 改写成"本地匹配到的模板 / `ReadAgingShelfItem` 伪项"；且 `SelectedAgingShelf` 变化会触发 `OnSelectedAgingShelfChanged` → 重拉点位 → `LoadAvailablePointsAsync` 里 `SelectedAgingPoint = null!`。
   结果：紧随其后被调用的 `AssignDeviceAndRereadAsync()` 里，`GetSelectedRemoteRackId()` 拿到的是**回读之后**的选择（伪项 → `RemoteId` 为空 → null），`SelectedAgingPoint` 也可能已被清空。于是用户明明选好了老化架和点位，却弹出"请先选择一个由接口同步生成的老化架"或"请先选择要点位的点位"。**这是当前自动绑定基本走不通的根本原因。**
2. **`TryResolveAssignIds` 在自动流程里会打断用户**。缺任一项就 `MessageBox.Show(..., Information)` 并返回，作为"自动后置步骤"应该降级为状态文本，而不是弹一个打扰式的框。

其余现状：`AssignDeviceAndRereadAsync` 成功后还会再 `DoReadConfig()` + `SelectReadShelfItem()` 一遍（相对前一步的校验属于冗余重读，且会再次强制切下拉框）；`DoReadConfig()` 是**同步阻塞**的（首字节超时 2000ms + 帧尾静默等待），整条链路会卡住 UI 若干秒。

### 3.2 目标链路

单一按钮「写入」（建议保持 Content 就叫 **写入**，语义最准）：

```
写入 → 等待 → 自动回读 → 校验
                          ├─ 不一致 → 中止：提示差异，绝不调 API
                          └─ 一致   → 确认 → AssignDevice 绑定 → 成功后重拉点位
```

### 3.3 改动点（文件级）

1. `View/MainWindow.xaml`（line 351–359）
   - **删除"写入并重新读取"按钮**及其 ToolTip 绑定；保留"写入"（line 344–350，`IsEnabled` 已是 `ButtonIsEnabled`，不变）。
   - 更新操作流程提示区文案（line 370–379）：去掉"最后点击写入并重新读取"，改为"→ 点击「写入」，工具会先写设备、自动回读校验，校验一致后自动绑定所选老化架点位"。
   - 底部那个 `Visibility="Collapsed"` 的旧按钮组里还有一枚 Content="写入"（line 1187–1197），它与主区的 case 同名共享逻辑，删除新按钮时别误删它，否则 Hidden-Old UI 改动会产生困惑（建议一并清理，属于同一处清理）。
2. `ViewModel/MainViewModel.cs`
   - 删除 `case "写入并重新读取"`（line 287–289）。
   - 把 `case "写入"`（line 181–285）重构为一个编排方法（建议 `private async Task WriteVerifyAndBindAsync()`，`async Task` 而非 `async void`），对外仍是 case 里一行 `await`。
   - **新增绑定上下文快照**：在 `Send` 之前把 `SelectedAgingShelf.RemoteId`、`SelectedAgingPoint.Id`、`ReadUDP_ID`、`ReadIsRoot` 取出来存成一个小结构（如 `BindContext`），后续所有 API 参数只从快照取，不再回头读 `SelectedAgingShelf`。这是修掉 3.1 里缺陷 1 的关键。
   - `TryResolveAssignIds` 增加重载 `TryResolveAssignIds(BindContext ctx, out ...)`：接受显式传入的三 id，不再依赖当前 UI 选择。
   - **回读后的 UI 回显降级**：`写入` 链路里的 `TryMatchAgingShelfFromRead()` 改为可选/延后——写入成功后若用户本来就有明确的老化架选择，**不再强制把下拉框切走**；只更新 `ReadAgingShelfItem` 内部数据用于展示。这样"用户选好的点位被清空"的问题也一并消失。（若业务上确实需要带动 `"读取配置项 → 再次点选才套用模板"` 的行为，需确认后再保留。）
   - **去掉 Success-but-redundant 的重读**：`AssignDeviceAndRereadAsync` 拆成「纯绑定」版本 `BindPointAsync(ctx)` 与保留版本；合并后的写入链路只调纯绑定版本，成功后做「重拉点位 + 状态更新」，不再重复 `DoReadConfig()` + `SelectReadShelfItem()`。
   - 失败原因若要给用户看，`TryResolveAssignIds` 失败时从 `MessageBox` 降级为 `ReadStatusMessage` / `PointAssignStatus`（写完设备之后弹一个"请选择老化架"的框，对用户的观感很糟）。
3. 状态与锁
   - 遮罩沿用 `IsReadBackWaiting / WaitProgress / WaitMessage`，阶段文案分段：「正在写入设备…」→「正在回读校验…」→「校验一致，正在绑定老化架点位…」→「刷新可用点位…」。
   - `IsWriting` 与 `IsAssigning` 双锁保留（现在是嵌套调用，外层 finally 在内层之后执行，顺序正确，别改）。
   - 全部结束仍然走外层 `finally` 复位 `ButtonIsEnabled` / `ComboBoxEB`。

### 3.4 实现要点

- **快照先行，UI 回显在后**：`AssignDevice` 需要的三件套必须在 `DoReadConfig()` 之前就取好。`SelectReadShelfItem()` / `TryMatchAgingShelfFromRead()` 都会把下拉框切到 `ReadAgingShelfItem`（`IsReadConfigItem=true`、`RemoteId` 为空，`GetSelectedRemoteRackId()` 直接返回 null）。只要顺序搞反，自动绑定 100% 失败。
- **写服务端的确认不能省**：现有"确定要写入吗"弹窗已经列出了将要写入的字段（`WroteSnapshot` 拼串）；建议在此基础上追加一行绑定目标（老化架名 / 点位名 / 根节点与否），让一次确认覆盖两个副作用。或者更保守：写在写入确认框里保持不变，另在"校验通过 → 调 AssignDevice 之前"再确认一次。**二选一，不要两处都弹。**
- **绑定失败 ≠ 写入失败**：设备已经写好且校验一致，此时 API 失败要单独成句（例如"设备写入成功且回读一致，但点位绑定失败：<原因>，可稍后重试"），并保留一个"重试绑定"入口（可以是 `PointAssignStatus` 旁的按钮，或直接让用户重选点位再点写入）。
- **错误不污染已读数据**：AssignDevice 失败时保留 `Read*` 值，只更新状态文本；catch 里绝不重置基础配置。
- **异步化但不并发**：`DoReadConfig()` 阻塞，建议 `await Task.Run(...)`；整个链路用 `IsWriting` 禁止重入。注意 `SerialModel.Send` 有 `lock`，但 `ReadOneFrameData` 没有，重入会抢 data。
- **根节点唯一性**：`deviceType` 由快照里的 `ReadIsRoot=="1"` 决定（不要取回读后的值，回读理论上一致，但如果用户手改过 UI，以写入时的值为准）。自动绑定可能在一个老化架上造出第二个根节点，服务端是否校验未知，建议至少在 UI 上做提示。
- **点位重拉**：绑定成功后必须调用 `LoadAvailablePointsAsync(...)`（已占用点位要从列表消失），保留 `_pointsRequestSeq` 防乱序。

### 3.5 注意事项 / 风险

- 合并后"写入"会产生**两个副作用**（写设备 + 写服务端绑定），确认弹窗必须同时体现，否则用户对第二个副作用没有预期。
- 不要顺手改 `CheckWriteOK` 的比较口径——它是"是否允许调用 API"的唯一闸门，是本次方案的安全边界。
- 回读不一致时**坚决不调 API**；这条是用户明确要求的顺序，实现时不要因为"反正也要绑定"而放宽。
- Tab③ 的"读取并匹配老化架"、Tab① 的"读取"都保持原样，不在本次范围内；但要注意 `读取` / `写入` 会各自覆盖 `WroteSnapshot` 的语义，别让重构后的写入链路把 `WroteSnapshot` 提前清空（现有代码是在写入前就赋值 `WroteSnapshot`，供回读比对用），否则 `CheckWriteOK` 会永远走不到 true。
- 验收口径：① 串口已开、未选老化架 → 写入照常执行、回读校验照常，绑定步骤跳过并给出状态提示（不再弹"请选择老化架"）；② 三者齐备且回读一致 → 无人工介入完成绑定，点位列表自动重刷、已用点位消失；③ 回读不一致 → 提示 diff，API 一次都没被调用（可用服务端数据确认）；④ AssignDevice 失败 → 已写入的配置数据保留、文案区分"写入成功但绑定失败"、可再次点击重试；⑤ 连续快速点击不产生并发写/并发绑定。

---

## 四、需求二：基本信息持久化

### 4.1 现状核实（按场景逐项验证）

先给数据分类，这是回答"是否保留"的关键：

| 数据 | 位置 | 是否持久化 |
| --- | --- | --- |
| 老化架模板（名称/编号/SSID/密码/Mesh/通道） | `AgingShelves` → `AgingShelves.json` | **是**（且任何属性变更即时全量覆写） |
| **基础配置** ReadSSID/WIFI_PS/SSID1/WIFI_PS1/Chanel/Mesh_ID/Mesh_PS/AgingNumber/SERVER_IP/SERVER_UDP_Port/UDPPort/IsRoot/ReadUDP_ID | `MainViewModel` 普通自动属性 | **否，纯内存** |
| 外接设备可编辑列表 `ExternaldeviceList`（名称 / ProtoID / 通讯方式 / 协议 / 老化步骤） | `MainViewModel` | **否，纯内存**（该 Tab 目前还是 Collapsed） |
| 回读结果 `ReadExternalDevices` | `MainViewModel` | 否（合理，每次读取覆盖） |
| UI 选择态：串口号/波特率/数据位/停止位、当前选中老化架、当前点位 | `MainViewModel` | **否，纯内存** |

三种场景结论：

1. **界面内切换页面 / Tab → 全部保留**。原因：`MainWindow.DataContext` 在 XAML 里声明为单个 `<vm:MainViewModel />` 实例，`TabControl` 切换只卸载视觉树节点，VM 不重建。
   ⚠️ 但有一个真实时间窗：`EditableReadValue` 样式没有设置 `UpdateSourceTrigger`，TextBox 默认是 `LostFocus`。**最后一个正在编辑的控件如果还没失焦就去点按钮/Tab 头部以外的地方切换，理论上存在未回写的风险**；更常见的表现是"改了值立刻点『写入』，VM 里拿到的可能是旧值"。这属于隐患而非必现 bug，但应顺势修掉。
2. **退出后重新进入 → 丢失（除老化架模板）**。本应用是单窗口 + `StartupUri`，关闭主窗口即进程结束；`App.xaml.cs` 没有 `OnExit` 覆写，`MainWindow` 也没有 `Closing` 钩子，所以退出前不会做任何保存。基础配置、串口选择、外接设备列表全部回到默认值。
3. **完全重启应用 → 同上，丢失**。且当前持久化目录是 `AppContext.BaseDirectory`（`bin\Debug\net10.0-windows10.0.17763.0\`），`dotnet clean` / 重新部署 / 卸载都会删掉 `AgingShelves.json` —— 连唯一持久化的那份也不可靠。

### 4.2 暴露出的三个现网级隐患

- **即时全量覆写**：`AutoSaveHandler` 在每个 PropertyChanged 时 `File.WriteAllText` 整个集合，输入时每敲一个字符写一次文件；写到一半异常终止会产生**截断的坏 JSON**，下次 `LoadAgingShelves` 反序列化失败 → 弹警告 → **把 `AgingShelves` 重置为空集合**；此时只要再触发一次 `SaveAgingShelves`，真实数据就被空集合覆盖，彻底丢失。这是当前持久化实现最危险的一点。
- **加载失败弹阻塞式 MessageBox**：构造期弹窗会打断启动流程。
- **废旧文件残留**：输出目录里仍有 `NodeHistory.json`，逻辑已删、数据还在，容易误导。

### 4.3 改动点

1. 新增持久化基础设施（建议 `Ultils/UiStateStore.cs` 或新建 `Persist/` 目录）：负责序列化/反序列化、**原子写（临时文件 → 替换）**、失败备份（`.bak`）、版本字段、Schema 兼容。
2. `MainViewModel`：新增 `RestoreUiState()`（构造函数中、`LoadAgingShelves()` 之后、老化架同步之前调用）与 `PersistUiState()`（延迟写 + 退出前强制写）。
3. `View/MainWindow.xaml`：基础配置区与老化架编辑区的所有 TextBox 绑定改为 `UpdateSourceTrigger=PropertyChanged`，消除"最后 edit 未回写"窗口。
4. `App.xaml.cs` / `MainWindow`：补 `Window.Closing`（或 `Application.Exit`）调用强制保存。
5. `SaveAgingShelves` 改造：防抖后写 + 原子写 + 写失败不刷屏弹窗（降频或只更新状态文本）。
6. （可选）提供"清除本地缓存 / 恢复默认"入口，让生命周期可控。

### 4.4 实现要点

- **存储位置：建议迁到 `%LOCALAPPDATA%\LightGateway\`**，并把旧位置（exe 同目录）作为一次性迁移来源读取。理由：程序目录可能被只读、被 `clean` 重建、被卸载删除；用户目录随用户配置保留（除非卸载程序显式清理）。
- **存储格式**：单文件 `UiState.json`（UTF-8、`WriteIndented`），顶层带 `Version`，分区组织：
  - `BasicConfig`（ReadSSID/WIFI_PS/SSID1/WIFI_PS1/Chanel/Mesh_ID/Mesh_PS/AgingNumber/SERVER_IP/SERVER_UDP_Port/UDPPort/IsRoot/ReadUDP_ID）
  - `ExternalDevices`（`ExternaldeviceList` 全量，含 AgingSteps 三层结构）
  - `Serial`（SelectedComPort / 波特率 / 数据位 / 停止位）
  - `Selection`（上次选中老化架的 Id 或 RemoteId、上次点位 id —— 用于恢复选择，不作为绑定依据）
- **写入时机（推荐组合，不是单一选择）**：
  - 实时输入 → 只更新 VM 内存；
  - **统一延迟写**：由 VM 自身 `PropertyChanged` 驱动 300–1000ms 去抖（DispatcherTimer 单次重置），合并掉连续输入；
  - **窗口关闭 / 应用退出**时强制 flush（必须同步执行完成再退出）；
  - 关键动作前（打开串口、"写入"、绑定点位之前）也可各强制 flush 一次，保证"崩溃前最后一次状态"尽量完整；
  - 不建议"仅提交时写"——本工具的操作习惯是改完就走，异常退出必丢。
- **读取恢复时机**：VM 构造函数中尽早恢复，但要**先恢复老化架模板再恢复选中项**（按 Id 匹配，RemoteId 优先），匹配不到就置空、静默降级，绝不弹窗；恢复完成后再触发一次 `UpdateCanAssignDevice()`，避免按钮状态不一致。
- **数据生命周期**：
  - 更新：属性变更去抖落盘；老化架同步成功后随之保存服务端字段（沿用现有链路）；
  - 清除：不自动过期；由用户显式操作删除文件/节点，或"恢复默认"入口清 `BasicConfig`+`Serial`（**保留老化架模板**，除非用户二次确认）；
  - 卸载：用 LocalAppData 时可保留（取决于卸载程序是否清理）；继续留在安装目录则必丢；
  - 配置重置（`clean`/重装）：AppData 不受影响，安装目录方案会丢；**这是选目录的决定性理由**；
  - 损坏：反序列化失败就把坏文件改名备份为 `.bak.<时间戳>`，降级为默认值并记录一条状态文本，**不要**静默清空、不要阻塞式弹窗。
- **不持久化**：`ReadExternalDevices`（回读快照）、`WroteSnapshot`（比对基准；若要保留"重启后仍可比对"另议，默认不存）、`IsReadBackWaiting/IsWriting/IsAssigning/IsLoadingPoints` 等瞬时状态、下拉框派生集合 `ComboBoxAgingItems`。
- **密码字段**：`WIFI_PS/WIFI_PS1/Mesh_PS` 明文落盘有泄露风险。最低限度用 Windows DPAPI（`ProtectedData`，CurrentUser 作用域）加密这三个字段，并在 JSON 里标注加密标记以便兼容迁移；至少要在验收时明确记录"接受明文"这一决定。
- **去抖与线程**：写盘放到后台、`File` 操作用 try/catch；VM 里所有 ObservableCollection 的更新必须回到 UI 线程（Dispatcher）。
- **PropertyChanged 全属性钩子**：可用 PropertyChanged.Fody 的全局回调统一驱动去抖保存，替代现在逐对象 `AttachAutoSave` 的做法（更省事也更不容易漏对象）。实现时需按 weaver 约定核对回调方法签名，不确定时退回"在关键属性的 setter 或既有 OnXxxChanged 中显式调用"。

### 4.5 注意事项 / 风险

- 恢复时机与**老化架自动同步**有耦合：同步会用服务端 `name` 覆盖 `CustomDisplayName`，若先恢复选中项再同步，选中可能失效；务必固定顺序。
- `SelectedIsRoot` 用 `SelectedValue + SelectedValuePath="Content"` 绑定字符串，序列化时注意是字符串而非布尔。
- 持久化后多版本并存：老用户磁盘上是无 `Version` 的旧 `AgingShelves.json`，要能识别并迁移。
- 验收口径：① Tab 来回切、改值后立刻点按钮，VM 拿到的是新值；② 关闭重开，基础配置/串口选择/外接设备列表全部还原；③ kill 进程模拟强杀，最多丢失最后 <1s 的输入；④ 手动把 JSON 改成坏数据，启动不崩、不弹窗、生成 .bak；⑤ 卸载/重装（或 `dotnet clean`）后 AppData 方案的数据仍在。

---

## 五、需求三：串口列表自动刷新

### 5.1 现状分析

- `RefreshAvailablePorts()` 只在两处调用：构造函数、`case "刷新"` 按钮。`SerialModel.GetPortNames()` 即 `SerialPort.GetPortNames()`。
- 选择回落逻辑：仅当 `SelectedComPort` 不在新列表**且新列表非空**时才回退到 `PortNameList[0]`；列表为空时不清空（行为半吊子）。这个策略在"自动刷新"下会很吵：用户插另一个设备的瞬间就可能悄悄改掉选择。
- "刷新"按钮 `IsEnabled` 绑定 `ComboBoxEB`（串口打开时禁用），保留手动入口时建议维持"打开串口后不可刷新"的约束，或改为允许刷新但不改变已打开的端口。
- **必须先修的既有缺陷**：`SerialModel.CloseSerial()` 执行了 `_serial.Close()` + `_serial.Dispose()`，却没有重建 `_serial`。此后再次 `OpenSerial` 会在已释放对象上设置属性/打开，抛异常（拔插后重连必然踩到）。自动刷新上线后"拔掉→自动关闭→插回→再打开"是标准路径，这个缺陷会从偶发变成必现。
- 项目已有使用 `HwndSource.AddHook` 的先例（`WindowActivationHelper` 处理 `WM_ACTIVATE`），风格可直接沿用。

### 5.2 方案选型

| 方案 | 说明 | 评价 |
| --- | --- | --- |
| **A. WM_DEVICECHANGE 消息钩子（推荐）** | `MainWindow` 拿到 `HwndSource` 后 `AddHook`，处理 `WM_DEVICECHANGE(0x0219)` 的 `DBT_DEVICEARRIVAL(0x8000)` / `DBT_DEVICEREMOVECOMPLETE(0x8004)`，触发带防抖的刷新 | 零新依赖、事件即时、与现有代码风格一致；拿不到设备 FriendlyName（够用） |
| B. WMI `ManagementEventWatcher` | 监听 `Win32_PnPEntity` / `Win32_SerialPort` 的实例增删事件 | 能拿到设备名，但需引入 `System.Management`、事件有延迟/偶发漏报，仅作补充 |
| C. 低频轮询兜底 | 2–3s 定时器枚举比对 | 简单可靠但浪费资源；建议仅在特殊环境（如消息被安全软件吞掉）作为可开关兜底 |

建议：**A 为主 + 低频兜底开关默认关闭 + 手动"刷新"入口保留**。

### 5.3 改动点

1. `View/MainWindow.xaml.cs`：在 `SourceInitialized`（或 `Loaded`）注册 `HwndSource` 钩子，`Closed` 中注销；把 "USB 设备变更" 事件以命令/方法形式转给 VM。MainWindow 目前逻辑为空，改动面很小。
2. `ViewModel/MainViewModel.cs`：新增去抖刷新入口（建议 `SchedulePortsRefresh()`），内部统一调用既有 `RefreshAvailablePorts()`；把" SelectedComPort 消失时怎么办"从现状的隐式回落，改写成显式的两种分支。
3. `Model/SerialModel.cs`：修 `CloseSerial` 的 Dispose 后不可复用问题（关闭后重建内部 `SerialPort` 实例，或在 Open 前判断可用）；`GetPortNames()` 增加异常兜底。
4. `View/MainWindow.xaml`：手动"刷新"按钮保留；建议在其旁增加一个"自动刷新"开关状态时的小提示或状态文本。

### 5.4 实现要点

- **去重**：一次物理插拔通常会带来**多条**设备变更消息（尤其是 `DBT_DEVNODES_CHANGED(0x0007)` 会连发）。只处理 ARRIVAL / REMOVECOMPLETE 两个事件码，其余忽略。
- **防抖**：用单次 `DispatcherTimer`（Reset 语义）——合并窗口建议 300–500ms；另外**插入后延迟 800ms–1s 再做一次校验性刷新**，因为驱动安装完成前 `SerialPort.GetPortNames()` 可能枚举不到新 COM 口（尤其 CH340/CP210x 首次插入）。
- **设备移除时的处理（关键）**
  - 串口**已打开**且当前端口消失：绝不静默切换选择。应提示"检测到串口已断开"，主动 `CloseSerial()`，并把按钮复位为 `打开串口`/绿色、`ButtonIsEnabled=false`、`ComboBoxEB=true`（现状 `case "关闭串口"` 里手改 `Button.Content` 与 `Background` 的逻辑要能被调用方复用，避免状态不一致）。
  - 串口**未打开**：当前选择消失时才回落到第一个可用口；列表为空则清空选择并保持禁用态。
  - 端口名比较统一用大小写不敏感（现状已 `ToLower`，保留）。
- **异常处理**：`SerialPort.GetPortNames()` / 后续枚举在驱动安装期可能抛异常；catch 后**保留上一次的列表**，只更新状态文本，绝不因为一次失败把下拉框清空。
- **线程**：`WM_DEVICECHANGE` 回调在 UI 线程可直接操作 VM；若采用 WMI 方案，事件线程需Dispatcher 切回再改集合。
- **列表容器**：`PortNameList` 目前是 `string[]`，每次整体赋值会触发 `ItemsSource` 重绑并可能重置 ComboBox 选中项。建议在"内容未变化"时跳过赋值（做一次有序比较），这也是最有效的去重手段。
- **生命周期**：务必在窗口关闭/应用退出时注销钩子与定时器，防止回调访问已释放资源。

### 5.5 注意事项 / 风险

- 必须先修 `SerialModel` 的 Dispose 复用缺陷，否则自动刷新上线后会立刻暴露"重连后无法再打开串口"。
- 不要在设备移除时自动 `CloseSerial` 而没有用户提示 —— 对正在执行的读取/写入会造成困惑，考虑加"读取/写入进行中则延后处理或仅提示"的策略。
- 不要在 `SerialPort` 打开时尝试枚举同一端口的属性做"富信息展示"（会触发驱动层 IO，可能卡 UI）。
- 虚拟机/USB 集线器场景下 DEVICECHANGE 风暴：防抖参数要能用一个常量集中配置，便于现场调参。
- 验收口径：① 插入 USB 转串口，1s 内下拉框出现新 COM 且不需点击刷新；② 拔出已打开的串口，界面复位为"未打开"且给出提示，选择不被静默改掉；③ 连续快速插拔 5 次不产生多余动作/不崩；④ 手动"刷新"仍可用；⑤ 关闭窗口后无回调泄漏（日志/断言可观测即可）。

---

## 六、建议实施顺序与交叉影响

| 顺序 | 事项 | 理由 |
| --- | --- | --- |
| 0 | **需求一：两个写入按钮合并 + 绑定上下文快照** | 范围最小、收益最直接（修掉自动绑定必失败的缺陷），且与其他两项无耦合，可立即开工 |
| 1 | **修 `SerialModel.CloseSerial` 复用缺陷 + `GetPortNames` 异常兜底** | 需求三的前置条件，独立、风险最低 |
| 2 | **持久化基础设施落地（含 AppData 迁移、原子写、去抖、UIState 分区）** | 需求二主体；顺带修掉 AgingShelves 的损坏风险 |
| 3 | **需求三：自动刷新** | 与需求一无耦合，但必须排在 1 之后；需求二的"恢复上次串口选择"会受益于稳定列表 |

**交叉影响提醒**
- 需求一合并后命令清单少了一项「写入并重新读取」；`CLAUDE.md` 的 "Command Dispatch Pattern" 段落与 "Aging Point & Device Assignment" 章节、以及本仓库的记忆笔记都要同步更新，否则后续会话会按旧清单改代码。
- 需求一写入链路里"先用快照取绑定参数"这条约定要写到 `CLAUDE.md`：任何在 `DoReadConfig` 之后读取 `SelectedAgingShelf` / `SelectedAgingPoint` 的新代码都要先想清楚是否会被回读流程改写。
- 需求二引入 AppData 目录后，`AgingShelves.json` 的位置会变；若现场已有存量数据，**迁移必须是单向且幂等**（迁移成功后的旧文件不要立刻删，留一个 `.migrated` 标记更稳）。
- 需求三的自动刷新会提高 `SelectedAgingShelf`/`SelectedAgingPoint` 被重置的概率，与需求二的"恢复上次选择"存在语义拉锯，建议：端口选择的自动回落限定在**串口未打开**时，且回落结果不覆盖用户显式选择过的 AppData 记录（除非该端口确实不存在）。
- 全部三项都触碰 `MainViewModel` 这个 1480 行的类；建议借这次机会把"持久化"与"设备管理通知"拆到 `Ultils/` 下的独立类型，VM 只保留编排，避免继续膨胀。

---

## 七、实施进展

### 需求一：已完成（2026-09-22）

`dotnet build` 通过：**0 错误**（仅既有 NU1510 + nullable 警告，未新增）。

| 文件 | 改动 |
| --- | --- |
| `ViewModel/MainViewModel.cs` | 新增 `WriteVerifyAndBindAsync()`：写设备 → 等待 → 回读（跑后台线程）→ `CheckWriteOK`，一致才 `BindPointAsync(ctx)`，不一致中止且不碰任何服务端接口 |
| 同上 | 新增 `BindContext` 私有类 + `CaptureBindContext()`：在 `serialModel.Send` **之前**快照节点 ID / 老化架 id / 点位 id / deviceType |
| 同上 | 新增 `DescribeBindPlan()`：写入确认框追加一行绑定目标（架 → 点位、节点 ID、是否根节点），一次确认覆盖两个副作用 |
| 同上 | 新增 `ApplyReadBackShelfSelection()`：用户已选中真实接口老化架时保留其选择，否则回退原 `TryMatchAgingShelfFromRead()` 行为 |
| 同上 | `AssignDeviceAndRereadAsync()` → `BindPointAsync(ctx)`：不再冗余 `DoReadConfig + SelectReadShelfItem`，成功后 `await LoadAvailablePointsAsync` 重拉点位（`await` 取代原来的 fire-and-forget） |
| 同上 | 删除 `case "写入并重新读取"` 与 `TryResolveAssignIds()`；`写入` 的 finally 补 `IsAssigning=false` 与 `UpdateCanAssignDevice()` |
| `View/MainWindow.xaml` | 删除「写入并重新读取」按钮；「写入」改回 `IsEnabled="{Binding ButtonIsEnabled}"`（串口打开即可写），操作流程提示区补了新文案与一行 `PointAssignStatus` 状态文本 |
| `CLAUDE.md` | 命令清单去掉旧按钮并加禁止再加第二个写入按钮的说明；新增 "Write + Verify + Bind" 章节固化快照约定与失败口径 |

**尚未做的验收（需要真机/真服务）**：成功的写+绑定全链路联调（`AssignDevice` 是写操作，未执行）；回读不一致时的中止分支；绑定失败后的重试路径。
