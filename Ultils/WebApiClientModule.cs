




using PropertyChanged;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace WebApiClientModule
{
    [AddINotifyPropertyChangedInterface]

        /// <summary>
        /// WebAPI 返回结构
        /// 对应：
        /// {
        ///   "success": true,
        ///   "data": "...json字符串...",
        ///   "message": "获取json成功"
        /// }
        /// </summary>
        public class ApiResponse<T>
        {
            public bool Success { get; set; }

            public T Data { get; set; }

            public string Message { get; set; }
        }

        /// <summary>
        /// 协议 JSON 接口调用模块
        /// </summary>
        public class ProtocolJsonApiClient
        {
            private readonly HttpClient _httpClient;

            /// <summary>
            /// 例如 baseUrl = "http://127.0.0.1:5000"
            /// </summary>
            public ProtocolJsonApiClient(string baseUrl)
            {
                _httpClient = new HttpClient
                {
                    BaseAddress = new Uri(baseUrl)
                };
            }

            /// <summary>
            /// 调用 WebAPI 获取协议 JSON
            /// </summary>
            /// <param name="id">协议 ID</param>
            /// <returns>协议 JSON 字符串</returns>
            public async Task<string> GetJsonAsync(long id)
            {
                // 这里根据你的实际路由修改
                // 如果你的控制器路由是 api/Protocol/GetJson，则写：
                // string url = $"/api/Protocol/GetJson?ID={id}";

                string url = $"http://10.16.160.51:5000/Login/GetJson?ID={id}";

                HttpResponseMessage response = await _httpClient.PostAsync(url, null);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"请求失败，HTTP状态码：{response.StatusCode}");
                }

                string responseText = await response.Content.ReadAsStringAsync();

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                ApiResponse<string> apiResult = JsonSerializer.Deserialize<ApiResponse<string>>(responseText, options);

                if (apiResult == null)
                {
                    throw new Exception("接口返回数据解析失败");
                }

                if (!apiResult.Success)
                {
                    throw new Exception($"获取 JSON 失败：{apiResult.Message}");
                }

                return apiResult.Data;
            }
        }
    }
