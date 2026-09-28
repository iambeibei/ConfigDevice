using System;

namespace LightGateway.ViewModel
{
    /// <summary>
    /// 写入结果类型，决定结果弹窗的配色与图标。
    /// </summary>
    public enum WriteResultKind
    {
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// 「写入」链路的统一结果对象。所有终态（成功 / 部分成功 / 失败 / 用户取消）都用它描述，
    /// 由 MainViewModel 的 case "写入" 在遮罩关闭后统一弹一次窗，避免成功路径静默、失败路径多处弹窗。
    /// </summary>
    public sealed class WriteOutcome
    {
        /// <summary>结果类型，决定弹窗配色与图标。</summary>
        public WriteResultKind Kind { get; set; } = WriteResultKind.Success;

        /// <summary>弹窗标题。</summary>
        public string Title { get; set; } = "";

        /// <summary>一句话结论。</summary>
        public string Summary { get; set; } = "";

        /// <summary>失败原因 / 明细，可为空。</summary>
        public string? Detail { get; set; }

        /// <summary>是否需要弹出结果窗（用户主动取消时为 false）。</summary>
        public bool ShowDialog { get; set; } = true;

        /// <summary>
        /// 用户点「确认」时是否自动关闭串口。仅「写入成功 + 绑定成功」为 true。
        /// </summary>
        public bool CanCloseSerial { get; set; }

        /// <summary>确认按钮文案。</summary>
        public string ConfirmText => CanCloseSerial ? "确认并关闭串口" : "确认";

        public bool IsSuccess => Kind == WriteResultKind.Success;

        public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

        public static WriteOutcome Error(string title, string summary, string? detail = null) =>
            new WriteOutcome { Kind = WriteResultKind.Error, Title = title, Summary = summary, Detail = detail };

        public static WriteOutcome Warning(string title, string summary, string? detail = null) =>
            new WriteOutcome { Kind = WriteResultKind.Warning, Title = title, Summary = summary, Detail = detail };

        public static WriteOutcome OkAndCloseSerial(string title, string summary, string? detail = null) =>
            new WriteOutcome
            {
                Kind = WriteResultKind.Success,
                Title = title,
                Summary = summary,
                Detail = detail,
                CanCloseSerial = true
            };

        public static WriteOutcome NoDialog() => new WriteOutcome { ShowDialog = false };
    }
}
