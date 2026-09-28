# 改造计划：写入结果统一反馈 + 成功后自动关闭串口

> 状态：**待评审，尚未改代码**。本文只描述现状、改动点、交互流程与验证方式。

---

## 一、现状梳理（代码事实）

### 1.1 「写入」入口

| 位置 | 说明 |
|------|------|
| `View/MainWindow.xaml:352-360` | 实际可见的「写入」按钮（设备读取结果 页签），`Command=CmdClick`，参数传自身，`IsEnabled={Binding ButtonIsEnabled}` |
| `View/MainWindow.xaml:1193-1203` | 第二个「写入」按钮，位于 `Visibility="Collapsed"` 的 StackPanel 内，UI 不可达，走同一 `case "写入"` |
| `ViewModel/MainViewModel.cs:188-223` | `case "写入"`：重入守卫 → `await WriteVerifyAndBindAsync()` → catch 兜底弹窗 → finally 复位标志并依据 `serialModel.IsOpen` 恢复按钮可用性 |
| `ViewModel/MainViewModel.cs:1092-1184` | `WriteVerifyAndBindAsync()`：完整编排（见 1.2） |

### 1.2 当前写入链路（`WriteVerifyAndBindAsync`）

```
ReadUDPPort = "1"
 ├─ CheckConfigOnRead()            // 385-428：校验失败时自身弹 MessageBox 并 return false → 流程静默结束
 ├─ CaptureBindContext()           // 1224：必须在 Send 之前快照（SelectedAgingShelf / SelectedAgingPoint 会被回读写坏）
 ├─ 组装 ChangeConfig JSON
 ├─ MessageBox 写前确认（配置快照 + DescribeBindPlan）
 │    └─ 用户点「否」→ return，无任何反馈
 ├─ serialModel.Send(message)
 ├─ IsReadBackWaiting = true（遮罩）
 ├─ await RunReadBackDelayAsync()  // 8 秒
 ├─ await Task.Run(DoReadConfig)   // 超时抛 TimeoutException("ESP32 未在规定时间内返回配置")
 ├─ ApplyReadBackShelfSelection()
 └─ CheckWriteOK()
      ├─ 不一致  → MessageBox(Warning) + return                       【有弹窗】
      ├─ 一致 且 bindContext == null  → 只写 ReadStatusMessage         【★无弹窗：需求 1 要补的洞1】
      └─ 一致 且 bindContext != null  → await BindPointAsync()
             ├─ AssertDevice 失败 → MessageBox(Warning)                【有弹窗】
             ├─ 抛异常            → MessageBox(Error)                  【有弹窗】
             └─ 成功 → 刷新点位，【★无任何弹窗：需求 1 要补的洞2】
```

异常冒泡到 `case "写入"` 的 catch → MessageBox(Error)。**结论：目前"成功路径"不弹窗，失败路径弹窗口径 3 种且不统一，全部散落在 `WriteVerifyAndBindAsync` / `BindPointAsync` / `case` 三处。**

### 1.3 串口现状

| 位置 | 说明 |
|------|------|
| `MainViewModel.cs:268-289` | `case "打开串口"` / `case "关闭串口"`：`obj.Content = "关闭串口"/"打开串口"`、`obj.Background = Red/Green`，**直接改 UI 元素**，VM 其他路径无法改这两个属性 |
| `MainViewModel.cs:1379-1401` | `OpenSerial()` / `CloseSerial()` → `SerialModel` |
| `SerialModel.cs:82-95` | `CloseSerial()`：`Close() + Dispose()` 后 **未重建 `_serial`** → 之后 `OpenSerial` 必抛（disposed object）。这是本次"自动关闭后要重开"的硬阻塞，必须先修 |
| `SerialModel.cs:96-111` | `IsOpen` getter 直接读 `_serial.IsOpen`，setter 只发通知不存值 |
| `MainViewModel.cs:1321-1329` | `UpdateCanAssignDevice()` 依赖 `serialModel.IsOpen` |
| XAML 233-236 | 串口按钮 **未** 绑定 Content/Background，且任何时候可点 |

---

## 二、改造目标

1. 点击「写入」后，**任何终态**（含成功、写失败、写成功但绑失败、参数不全、用户取消以外的异常）都弹出**唯一一次**结果弹窗，文案明确区分"写入失败 / 写入成功但绑定失败"。
2. 只有 **写入成功 且 绑定成功**，用户在结果弹窗点「确认」时才关闭串口；其余情况点确认仅关窗，串口保持原状态。用户不点确认（X 关窗）→ 串口一律不动。

---

## 三、改动点清单

### 3.1 `Model/SerialModel.cs`（前置修复，硬依赖）

| # | 修改点 |
|---|--------|
| S1 | `CloseSerial()`：关闭后 `_serial.DataReceived -= OnDataReceived` → `Dispose()` → **`_serial = new SerialPort(); _serial.DataReceived += OnDataReceived;`**，使串口可重复打开。`IsOpen=false; SerialPortHasOpen=false` |
| S2 | `Close()` 用 try/catch 包裹（拔线时 `Close()` 可能抛），保证 `_serial` 始终被安全替换 |
| S3 | （建议）`OpenSerial` 增加 `_serial == null` 兜底；或改为返回 bool，由 VM 决定如何提示 |

> 不修 S1，自动化关闭后"再点打开串口"必然抛异常，功能不可回归。

### 3.2 `ViewModel/MainViewModel.cs`

| # | 位置 | 修改点 |
|---|------|--------|
| V1 | ~1206（`BindContext` 附近） | 新增 `private sealed class WriteOutcome`：`Stage Stage`（Validation / Write / Verify / Bind）、`bool Success`、`bool CanCloseSerial`、`string Title`、`string Summary`、`string? Detail`、`bool ShowDialog` + 3 个静态工厂 `Fail/Success/BindFailed` |
| V2 | 385-428 | `CheckConfigOnRead()` → `bool TryValidateConfig(out List<string> errors)`：**不再自己弹 MessageBox**，把缺失项收集成列表交给统一弹窗（唯一调用点 1096 行，改造安全） |
| V3 | 1092-1184 | `WriteVerifyAndBindAsync()` 改为 `Task<WriteOutcome>`：全程 try/catch 消化异常，删除内部 3 处 MessageBox；写前确认框保留（属于用户授权，不算结果反馈）；每个终态都构造 WriteOutcome 返回 |
| V4 | 1272-1316 | `BindPointAsync` 改为 `Task<(bool success, string message)>`：**不再弹 MessageBox**，失败原因（服务端 message / 超时秒数 / 异常）返回给 V3 |
| V5 | 188-223 | `case "写入"` 重排（见 §4）：`await` 拿 WriteOutcome → finally 复位 → 关遮罩后再弹窗 → 按 `CanCloseSerial && confirmed` 关串口 |
| V6 | 268-289 | `case "打开串口"/"关闭串口"` 删除 `obj.Content/obj.Background` 直写，改调用 `ApplySerialUiState(bool isOpen)` |
| V7 | 新增 | `private void ApplySerialUiState(bool isOpen)`：统一设置 `SerialButtonContent` / `SerialButtonBackground` / `ButtonIsEnabled` / `ComboBoxEB` / `UpdateCanAssignDevice()` |
| V8 | 新增 | `public string SerialButtonContent { get; set; } = "打开串口";` `public Brush SerialButtonBackground { get; set; } = Brushes.Green 等价的 SolidColorBrush`（支持 testability 与自动关闭） |
| V9 | 新增 | `private void ShowWriteResult(WriteOutcome o)`：`new WriteResultDialog(o){Owner=Application.Current.MainWindow}.ShowDialog()`，返回用户是否点了主按钮 |
| V10 | 新增 | `private bool CloseSerialAfterWrite()`：`if (!serialModel.IsOpen) return true;` try → `CloseSerial()` → `ApplySerialUiState(false)`；失败 MessageBox 且**不动 UI 状态** |
| V11 | 1398-1401 | `CloseSerial()` 不变（保持纯模型调用），UI 联动统一在 V7/V10 |

### 3.3 `View/MainWindow.xaml`

| # | 修改点 |
|---|--------|
| X1 | 233-236 串口按钮改为 `Content="{Binding SerialButtonContent}" Background="{Binding SerialButtonBackground}"`（仍用 `CommandParameter={RelativeSource Self}`，继续走 `CmdClick` 分支） |
| X2 | （可选）为串口按钮加 `x:Key="SerialToggleButton"` 样式，修掉 hover 时背景被触发器刷成蓝色的既有小瑕疵 |
| X3 | （可选）删除 1181-1216 已 `Collapsed` 的旧按钮组，避免误以为存在第二个写入入口 |

### 3.4 `View/WriteResultDialog.xaml(.cs)`（新增）

轻量窗口，`SizeToContent=WidthAndHeight`、`ResizeMode=NoResize`、`WindowStartupLocation=CenterOwner`、幅宽约 460。

- 头部：图标 + 标题（成功绿 `#16A34A` / 失败红 `#DC2626` / 部分成功橙 `#EA580C`）
- 主体：`Summary`（加粗结论）+ `Detail`（`TextWrapping=Wrap`，超过 ~6 行进 `ScrollViewer`）——失败明细、回读 diff、服务端 message 都放这里
- 底部：**单个主按钮**
  - 绑定成功 → 文案「确认并关闭串口」
  - 其余场景 → 文案「确认」
- 返回值：`DialogResult = true` 仅由主按钮设置；标题栏 X / Alt+F4 → `false/null`（视为"未确认"）

> **为什么不用 MessageBox**：单按钮 `MessageBoxButton.OK` 时标题栏 X 被系统禁用 → "用户未点确认"场景不成立、且按钮文案无法自定义；`OKCancel` 的 Cancel 语义别扭。自建窗口约 60 行 XAML，可控且放得下失败明细。
> **备选方案 B（最小改动）**：保留 MessageBox.OK，文案写明"确认后将自动关闭串口"，点击 OK 后关闭串口。代价：用户没有"保留串口"的选择。**需评审二选一。**

### 3.5 文档

`CLAUDE.md` 的 "Write + Verify + Bind" 章节补充"结果反馈与串口自动关闭"小节；本文件归档到 `docs/`。

---

## 四、完整交互流程（点击「写入」→ 关闭串口）

```
(UI线程) 点击「写入」
  │
  ├─ IsWriting ? return                       // 重复点击守卫（已有，保留）
  ├─ IsWriting = true; ButtonIsEnabled = false; ComboBoxEB = false
  │
  ├─ await WriteVerifyAndBindAsync()  ────────────────────────────────┐
  │    参数不全 ──► Outcome(Fail/Validation, Detail=缺失字段列表)       │
  │    点儿     ──► Outcome(ShowDialog=false)  // 用户主动取消          │
  │    发送/回读超时 ──► Outcome(Fail/Write, Detail=异常原文+排查建议)   │
  │    回读不一致 ──► Outcome(Fail/Verify, Detail=diffs + 未调用绑定)    │
  │    一致且无绑定上下文 ──► Outcome(部分成功, Detail=如何选择点位)      │
  │    一致→绑定失败 ──► Outcome(部分成功, Detail=服务端原因)            │
  │    一致→绑定成功 ──► Outcome(Success, CanCloseSerial=true)         │
  └───────────────────────────────────────────────────────────────────┘
  │
  ├─ finally: IsReadBackWaiting=false; IsWriting=false; IsAssigning=false
  │           ButtonIsEnabled 保持 false（防止弹窗期间再次触发）
  │
  ├─ if (!outcome.ShowDialog) → ApplySerialUiState(serialModel.IsOpen); 结束
  │
  ├─ confirmed = WriteResultDialog.ShowDialog(outcome)   // 遮罩已消失，模态阻塞
  │     ├─ 点主按钮 → confirmed = true
  │     └─ 点 X     → confirmed = false
  │
  ├─ if (confirmed && outcome.CanCloseSerial)  // 仅"写入成功 + 绑定成功"
  │       CloseSerialAfterWrite()  → serialModel.CloseSerial() + ApplySerialUiState(false)
  │                                  （按钮变绿"打开串口"、端口下拉解锁、写入/读取禁用）
  │   否则：串口保持原样
  │
  └─ ApplySerialUiState(serialModel.IsOpen)    // 统一收口（尤其"未确认"分支要恢复按钮可用）
```

**关键顺序约束**

1. **遮罩必须先消失再弹窗**：`IsReadBackWaiting=false` 在 finally，`ShowDialog` 在其后，否则遮罩（`ZIndex=100`、半透明黑）会把结果窗盖住。
2. **重试性**：`ApplySerialUiState` 幂等，重复调用不会破坏按钮状态。
3. **绑定成功后的点位刷新**（`LoadAvailablePointsAsync`）在关闭串口之前已完成 —— HTTP 不依赖串口，顺序安全。

---

## 五、弹窗文案与按钮设计

| # | 场景 | 标题 / 色 | Summary | Detail | 主按钮 | 点确认后 |
|---|------|-----------|---------|--------|--------|----------|
| 1 | 参数不全 | 写入失败 / 红 | 配置不完整，未向设备写入任何数据。 | 逐条列出缺失字段（WiFi 名称、WiFi 密码、MeshID、Mesh 密码、节点 ID、UDP 端口、服务器 IP、服务器端口） | 确认 | 仅关窗，**串口不动** |
| 2 | 写前确认点「否」 | — | 不弹窗（用户主动取消，无歧义） | — | — | 串口不动 |
| 3 | 发送/回读超时 | 写入失败 / 红 | 设备未在规定时间内返回配置，写入结果未知。 | 异常原文 + "请检查串口连接与设备供电后重试" | 确认 | **串口不动** |
| 4 | 回读比对不一致 | 写入失败 / 红 | 已发送写入命令，但设备回读值与写入值不一致。 | 逐条 `Key: 期望 'x'，实际 'y'` + "未调用点位绑定接口" | 确认 | **串口不动** |
| 5 | 一致，未选接口老化架/点位 | 写入成功（未绑定点位） / 橙 | 配置已写入并通过回读校验；未选择老化架或点位，已跳过绑定。 | 提示如何重新选架/选点位后再写入 | 确认 | **串口不动**（不算"都成功"） |
| 6 | 一致 + 绑定成功 | 写入成功 / 绿 | 配置写入成功，已绑定 `老化架X → 点位Y`（节点 ID `xxxx`，根/非根节点）。 | 点位列表已刷新 | **确认并关闭串口** | **关闭串口** + 关窗 |
| 7 | 一致 + 绑定失败 | 写入成功，点位绑定失败 / 橙 | 配置已写入设备并通过回读校验，但点位绑定失败。 | 服务端 message / 超时秒数 / 异常原文 + "可重新选择点位后再次写入" | 确认 | **串口不动** |

统一约束：
- 失败时**不清空 `Read*`、不重置 `WroteSnapshot`**（现状如此，保留），用户可直接改点位重试。
- 文案必须能区分"写入失败"（#1/#3/#4）与"写入成功但绑定失败"（#7）—— 这是本次需求的核心口径。

---

## 六、边界情况与处理策略

| # | 边界 | 处理 |
|---|------|------|
| E1 | 重复点击「写入」 | `IsWriting` 守卫；且弹窗期间 `ButtonIsEnabled` 保持 false，弹窗关闭后再 `ApplySerialUiState`。弹窗为模态，主窗不可点，双重保险 |
| E2 | 用户点 X 关掉结果窗 | `confirmed=false` → 串口一律不关；随后 `ApplySerialUiState(true)` 恢复"写入/读取可用、端口下拉锁定、串口按钮红色'关闭串口'" |
| E3 | 写入成功+绑定成功，但串口已断开/拔出 | `CloseSerialAfterWrite()` 先判 `serialModel.IsOpen`，幂等返回 true，只刷 UI 不报错 |
| E4 | 关闭串口本身异常 | try/catch → MessageBox("串口关闭失败：…")，UI 保持"已打开"状态，不假装成功 |
| E5 | 自动关闭后再次「打开串口」 | 依赖 S1 `_serial` 重建；回归必测项 |
| E6 | 写入失败后立即重试 | 允许；重新弹写前确认框，`WroteSnapshot` 以最近一次 Send 为准 |
| E7 | 弹窗 Owner | `Owner = Application.Current.MainWindow`，避免被主窗遮住或 Alt+Tab 后丢失焦点 |
| E8 | 第二个 Collapsed 的「写入」按钮（XAML 1200） | 走同一 case，行为天然一致；建议后续删除（X3） |
| E9 | 绑定接口是写操作 | 联调/自测优先用非生产点位，或用桩替 `AssignDeviceAsync` |
| E10 | `IsOpen` 既有怪实现（setter 不存值） | 本次不重构，只保证 getter（`_serial.IsOpen`）在重建后仍正确 |

---

## 七、实施顺序

1. **S1/S2/S3** `SerialModel` 可重复开闭（独立、先做、可单独验证）
2. **V1/V2/V3/V4** 统一结果模型，去除链路内散落 MessageBox
3. **X1 + V7/V8/V6** 串口按钮改为数据绑定，打开/关闭状态单一出口
4. **3.4 新增 `WriteResultDialog`**
5. **V5/V9/V10/X2/X3** 编排与自动关闭串口
6. 文档同步（`CLAUDE.md` / `docs/`）

---

## 八、验证方式

### 8.1 静态

- `dotnet build LightGateway.csproj` 通过，不新增 nullable / 编译错误（允许既有的 NU1510 与 nullable 警告基线）。
- `grep -n "MessageBox" ViewModel/MainViewModel.cs`：写入链路（1092-1316、188-223）内应**仅剩**写前确认框与串口层错误提示，其余结果弹窗全部消失；`WriteResultDialog.ShowDialog` 是唯一出口。

### 8.2 手工回归矩阵

| 场景 | 构造方式 | 期望 |
|------|----------|------|
| A 参数不全 | 清空 WiFi 密码后点写入 | 失败弹窗列出缺失项；串口仍开、按钮仍红"关闭串口" |
| B 回读超时 | 写入前拔掉串口线 / 断开设备 | 失败弹窗"设备未返回配置"；串口状态不变 |
| C 回读不一致 | 篡改 Send 前的 baseConfig（临时桩） | 失败弹窗列出 diff；串口不变 |
| D 一致但无绑定目标 | 老化架选「读取配置」伪项、点位留空 | 橙色"未绑定点位"弹窗；串口不变 |
| E 写入+绑定全成功 | 正常设备 + 可用点位 | 绿色成功弹窗，按钮「确认并关闭串口」；点确认 → 串口按钮变绿"打开串口"、端口下拉解锁、写入/读取禁用 |
| E' 同 E 但点 X 关窗 | — | 串口**仍打开**，按钮仍红"关闭串口"，写入按钮可用 |
| F 绑定失败 | 断网 / 停服务端 / 指向错误 IP 制造超时 | 橙色"写入成功，点位绑定失败"并给出超时原因；串口不变；改点位后可再次写入成功 |
| G 自动关闭后重开 | E 之后点「打开串口」再点「读取」 | **必须成功**（验证 S1）；否则即为回归失败 |
| H 重复点击写入 | 写入过程中连点 | 只执行一次写入，只有一次结果弹窗 |
| I 关闭串口异常 | 关闭瞬间拔线 | 提示"串口关闭失败"，UI 保持已打开状态 |

### 8.3 联调注意

`AssignDevice` 是**服务端写操作**，真机验证前确认老化架/点位为测试资源；条件不具备时先用桩替换 `RackApiClient.AssignDeviceAsync` 跑通 E/F 两条路径，再上真机。
