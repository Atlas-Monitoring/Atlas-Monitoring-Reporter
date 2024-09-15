namespace Atlas_Monitoring_Reporter.Models.Internal
{
    public class ReporterConfiguration
    {
        public string URLApi { get; set; } = string.Empty;
        public int IntervalInSeconds { get; set; } = 300; // 5 minutes
    }
}
