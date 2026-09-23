using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LightGateway.Model
{
    /// <summary>
    /// 老化架模板。老化架内的 WiFi / Mesh / 通道 / 老化架号会映射到基础配置中对应的字段。
    /// </summary>
    public class AgingShelfModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _id = System.Guid.NewGuid().ToString("N");
        private string _shelfNumber = "";
        private string _ssid = "";
        private string _wifiPs = "";
        private string _ssid1 = "";
        private string _wifiPs1 = "";
        private string _channel = "";
        private string _meshId = "";
        private string _meshPs = "";
        private bool _isReadConfigItem;
        private string _customDisplayName = "";
        private string _remoteId = "";
        private string _remoteName = "";
        private string _remark = "";
        private string _masterControlId = "";

        /// <summary>
        /// 唯一标识。
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        /// <summary>
        /// 服务端（老化架列表接口）返回的老化架 id，作为同步的唯一键。本地手工创建的项为空。
        /// </summary>
        public string RemoteId
        {
            get => _remoteId;
            set => SetProperty(ref _remoteId, value);
        }

        /// <summary>
        /// 服务端返回的老化架名称（name 字段）。
        /// </summary>
        public string RemoteName
        {
            get => _remoteName;
            set => SetProperty(ref _remoteName, value);
        }

        /// <summary>
        /// 服务端返回的备注（remark 字段）。
        /// </summary>
        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }

        /// <summary>
        /// 服务端返回的主控编号（masterControlId 字段）。
        /// </summary>
        public string MasterControlId
        {
            get => _masterControlId;
            set => SetProperty(ref _masterControlId, value);
        }

        /// <summary>
        /// 是否为由老化架列表接口同步生成的项。
        /// </summary>
        public bool IsRemoteSynced => !string.IsNullOrWhiteSpace(_remoteId);

        /// <summary>
        /// 老化架号，对应设备端的 AgingNumber。
        /// </summary>
        public string ShelfNumber
        {
            get => _shelfNumber;
            set
            {
                if (SetProperty(ref _shelfNumber, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        /// <summary>
        /// 标识该项是否为由设备读取生成的临时配置（不持久化到本地模板）。
        /// </summary>
        public bool IsReadConfigItem
        {
            get => _isReadConfigItem;
            set => SetProperty(ref _isReadConfigItem, value);
        }

        /// <summary>
        /// 自定义显示名称。若为空，则使用 ShelfNumber 生成默认名称。
        /// </summary>
        public string CustomDisplayName
        {
            get => _customDisplayName;
            set
            {
                if (SetProperty(ref _customDisplayName, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        /// <summary>
        /// 主 Wi-Fi 名称。
        /// </summary>
        public string SSID
        {
            get => _ssid;
            set => SetProperty(ref _ssid, value);
        }

        /// <summary>
        /// 主 Wi-Fi 密码。
        /// </summary>
        public string WIFI_PS
        {
            get => _wifiPs;
            set => SetProperty(ref _wifiPs, value);
        }

        /// <summary>
        /// 备用 Wi-Fi 名称。
        /// </summary>
        public string SSID1
        {
            get => _ssid1;
            set => SetProperty(ref _ssid1, value);
        }

        /// <summary>
        /// 备用 Wi-Fi 密码。
        /// </summary>
        public string WIFI_PS1
        {
            get => _wifiPs1;
            set => SetProperty(ref _wifiPs1, value);
        }

        /// <summary>
        /// 通道，对应设备端字段 Chanel（历史拼写）。
        /// </summary>
        public string Channel
        {
            get => _channel;
            set => SetProperty(ref _channel, value);
        }

        /// <summary>
        /// Mesh ID。
        /// </summary>
        public string Mesh_ID
        {
            get => _meshId;
            set => SetProperty(ref _meshId, value);
        }

        /// <summary>
        /// Mesh 密码。
        /// </summary>
        public string Mesh_PS
        {
            get => _meshPs;
            set => SetProperty(ref _meshPs, value);
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_customDisplayName))
                {
                    return _customDisplayName;
                }
                return string.IsNullOrWhiteSpace(_shelfNumber) ? "未命名老化架" : $"老化架 {_shelfNumber}";
            }
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
