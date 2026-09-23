# `onlyAvailable` 字段改造：服务端参考实现

> **重要前提**：本仓库（`LightGateway`）是 **WPF 客户端**，只有调用方代码。
> `https://192.168.40.116:7088/DeviceServiceCallback/*` 的服务端代码**不在本仓库**，无法直接修改。
> 下面的内容是按 ASP.NET Core + EF Core 常见写法给出的**参考实现**，需要放到服务端仓库中。
> 其中「可用状态的判定条件」必须替换为你们真实的字段/枚举，不能照抄。

---

## 1. 请求模型（DTO）

缺省行为的关键：`bool` 的 CLR 默认值是 `false`，而需求要求"未传时按 `true` 处理"。
所以要么用 `bool?` 在绑定后兜底，要么给 `bool` 属性显式初始化为 `true`。**推荐 `bool?`**，
因为它能区分"没传"和"显式传 false"，语义最清晰。

```csharp
/// <summary>
/// 节点（点位）查询请求。
/// 示例：{ "agingRackId": 0, "pageSize": 0, "pageIndex": 0, "onlyAvailable": true }
/// </summary>
public sealed class AvailablePointListRequest
{
    /// <summary>老化架 id。必填，必须 &gt; 0。</summary>
    public long AgingRackId { get; set; }

    /// <summary>分页大小。保持原逻辑，不做校验性改动。</summary>
    public long PageSize { get; set; }

    /// <summary>页码，从 1 开始。保持原逻辑。</summary>
    public long PageIndex { get; set; }

    /// <summary>
    /// 是否只返回"可用"状态的节点。缺省（null）时按 true 处理。
    /// true  = 只返回可用节点；false = 返回全部节点、跳过可用性过滤。
    /// </summary>
    public bool? OnlyAvailable { get; set; }

    /// <summary>对外暴露的确定值：未传参数时返回 true。</summary>
    public bool OnlyAvailableResolved => OnlyAvailable ?? true;
}
```

## 2. 控制器（接口）签名

```csharp
[HttpPost]
[Route("DeviceServiceCallback/AvailablePointList")]
public async Task<IActionResult> AvailablePointList([FromBody] AvailablePointListRequest request)
{
    // --- 参数校验（只追加，不改变原有的 agingRackId / 分页逻辑） ---
    if (request == null)
    {
        return Ok(new { code = 0, message = "请求体不能为空", data = Array.Empty<object>(), total = 0 });
    }

    if (request.AgingRackId <= 0)
    {
        return Ok(new { code = 0, message = "agingRackId 无效", data = Array.Empty<object>(), total = 0 });
    }

    // 兼容原客户端未传分页的情况，保持原有兜底行为（不要在这里改成必填）。
    long pageSize = request.PageSize > 0 ? request.PageSize : 100000;
    long pageIndex = request.PageIndex > 0 ? request.PageIndex : 1;

    var (items, total) = await _pointService.QueryAsync(
        request.AgingRackId, pageSize, pageIndex, request.OnlyAvailableResolved);

    return Ok(new { code = 1, message = "操作成功", data = items, total });
}
```

## 3. 查询过滤实现（显式分支 + 可用状态判定）

**把下面 `IsAvailable(p)` 里的判定条件替换成你们真实的字段。** 常见两种形态，二选一或组合使用：

- 形态 A：以占用设备数判定 —— 未绑定设备（或 `deviceId` 为空）即为可用；
- 形态 B：以状态字段判定 —— 如 `Status == 1`（1 = 空闲/可用）。

```csharp
public async Task<(List<PointDto> Items, int Total)> QueryAsync(
    long agingRackId, long pageSize, long pageIndex, bool onlyAvailable)
{
    // 原有过滤条件保持不变：只按老化架过滤。
    IQueryable<RackPoint> query = _db.RackPoints.AsNoTracking()
        .Where(p => p.AgingRackId == agingRackId);

    // 新增：可用性过滤。显式写出分支，不依赖任何隐式条件。
    if (onlyAvailable)
    {
        // 可用状态的判定条件（示例，替换成真实字段）：
        //   点位未被设备占用（deviceId 为空 / 已绑定设备数为 0）且状态为可用。
        query = query.Where(p => IsAvailableExpression(p));
    }
    // onlyAvailable == false：跳过可用性过滤，返回全部节点。

    int total = await query.CountAsync();

    List<PointDto> items = await query
        .OrderBy(p => p.LayerNumber).ThenBy(p => p.PointSequence)   // 保持原有排序
        .Skip((int)((pageIndex - 1) * pageSize))
        .Take((int)pageSize)
        .Select(p => new PointDto
        {
            Id = p.Id,
            PointSequence = p.PointSequence,
            LayerNumber = p.LayerNumber,
            AgingRackId = p.AgingRackId,
            DeviceId = p.DeviceId,
            DeviceCount = p.DeviceCount
        })
        .ToListAsync();

    return (items, total);
}

/// <summary>
/// 可用状态判定（表达式形式，可翻译成 SQL）。
/// ★ 请替换为你们真实的字段与取值。
/// </summary>
private static bool IsAvailableExpression(RackPoint p)
{
    // 形态 A：未被占用
    bool notOccupied = p.DeviceCount == 0 || p.DeviceId == null || p.DeviceId == "";

    // 形态 B：状态可用（示例：Status == 1 表示可用；没有该字段就删掉这一行）
    // bool statusOk = p.Status == 1;

    return notOccupied;
}
```

## 4. 兼容性检查清单

- ✅ `agingRackId` 过滤逻辑未改动；
- ✅ 分页（`pageSize` / `pageIndex`）逻辑与兜底行为未改动；
- ✅ 响应结构（`total / data / code / message`）未改动，`code == 1` 仍是成功；
- ✅ 老客户端不传 `onlyAvailable` → 服务端解析为 `true` → **返回结果与改造前完全一致**；
- ✅ 排序方式保持不变（客户端也会再排序一次，无副作用）。

---

## 5. 客户端（本仓库）已完成的改动

| 文件 | 改动 |
| --- | --- |
| `Ultils/RackApiClient.cs` | 新增 `AvailablePointListRequest`（`[JsonPropertyName]` 固定为 `agingRackId / pageSize / pageIndex / onlyAvailable`）；新增常量 `DefaultPageIndex`、`DefaultOnlyAvailable`；`GetAvailablePointsAsync(long agingRackId, bool onlyAvailable = true, ...)` 改为用强类型 DTO 组装请求体 |
| `ViewModel/MainViewModel.cs` | 新增 `OnlyAvailable` 属性（默认 `true`）；新增 `OnOnlyAvailableChanged()`（切换后立即重新查询且回到首页）；`LoadAvailablePointsAsync(string rackId, bool onlyAvailable = true)` 透传；绑定成功后的重拉沿用当前开关；状态文案区分"可用点位 / 节点（含不可用）" |
| `View/MainWindow.xaml` | 点位下拉框右侧新增「仅显示可用节点」`CheckBox`，双向绑定 `OnlyAvailable`，默认选中 |

**待服务端上线后验证**：取消勾选时确实能拿到被占用/不可用的节点；勾选时结果与改造前一致。
