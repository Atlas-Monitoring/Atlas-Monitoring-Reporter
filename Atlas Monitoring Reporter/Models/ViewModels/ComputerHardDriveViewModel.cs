namespace Atlas_Monitoring_Reporter.Models.ViewModels
{
    public class ComputerHardDriveViewModel
    {
        public Guid Id { get; set; }
        public Guid ComputerId { get; set; }
        public string Letter { get; set; } = string.Empty;
        public double TotalSpace { get; set; } = 0;
        public double SpaceUse { get; set; } = 0;
    }
}
