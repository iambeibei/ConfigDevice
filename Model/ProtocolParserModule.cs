



using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LightGateway.Model
{
    /// <summary>
    /// 协议根对象
    /// </summary>
    public class DeviceProtocol
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("uuid")]
        public string Uuid { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("protocolType")]
        public int ProtocolType { get; set; }

        [JsonPropertyName("protocolTypeStr")]
        public string ProtocolTypeStr { get; set; }

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("contents")]
        public List<CommandContent> Contents { get; set; } = new();
    }

    /// <summary>
    /// JSON 中 contents 数组里的单条命令
    /// </summary>
    public class CommandContent
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("名称")]
        public string Name { get; set; }

        [JsonPropertyName("读写状态")]
        public string ReadWriteState { get; set; }

        [JsonPropertyName("命令模板")]
        public string CommandTemplate { get; set; }

        [JsonPropertyName("命令参数")]
        public JsonElement? CommandParameters { get; set; }

        [JsonPropertyName("响应模板")]
        public string ResponseTemplate { get; set; }

        [JsonPropertyName("响应参数")]
        public JsonElement? ResponseParameters { get; set; }
    }

    /// <summary>
    /// 解析后统一使用的命令对象
    /// </summary>
    public class ProtocolCommand
    {
        public string Id { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// R = 读命令，W = 写命令
        /// </summary>
        public string ReadWriteState { get; set; }

        public string CommandTemplate { get; set; }

        public string ResponseTemplate { get; set; }

        public string CommandParameterJson { get; set; }

        public string ResponseParameterJson { get; set; }

        public override string ToString()
        {
            return $"{Name} [{ReadWriteState}] => {CommandTemplate}";
        }
    }

    /// <summary>
    /// 最终解析结果
    /// </summary>
    public class ProtocolParseResult
    {
        public string DeviceName { get; set; }

        public string ProtocolType { get; set; }

        public ProtocolCommand[] ReadCommands { get; set; }

        public ProtocolCommand[] WriteCommands { get; set; }

        public ProtocolCommand[] AllCommands { get; set; }

        public string[] ReadCommandListName { get; set; }
        public string[] WiteCommandListName {  get; set; }
    }

    /// <summary>
    /// 协议解析功能模块
    /// </summary>
    public static class ProtocolCommandParser
    {
        private static readonly Regex PlaceholderRegex = new Regex("<[^>]+>", RegexOptions.Compiled);

        /// <summary>
        /// 解析协议 JSON，并将读命令和写命令分开
        /// </summary>
        public static ProtocolParseResult Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("JSON 数据不能为空");
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            DeviceProtocol protocol = JsonSerializer.Deserialize<DeviceProtocol>(json, options);

            if (protocol == null)
            {
                throw new InvalidOperationException("协议 JSON 解析失败");
            }

            var allCommands = protocol.Contents
                //.Where(x => !string.IsNullOrWhiteSpace(x.CommandTemplate))
                .Select(ConvertToProtocolCommand)
                .ToArray();

            var readCommands = allCommands
                .Where(x => string.Equals(x.ReadWriteState, "R", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var writeCommands = allCommands
                .Where(x => string.Equals(x.ReadWriteState, "W", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var readCommandListName= readCommands.Select(x => x.Name).ToArray();
            var writeCommandListName= writeCommands.Select(x => x.Name).ToArray();

            return new ProtocolParseResult
            {
                DeviceName = protocol.Name,
                ProtocolType = protocol.ProtocolTypeStr,
                AllCommands = allCommands,
                ReadCommands = readCommands,
                WriteCommands = writeCommands,
                ReadCommandListName = readCommandListName,
                WiteCommandListName=writeCommandListName
            };
        }

        /// <summary>
        /// 根据命令名称查找命令
        /// </summary>
        public static ProtocolCommand FindByName(ProtocolCommand[] commands, string name)
        {
            if (commands == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return commands.FirstOrDefault(x => x.Name == name);
        }

        /// <summary>
        /// 根据命令模板生成实际 SCPI 命令
        /// 例如：
        /// 模板：INPut <NRf>
        /// 参数：1
        /// 输出：INPut 1
        /// </summary>
        public static string BuildCommand(ProtocolCommand command, params object[] values)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            return BuildCommand(command.CommandTemplate, values);
        }

        /// <summary>
        /// 根据模板字符串生成实际命令
        /// </summary>
        public static string BuildCommand(string commandTemplate, params object[] values)
        {
            if (string.IsNullOrWhiteSpace(commandTemplate))
            {
                throw new ArgumentException("命令模板不能为空");
            }

            if (values == null || values.Length == 0)
            {
                return commandTemplate;
            }

            int index = 0;

            string result = PlaceholderRegex.Replace(commandTemplate, match =>
            {
                if (index >= values.Length)
                {
                    return match.Value;
                }

                object value = values[index++];
                return Convert.ToString(value);
            });

            return result;
        }

        private static ProtocolCommand ConvertToProtocolCommand(CommandContent item)
        {
            return new ProtocolCommand
            {
                Id = item.Id,
                Name = item.Name,
                ReadWriteState = item.ReadWriteState,
                CommandTemplate = item.CommandTemplate,
                ResponseTemplate = item.ResponseTemplate,
                CommandParameterJson = GetJsonText(item.CommandParameters),
                ResponseParameterJson = GetJsonText(item.ResponseParameters)
            };
        }

        private static string GetJsonText(JsonElement? element)
        {
            if (!element.HasValue)
            {
                return null;
            }

            if (element.Value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return element.Value.GetRawText();
        }
    }
}