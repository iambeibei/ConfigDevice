# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

LightGateway is a .NET 10 WPF desktop application for configuring an ESP32-based gateway over a serial port. It uses MVVM with command bindings and communicates via a JSON protocol over serial.

## Build and Run

- Build: `dotnet build` (or `dotnet build LightGateway.slnx`)
- Run: `dotnet run`
- Target framework: `net10.0-windows10.0.17763.0`
- Output: Windows executable (`OutputType=WinExe`)

There are no unit tests, lint scripts, or CI files in this repo. Build warnings are expected and mostly nullable-reference warnings plus a `NU1510` warning about the explicit `System.Text.Json` package reference.

## Architecture

### MVVM Structure

- `View/MainWindow.xaml` — single main window with two tabs: **设备读取结果** (editable config read-back) and **外接设备设置** (external device configuration).
- `ViewModel/MainViewModel.cs` — central viewmodel. All UI commands are dispatched through a single `ICommand` (`CmdClick`) and a `switch` on `Button.Content`.
- `Model/` — serial communication, device models, and protocol parsing.
- `Command/Base.cs` — `BaseCommand<T>` implementation and `BaseNotify` helper.
- `Ultils/` — utility byte-conversion helpers and the HTTP client for fetching protocol JSON.

### Command Dispatch Pattern

Buttons bind to `CmdClick` and pass themselves as `CommandParameter`. `MainViewModel.CmdCmdClickAction` switches on `obj.Content.ToString()`. Current recognized contents include: `读取`, `写入`, `回读比对`, `+1`, `刷新`, `打开串口`, `关闭串口`, `获取协议`, `添加/删除PreAction`, `添加/删除AfAction`, `添加/删除读取参数`, `添加设备`, `删除设备`, `同步老化架`, `删除老化架`, `读取并匹配老化架`.

Note: `写入` is the single entry point for the whole write flow — it writes the device, auto-reads back, verifies, and only then binds the aging-rack point. The standalone `写入并重新读取` button was removed (see "Write + Verify + Bind" below); do not reintroduce a second write button.

Adding a new button usually means:
1. Adding the XAML button with `Command="{Binding CmdClick}"` and `CommandParameter="{Binding RelativeSource={RelativeSource Mode=Self}}"`.
2. Adding a matching `case` in `MainViewModel.CmdCmdClickAction`.

### Serial Communication

- `Model/SerialModel.cs` wraps `System.IO.Ports.SerialPort`.
- Config commands are sent as UTF-8 JSON via `SerialModel.Send(byte[])`.
- Responses are read with `SerialModel.ReadOneFrameData(int FirstByteReadTimeOut = 300, int ReadTimeOut = 3)`, which waits for the first byte then drains the frame.
- The class also contains legacy Modbus/CRC helpers (`ComputeCRC16`, `BuildFunc3And4Message`, etc.) that are not part of the JSON config flow.

### JSON Protocol

- Read config request: `{ "Cmd": "ReadConfig" }`
- Write config request: `{ "Cmd": "ChangeConfig", "Value": { ... }, "ExternalDevices": [ ... ] }`
- Response model: `ReadConfigResponse` in `MainViewModel.cs`, with `Value` mapped to `ChangeConfigValue`.
- The read-back value `DEVICE_ID` is bound to the UI property `ReadUDP_ID` due to historical naming.

### External Devices

- Editable design-time model: `ExDeviceModel` / `AgingSteps` / `AgingFunction` / `Action` / `PreAction` / `AfterAction` / `SampleData`.
- Read-back model: `ReadExternalDeviceModel` and its `Read*` counterparts, kept separate so a read does not overwrite pending edits.

### INotifyPropertyChanged

- Most viewmodels and models use `[AddINotifyPropertyChangedInterface]` from `PropertyChanged.Fody`.
- `FodyWeavers.xml` configures the `PropertyChanged` weaver.
- `Command/Base.cs` contains a manual `BaseNotify` implementation that is largely superseded by Fody.

### Protocol Fetching

- `Ultils/WebApiClientModule.cs` contains `ProtocolJsonApiClient`, which calls `http://10.16.160.51:5000/Login/GetJson?ID={id}` to fetch protocol JSON.
- `MainViewModel.GetProto` and the `获取协议` command use this to populate `protos`, parsed by `ProtocolCommandParser` in `Model/ProtocolParserModule.cs`.

## Key Files

| File | Role |
|------|------|
| `View/MainWindow.xaml` | Main UI layout and tab content |
| `ViewModel/MainViewModel.cs` | Command dispatcher, config properties, JSON parsing, node-ID history |
| `Model/SerialModel.cs` | Serial port open/close/send/read |
| `Model/ExDeviceModel.cs` | External device editable and read-back models |
| `Model/ProtocolParserModule.cs` | Parses fetched protocol JSON into command lists |
| `Ultils/WebApiClientModule.cs` | HTTP client for protocol JSON API |
| `Ultils/RackApiClient.cs` | HTTP client for the rack / point / assignment APIs (`RackPageList`, `AvailablePointList`, `AssignDevice`) |
| `Ultils/Util.cs` | Byte/hex/endian helpers |
| `Command/Base.cs` | `BaseCommand<T>` and `BaseNotify` |
| `FodyWeavers.xml` | Configures `PropertyChanged.Fody` |

## Aging Rack Sync (老化架同步)

- Endpoint: `POST https://192.168.40.116:7088/DeviceServiceCallback/RackPageList`, body fixed at
  `{"pageSize":10000000,"pageIndex":1}`. Response: `{ total, data[{id,name,remark,masterControlId}], code, message }`.
- Two non-obvious constraints, both verified against the live service:
  1. The endpoint is HTTPS with an **untrusted certificate** — `RackApiClient` bypasses validation only for the
     `192.168.40.116` host whitelist; every other host is still validated strictly.
  2. **`code == 1` means success** (not 0 / 200). Do not reuse `ApiResponse.Success` semantics here.
- Sync model: diff by `RemoteId` (the interface `id`). New remote racks are added, racks missing from the response are
  deleted, existing ones get `CustomDisplayName` refreshed from `name` so all aging-rack dropdowns show the server name.
  Racks with no `RemoteId` (legacy local entries) are never deleted.
- **Failure policy: never touch local data on failure.** Timeouts, HTTP errors, `code != 1`, empty/garbage payloads all
  leave `AgingShelves` untouched and only update `RackSyncStatus`. An empty `data` array is treated as suspicious and
  does NOT delete anything.
- Triggered once silently at startup plus a manual `同步老化架` button. The old `添加老化架` button was removed — the
  interface is now the only source of truth for the rack list.
- Shelf numbers are not returned by the interface; they are extracted from `name` (e.g. `老化架1` → `1`) and used to
  build `Mesh_ID` via `BuildMeshId`. User-entered shelf numbers / mesh IDs are never overwritten by a sync.

## Write + Verify + Bind (写入 → 自动回读校验 → 绑定点位)

- `写入` (`WriteVerifyAndBindAsync`) 是唯一的写入入口。顺序是：`CaptureBindContext()`（在写之前快照 节点 ID /
  老化架 id / 点位 id / deviceType）→ `CheckConfigOnRead()` → 确认弹窗（同时列出绑定目标）→ 发送 `ChangeConfig`
  → 等待 → `DoReadConfig()`（跑在后台线程）→ `CheckWriteOK()`。**只有回读一致才调用 `BindPointAsync(context)`**；
  不一致就中止，一次都不会调用服务端接口。旧的独立按钮 `写入并重新读取` 已删除，不要再加第二个写入按钮。
- **为什么必须快照**：`TryMatchAgingShelfFromRead()` / `SelectReadShelfItem()` 会把 `SelectedAgingShelf` 改写成
  本地匹配模板或 `ReadAgingShelfItem` 伪项（伪项 `RemoteId` 为空 → `GetSelectedRemoteRackId()` 返回 null）；
  而 `SelectedAgingShelf` 一旦变化就会重新拉取点位并清空 `SelectedAgingPoint`。因此在 `DoReadConfig()` 之后
  再读这两个属性，拿到的必然是空值 —— 这正是历史上自动绑定必失败的根源。**绑定参数一律在 `serialModel.Send` 之前捕获。**
- `ApplyReadBackShelfSelection()`：用户显式选中的是接口同步的老化架时保留其选择，否则回退到
  `TryMatchAgingShelfFromRead()` 的旧行为。这样回读不会悄悄清掉用户选好的点位。
- 失败口径：`AssignDevice` 失败不回滚、不清空已写入的配置，只更新 `ReadStatusMessage` / `PointAssignStatus`，
  让用户重选点位后再写入；文案必须区分"写入失败"与"写入成功但绑定失败"。

## Aging Point & Device Assignment (点位选择与设备分配)

- `AvailablePointList`: `POST .../DeviceServiceCallback/AvailablePointList`, body
  `{"agingRackId":<long>,"pageSize":100000,"pageIndex":1,"onlyAvailable":true}`. `onlyAvailable` 默认 `true`
  （只返回可用节点）；传 `false` 返回全部节点、跳过可用性过滤。客户端由 `AvailablePointListRequest` 组装，
  `GetAvailablePointsAsync(agingRackId, onlyAvailable = true)`；界面上的「仅显示可用节点」`CheckBox` 绑定
  `MainViewModel.OnlyAvailable`（默认勾选），切换后立即从首页重新查询。**服务端由 192.168.40.116 提供，
  是否真正支持该字段需联调确认**，服务端参考实现见 `docs/节点查询-onlyAvailable-服务端参考实现.md`。
  Items carry
  `id / pointSequence / layerNumber / agingRackId / deviceId / deviceCount`; combo label is
  `第{layerNumber}层-点位{pointSequence}` and the value is `id`.
- `AssignDevice`: `POST .../DeviceServiceCallback/AssignDevice`, body
  `{deviceId, deviceType, agingRackId, agingRackPointId}`. `deviceId` comes from `ReadUDP_ID` (node ID),
  `deviceType` is `2` when `ReadIsRoot == "1"` otherwise `1`, `agingRackId` is the selected shelf's `RemoteId`,
  `agingRackPointId` is the selected point's `Id`. `code == 1` means success.
- The point dropdown renders/enables **only** after a rack with a non-empty `RemoteId` is selected **and** the
  request succeeds. Selecting the `读取配置` pseudo-item (no `RemoteId`) clears and disables it. Failures clear it too.
- Rack switches re-fetch points; a per-request sequence number (`_pointsRequestSeq`) discards stale responses so fast
  switching cannot let an older response overwrite a newer one.
- Binding reuses the existing `IsReadBackWaiting` overlay; `WaitMessage` is rewritten per stage.
- All three endpoints share the same host, so the same certificate whitelist and `code == 1` convention apply.

## Persistent State

- Aging shelf templates are stored in `AgingShelves.json` under `AppContext.BaseDirectory`.
- These templates are kept in sync with the `RackPageList` interface; `RemoteId`/`RemoteName`/`Remark`/`MasterControlId`
  are persisted alongside the editable fields.
- Node-ID history was removed: each device ships with a factory-assigned unique ID, so the
  `NodeHistory.json` file and all related load/save/append/duplicate-check logic are gone.
- Node ID is displayed read-only in the basic config panel (it comes from the device).

## Notes for Editing

- Keep XAML command bindings consistent: use `CommandParameter="{Binding RelativeSource={RelativeSource Mode=Self}}"` so the switch can read `Button.Content`.
- The project has nullable references enabled, but many warnings are pre-existing; prefer not introducing new ones.
- `System.Text.Json` is referenced explicitly despite the `NU1510` warning; do not remove it unless also resolving the warning intentionally.
