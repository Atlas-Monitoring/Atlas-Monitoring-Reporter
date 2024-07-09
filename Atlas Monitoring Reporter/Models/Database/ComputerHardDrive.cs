using Atlas_Monitoring.Core.Models.Database;

namespace Atlas_Monitoring_Reporter.Models.Database
{
    public class ComputerHardDrive
    {
        public Guid Id { get; set; }
        public Device Device { get; set; }
        public string Letter { get; set; } = string.Empty;
        public double TotalSpace { get; set; } = 0;
        public double SpaceUse { get; set; } = 0;
        public DateTime DateAdd { get; set; } = DateTime.Now;
        public DateTime DateUpdate { get; set; } = DateTime.Now;
    }
}