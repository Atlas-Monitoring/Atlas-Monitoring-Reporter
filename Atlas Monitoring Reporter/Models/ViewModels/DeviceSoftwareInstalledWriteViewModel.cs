namespace Atlas_Monitoring_Reporter.Models.ViewModels
{
    public class DeviceSoftwareInstalledWriteViewModel
    {
        public Guid DeviceId { get; set; }
        public string AppName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
    }
}
