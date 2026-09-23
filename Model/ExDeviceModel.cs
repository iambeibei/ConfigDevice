using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;


namespace LightGateway.Model
{
    
    public class SampleData
    {
        public string Value { get; set; }
    }
    public class PreAction
    {
        public string ParaName { get; set; }
        public string ParaValue { get; set; }

    }

    public class AfterAction
    {
        public string ParaName { get; set; }
        public string ParaValue { get; set; }
    }

    public class Action
    {
        public ObservableCollection<PreAction> PreAction { get; set; }= new ObservableCollection<PreAction>();
        public ObservableCollection<AfterAction> AfterAction { get; set; }=new ObservableCollection<AfterAction>();
    }
    public class AgingFunction
    {
        public string Method { get; set; }
        public ObservableCollection<SampleData> SampleData { get; set; }
        public Action Action { get; set; }
    }

    public class AgingSteps
    {
        public ObservableCollection<AgingFunction> AgingFunctionList { get; set; } = new ObservableCollection<AgingFunction>
        {
            new AgingFunction{Method="Standing",Action=new Action(),SampleData=new ObservableCollection<SampleData>()},
            new AgingFunction{Method="Discharge",Action=new Action(),SampleData=new ObservableCollection<SampleData>()},
            new AgingFunction{Method="Recharge",Action=new Action(),SampleData=new ObservableCollection<SampleData>()}
        };
    }
    public class ExDeviceModel
    {
        public string Name { get; set; } = "NoName";
        public int ProtoID { get; set; } = 0;
        public string[] Communications { get; set; } = new string[] { "UART", "CAN", "RS485", "RS232", "Bluetooth", "Unselected" };
        public string Communication { get; set; } = "Unselected";
        public int Port { get; set; } = 0;
        public string[] Protos { get; set; } = new string[] { "MODBUS", "SCPI", "Unselected" };

        public string Proto { get; set; } = "Unselected";

        public AgingSteps AgingSteps { get; set; } = new AgingSteps();

    }

    /// <summary>
    /// ESP32 回读的外接设备配置。该模型与可编辑配置分离，避免读取时覆盖待写入数据。
    /// </summary>
    public class ReadExternalDeviceModel
    {
        public string Name { get; set; } = "";
        public long ProtoID { get; set; }
        public string Communication { get; set; } = "";
        public int Port { get; set; }
        public string Proto { get; set; } = "";
        public ReadAgingSteps AgingSteps { get; set; } = new ReadAgingSteps();
    }

    public class ReadAgingSteps
    {
        public ObservableCollection<ReadAgingFunction> AgingFunctionList { get; set; } = new();
    }

    public class ReadAgingFunction
    {
        public string Method { get; set; } = "";
        public ObservableCollection<SampleData> SampleData { get; set; } = new();
        public ReadAction Action { get; set; } = new ReadAction();
    }

    public class ReadAction
    {
        public ObservableCollection<PreAction> PreAction { get; set; } = new();
        public ObservableCollection<AfterAction> AfterAction { get; set; } = new();
    }
}
