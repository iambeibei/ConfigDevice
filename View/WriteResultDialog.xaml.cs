using System.Windows;
using System.Windows.Media;
using LightGateway.ViewModel;

namespace LightGateway.View
{
    /// <summary>
    /// 写入结果反馈窗口。成功 / 部分成功 / 失败都由它统一呈现，
    /// 用户点主按钮返回 true（触发后续动作，如关闭串口），从标题栏关闭返回 false。
    /// </summary>
    public partial class WriteResultDialog : Window
    {
        public WriteResultDialog(WriteOutcome outcome)
        {
            InitializeComponent();

            AccentBrush = outcome.Kind switch
            {
                WriteResultKind.Success => new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),
                WriteResultKind.Warning => new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
                _ => new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
            };
            AccentTextBrush = outcome.Kind switch
            {
                WriteResultKind.Success => new SolidColorBrush(Color.FromRgb(0x14, 0x66, 0x2E)),
                WriteResultKind.Warning => new SolidColorBrush(Color.FromRgb(0x9A, 0x34, 0x12)),
                _ => new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B))
            };

            DataContext = new WriteResultViewData
            {
                Title = outcome.Title,
                Summary = outcome.Summary,
                Detail = outcome.Detail ?? "",
                ConfirmText = outcome.ConfirmText,
                AccentBrush = AccentBrush,
                AccentTextBrush = AccentTextBrush,
                IsSuccess = outcome.Kind == WriteResultKind.Success,
                IsWarning = outcome.Kind == WriteResultKind.Warning,
                IsError = outcome.Kind == WriteResultKind.Error
            };
        }

        public Brush AccentBrush { get; }
        public Brush AccentTextBrush { get; }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 弹窗数据。失败原因放在 Detail 里，避免主结论被长文本淹没。
        /// </summary>
        public sealed class WriteResultViewData
        {
            public string Title { get; set; } = "";
            public string Summary { get; set; } = "";
            public string Detail { get; set; } = "";
            public string ConfirmText { get; set; } = "确认";
            public Brush AccentBrush { get; set; } = Brushes.Gray;
            public Brush AccentTextBrush { get; set; } = Brushes.Black;
            public bool IsSuccess { get; set; }
            public bool IsWarning { get; set; }
            public bool IsError { get; set; }
            public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
        }
    }
}
