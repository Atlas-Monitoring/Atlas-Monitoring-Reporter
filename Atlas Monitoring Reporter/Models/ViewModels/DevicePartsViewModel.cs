namespace Atlas_Monitoring_Reporter.Models.ViewModels
{
    public class DevicePartsReadViewModel
    {
        public Guid Id { get; set; }
        public Guid DeviceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Labels { get; set; } = string.Empty;
    }

    public class DevicePartsWriteViewModel
    {
        public Guid Id { get; set; }
        public Guid DeviceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Labels { get; set; } = string.Empty;
    }
}
