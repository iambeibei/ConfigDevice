# LightGateway 项目长期笔记

## 老化架（Aging Rack）相关约定

- 老化架列表唯一数据源是接口 `POST https://192.168.40.116:7088/DeviceServiceCallback/RackPageList`，
  请求体固定 `{"pageSize":10000000,"pageIndex":1}`。本地 `AgingShelves.json` 只做缓存/持久化。
- **该接口 `code == 1` 表示成功**，不是 0 / 200，切勿套用 `WebApiClientModule.ApiResponse.Success` 的语义。
- **该接口 HTTPS 证书不受信**（内网自签名）。`RackApiClient` 只对 host `192.168.40.116` 放行证书校验，
  改动网络层时不要把它改成全局跳过校验。
- 同步以接口 `id` → `AgingShelfModel.RemoteId` 为唯一键做差集。同步失败时绝不改动本地数据；接口返回空列表不删除任何本地项。

## 点位与设备分配

- 相关接口（同域、同自签名证书、`code == 1` 才算成功）：
  - `AvailablePointList`：入参 `{agingRackId:<long>, pageSize:100000, pageIndex:1}`，data 元素含
    `id / pointSequence / layerNumber / deviceCount`，下拉 label = `第{layerNumber}层-点位{pointSequence}`，value = `id`。
  - `AssignDevice`：入参 `{deviceId, deviceType, agingRackId, agingRackPointId}`；deviceId 取 `ReadUDP_ID`，
    deviceType = 根节点 2 / 非根节点 1（看 `ReadIsRoot == "1"`）；失败时返回 `{code:-1, message:"..."}`。
- 「写入」是**唯一入口**：写设备 → 自动回读 → `CheckWriteOK` 校验 → **校验一致才** `BindPointAsync(ctx)` 调
  `AssignDevice`；不一致即中止，一次都不调服务端接口。旧的独立按钮「写入并重新读取」已删除，别再加第二个写入按钮。
- **绑定参数必须快照**（`CaptureBindContext()`，在 `Send` 之前）：`DoReadConfig` 之后
  `TryMatchAgingShelfFromRead()` / `SelectReadShelfItem()` 会把 `SelectedAgingShelf` 切成「读取配置」伪项
  （RemoteId 为空 → `GetSelectedRemoteRackId()` 返回 null），且下拉变更会重拉点位并清空 `SelectedAgingPoint`。
  在回读之后读这两个属性，拿到的必然是空值 —— 这是历史上自动绑定必失败的根因。
- 绑定失败 ≠ 写入失败：不清空 `Read*`，只更新 `ReadStatusMessage`/`PointAssignStatus`，文案必须区分二者。
- 老化架快速切换会产生并发点位请求，靠 `_pointsRequestSeq` 序号丢弃过期响应，改动这块时别把它删掉。
- 联调注意：`AssignDevice` 是**写操作**，真实设备联调前先确认不会产生脏数据。

## 持久化与串口（务必记住的现状）

- **唯一持久化数据**：`AppContext.BaseDirectory\AgingShelves.json`（exe 同目录，`dotnet clean`/重装/卸载会丢）。
  基础配置 `Read*` 全族、`ExternaldeviceList`、串口选择与上次选中项都是 **MainViewModel 纯内存属性**，
  退出即丢；`App.xaml.cs` 无 OnExit、MainWindow 无 Closing 保存钩子。
- 老化架保存链路 `AttachAutoSave → AutoSaveHandler → SaveAgingShelves` 是**每次 PropertyChanged 全量覆写**，
  存在写坏后反序列化失败→被重置为空→二次覆盖致数据丢失的风险，改造时一并处理（去抖 + 原子写 + 备份）。
- `SerialModel.CloseSerial()` 执行 `Close()+Dispose()` 后**未重建 `_serial`**，之后 `OpenSerial` 必抛异常。
  任何涉及"关闭再打开串口"的改动前先修这个。
- 自动化的写操作（`AssignDevice` 改服务端绑定）不能静默执行，保留用户确认入口。
- 计划类文档放 `docs/`，如 `docs/LightGateway-改造计划.md`（操作流程合并 / 持久化 / 串口自动刷新）。

## 环境备忘

- 本机 bash（Git Bash 便携版）缺少 coreutils：`mkdir` / `grep` / `head` / `cut` 等不可用，`cd` 与 `curl.exe`、`dotnet` 可用。
- PowerShell 工具在本次会话中无 stdout 回显，需要取回输出时优先用 Bash + `curl.exe` / `dotnet`。
