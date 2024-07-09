using Atlas_Monitoring_Reporter.Models.Database;
using Atlas_Monitoring_Reporter.Models.Internal;

namespace Atlas_Monitoring.Core.Models.Database
{
    public class Device
    {
        public Guid Id { get; set; }
        public DeviceStatus DeviceStatus { get; set; } = DeviceStatus.Undefined;
        public DeviceType DeviceType { get; set; } = DeviceType.Undefined;
        public string Name { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public double MaxRam { get; set; } = 0;
        public double NumberOfLogicalProcessors { get; set; } = 0;
        public string OS { get; set; } = string.Empty;
        public string OSVersion { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public DateTime DateAdd { get; set; }
        public DateTime DateUpdated { get; set; }
    }
}
