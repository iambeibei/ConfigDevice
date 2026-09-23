using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace LightGateway.Ultils
{
    /// <summary>
    /// AvailablePointList 接口返回的单个点位。
    /// </summary>
    public class RackPointItem
    {
        public string? Id { get; set; }

        public int PointSequence { get; set; }

        public int LayerNumber { get; set; }

        public string? AgingRackId { get; set; }

        public string? DeviceId { get; set; }

        public int DeviceCount { get; set; }

        /// <summary>
        /// 点位下拉框的显示名，形如「第1层-点位2」。
        /// </summary>
        public string DisplayName => $"第{LayerNumber}层-点位{PointSequence}";
    }

    /// <summary>
    /// AvailablePointList 接口请求体。
    /// 字段命名与接口约定保持一致（camelCase），序列化后形如：
    /// { "agingRackId": 0, "pageSize": 0, "pageIndex": 0, "onlyAvailable": true }
    /// </summary>
    public class AvailablePointListRequest
    {
        /// <summary>
        /// 老化架 id（接口返回的雪花 id）。
        /// </summary>
        [JsonPropertyName("agingRackId")]
        public long AgingRackId { get; set; }

        /// <summary>
        /// 分页大小，与接口约定保持一致。
        /// </summary>
        [JsonPropertyName("pageSize")]
        public long PageSize { get; set; }

        /// <summary>
        /// 页码，从 1 开始。
        /// </summary>
        [JsonPropertyName("pageIndex")]
        public long PageIndex { get; set; }

        /// <summary>
        /// 是否只返回可用状态的节点。默认 true：不传时服务端按“只要可用”处理。
        /// false 表示返回全部节点、跳过可用性过滤。
        /// </summary>
        [JsonPropertyName("onlyAvailable")]
        public bool OnlyAvailable { get; set; } = true;
    }

    /// <summary>
    /// AvailablePointList 接口返回结构。
    /// </summary>
    public class AvailablePointListResponse
    {
        public int Total { get; set; }

        public List<RackPointItem>? Data { get; set; }

        public int Code { get; set; }

        public string? Message { get; set; }
    }

    /// <summary>
    /// AssignDevice 接口返回结构，data 为字符串。
    /// </summary>
    public class AssignDeviceResponse
    {
        public int Code { get; set; }

        public string? Message { get; set; }

        public string? Data { get; set; }
    }

    /// <summary>
    /// 老化架列表接口返回的单个老化架。
    /// </summary>
    public class RackItem
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Remark { get; set; }

        public string? MasterControlId { get; set; }
    }

    /// <summary>
    /// RackPageList 接口返回结构。
    /// {
    ///   "total": 1,
    ///   "data": [ { "id": "...", "name": "...", "remark": "", "masterControlId": "..." } ],
    ///   "code": 1,
    ///   "message": "操作成功"
    /// }
    /// </summary>
    public class RackPageListResponse
    {
        public int Total { get; set; }

        public List<RackItem>? Data { get; set; }

        public int Code { get; set; }

        public string? Message { get; set; }
    }

    /// <summary>
    /// 老化架接口调用异常。调用方据此区分“请求层失败”与“业务层失败”并提示用户。
    /// </summary>
    public class RackApiException : Exception
    {
        public RackApiException(string message) : base(message)
        {
        }

        public RackApiException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// 老化架列表接口客户端。
    /// 接口为内网 HTTPS 且使用自签名证书，因此仅对该内网地址跳过证书校验，其它地址保持严格校验。
    /// </summary>
    public sealed class RackApiClient
    {
        public const string RackPageListUrl = "https://192.168.40.116:7088/DeviceServiceCallback/RackPageList";

        public const string AvailablePointListUrl = "https://192.168.40.116:7088/DeviceServiceCallback/AvailablePointList";

        public const string AssignDeviceUrl = "https://192.168.40.116:7088/DeviceServiceCallback/AssignDevice";

        /// <summary>
        /// 点位列表页大小，与接口约定保持一致。
        /// </summary>
        public const long PointPageSize = 100000;

        /// <summary>
        /// 点位列表默认页码（首页）。
        /// </summary>
        public const long DefaultPageIndex = 1;

        /// <summary>
        /// 点位列表默认是否只取可用节点。
        /// </summary>
        public const bool DefaultOnlyAvailable = true;

        /// <summary>
        /// 接口约定的成功状态码（该接口为 1，不是 0 / 200）。
        /// </summary>
        public const int SuccessCode = 1;

        private const string RequestBody = "{\"pageSize\":10000000,\"pageIndex\":1}";

        /// <summary>
        /// 证书校验白名单：仅这些主机允许自签名证书。
        /// </summary>
        private static readonly HashSet<string> TrustedHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "192.168.40.116"
        };

        /// <summary>
        /// 共享 HttpClient，避免频繁创建导致端口耗尽。
        /// </summary>
        private static readonly Lazy<HttpClient> SharedClient = new(CreateClient);

        /// <summary>
        /// 默认超时时间。
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
                    errors == System.Net.Security.SslPolicyErrors.None ||
                    (request?.RequestUri != null && TrustedHosts.Contains(request.RequestUri.Host))
            };

            // 单次调用的超时由 CancellationTokenSource 精确控制，这里只作为兜底硬上限。
            return new HttpClient(handler)
            {
                Timeout = DefaultTimeout + TimeSpan.FromSeconds(20)
            };
        }

        /// <summary>
        /// 拉取老化架列表。
        /// </summary>
        /// <param name="timeout">超时时间，默认 10 秒；超时抛出 TimeoutException。</param>
        /// <param name="cancellationToken">外部取消令牌。</param>
        /// <returns>老化架列表；接口 data 为空时返回空集合。</returns>
        public async Task<List<RackItem>> GetRacksAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            TimeSpan actualTimeout = timeout ?? DefaultTimeout;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(actualTimeout);

            using var content = new StringContent(RequestBody, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await SharedClient.Value.PostAsync(RackPageListUrl, content, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"调用老化架列表接口超时（{actualTimeout.TotalSeconds:0} 秒）。");
            }
            catch (HttpRequestException ex)
            {
                throw new RackApiException($"无法连接老化架服务：{ex.Message}", ex);
            }
            catch (Exception ex) when (ex is not RackApiException)
            {
                throw new RackApiException($"调用老化架列表接口失败：{ex.Message}", ex);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new RackApiException($"老化架列表接口返回 HTTP {(int)response.StatusCode}（{response.StatusCode}）。");
                }

                string responseText;
                try
                {
                    responseText = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"读取老化架列表响应超时（{actualTimeout.TotalSeconds:0} 秒）。");
                }

                if (string.IsNullOrWhiteSpace(responseText))
                {
                    throw new RackApiException("老化架列表接口返回内容为空。");
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                };

                RackPageListResponse? result;
                try
                {
                    result = JsonSerializer.Deserialize<RackPageListResponse>(responseText, options);
                }
                catch (JsonException ex)
                {
                    throw new RackApiException($"老化架列表接口返回数据解析失败：{ex.Message}", ex);
                }

                if (result == null)
                {
                    throw new RackApiException("老化架列表接口返回数据解析失败。");
                }

                if (result.Code != SuccessCode)
                {
                    string message = string.IsNullOrWhiteSpace(result.Message) ? "未知错误" : result.Message;
                    throw new RackApiException($"老化架列表接口返回失败（code={result.Code}）：{message}");
                }

                return result.Data ?? new List<RackItem>();
            }
        }

        /// <summary>
        /// 拉取指定老化架下的点位列表。
        /// </summary>
        /// <param name="agingRackId">老化架 id（接口返回的雪花 id）。</param>
        /// <param name="onlyAvailable">是否只返回可用状态的节点，默认 true；false 表示返回全部节点、跳过可用性过滤。</param>
        public async Task<List<RackPointItem>> GetAvailablePointsAsync(
            long agingRackId,
            bool onlyAvailable = DefaultOnlyAvailable,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            // 分页与老化架过滤逻辑保持原样，仅追加 onlyAvailable 条件；每次查询都从首页开始。
            var request = new AvailablePointListRequest
            {
                AgingRackId = agingRackId,
                PageSize = PointPageSize,
                PageIndex = DefaultPageIndex,
                OnlyAvailable = onlyAvailable
            };

            string body = JsonSerializer.Serialize(request);

            var result = await PostJsonAsync<AvailablePointListResponse>(AvailablePointListUrl, body, "点位列表", timeout, cancellationToken);

            if (result.Code != SuccessCode)
            {
                throw new RackApiException($"点位列表接口返回失败（code={result.Code}）：{FormatMessage(result.Message)}");
            }

            return result.Data ?? new List<RackPointItem>();
        }

        /// <summary>
        /// 把设备分配到指定点位。
        /// </summary>
        /// <returns>是否成功（code 为 1）以及服务端返回的 message。</returns>
        public async Task<(bool success, string message)> AssignDeviceAsync(
            long deviceId,
            int deviceType,
            long agingRackId,
            long agingRackPointId,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            string body = JsonSerializer.Serialize(new
            {
                deviceId,
                deviceType,
                agingRackId,
                agingRackPointId
            });

            AssignDeviceResponse result;
            try
            {
                result = await PostJsonAsync<AssignDeviceResponse>(AssignDeviceUrl, body, "设备分配", timeout, cancellationToken);
            }
            catch (RackApiException ex)
            {
                // 分配接口在业务失败时也可能返回非 2xx，统一转成失败结果由调用方提示。
                return (false, ex.Message);
            }

            if (result.Code != SuccessCode)
            {
                return (false, FormatMessage(result.Message));
            }

            return (true, string.IsNullOrWhiteSpace(result.Message) ? "操作成功" : result.Message);
        }

        private static string FormatMessage(string? message)
        {
            return string.IsNullOrWhiteSpace(message) ? "未知错误" : message;
        }

        /// <summary>
        /// 通用 POST JSON 请求：处理超时、HTTP 状态码与反序列化，返回已解析的响应对象。
        /// </summary>
        private async Task<T> PostJsonAsync<T>(string url, string jsonBody, string apiName, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            TimeSpan actualTimeout = timeout ?? DefaultTimeout;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(actualTimeout);

            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await SharedClient.Value.PostAsync(url, content, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"调用{apiName}接口超时（{actualTimeout.TotalSeconds:0} 秒）。");
            }
            catch (HttpRequestException ex)
            {
                throw new RackApiException($"无法连接{apiName}服务：{ex.Message}", ex);
            }
            catch (Exception ex) when (ex is not RackApiException)
            {
                throw new RackApiException($"调用{apiName}接口失败：{ex.Message}", ex);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new RackApiException($"{apiName}接口返回 HTTP {(int)response.StatusCode}（{response.StatusCode}）。");
                }

                string responseText;
                try
                {
                    responseText = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"读取{apiName}接口响应超时（{actualTimeout.TotalSeconds:0} 秒）。");
                }

                if (string.IsNullOrWhiteSpace(responseText))
                {
                    throw new RackApiException($"{apiName}接口返回内容为空。");
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                };

                try
                {
                    T? result = JsonSerializer.Deserialize<T>(responseText, options);
                    if (result == null)
                    {
                        throw new RackApiException($"{apiName}接口返回数据解析失败。");
                    }

                    return result;
                }
                catch (JsonException ex)
                {
                    throw new RackApiException($"{apiName}接口返回数据解析失败：{ex.Message}", ex);
                }
            }
        }
    }
}
