namespace Atlas_Monitoring_Reporter.Models.Database
{
    public class DeviceType
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public static DeviceType Undefined = new DeviceType { Id = 1, Name = "Undefined" };
        public static DeviceType Computer = new DeviceType { Id = 2, Name = "Computer" };
        public static DeviceType Server = new DeviceType { Id = 3, Name = "Server" };
        public static DeviceType Printer = new DeviceType { Id = 4, Name = "Printer" };
        public static DeviceType Router = new DeviceType { Id = 5, Name = "Router" };
        public static DeviceType Switch = new DeviceType { Id = 6, Name = "Switch" };
        public static DeviceType Phone = new DeviceType { Id = 7, Name = "Phone" };
        public static DeviceType MobilePhone = new DeviceType { Id = 8, Name = "MobilePhone" };
        public static DeviceType Other = new DeviceType { Id = 9, Name = "Other" };
    }
}
