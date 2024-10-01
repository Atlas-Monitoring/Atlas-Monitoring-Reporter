using Atlas_Monitoring_Reporter.Models.ViewModels;
using Microsoft.Win32;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Atlas_Monitoring_Reporter
{
    public class StaticInformationComputer
    {
        #region Properties
        private readonly ILogger<StaticInformationComputer> _logger;
        #endregion

        #region Constructor
        public StaticInformationComputer(ILogger<StaticInformationComputer> logger)
        {
            _logger = logger;
        }
        #endregion

        #region Public Methods
        public ComputerWriteViewModel GetStaticInformationOfComputer()
        {
            ComputerWriteViewModel computerViewModel = new();

            /////
            ///Computer View Model
            ///

            //Get max ram 
            computerViewModel.MaxRam = GetDataDoubleFromObjectQuery("TotalVisibleMemorySize", "Win32_OperatingSystem");
            computerViewModel.MaxRam = computerViewModel.MaxRam > 0 ? computerViewModel.MaxRam / 1000000 : 0;

            //Get OS
            computerViewModel.OS = GetDataStringFromObjectQuery("Caption", "Win32_OperatingSystem");

            //Get Name of computer
            computerViewModel.Name = GetDataStringFromObjectQuery("Name", "Win32_computersystem");

            //Get Domain of computer
            computerViewModel.Domain = GetDataStringFromObjectQuery("Domain", "Win32_computersystem");

            //Get NumberOfLogicalProcessors of computer
            computerViewModel.NumberOfLogicalProcessors = GetDataDoubleFromObjectQuery("NumberOfLogicalProcessors", "Win32_computersystem");

            //Get Model of computer
            computerViewModel.Model = GetDataStringFromObjectQuery("Model", "Win32_computersystem");

            //Get Manufacturer of computer
            computerViewModel.Manufacturer = GetDataStringFromObjectQuery("Manufacturer", "Win32_computersystem");

            //Get OS Version 
            computerViewModel.OSVersion = $"{Environment.OSVersion.Version} ({Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "displayVersion", "Undefined").ToString()})";

            //Get Username
            computerViewModel.UserName = GetDataStringFromObjectQuery("UserName", "Win32_computersystem");

            //Get PhysicalIp
            computerViewModel.Ip = GetPhysicalIPAdress();

            //Get Serial Number
            computerViewModel.SerialNumber = GetDataStringFromObjectQuery("SerialNumber", "Win32_BIOS");
            if (computerViewModel.SerialNumber == "Default string" || computerViewModel.SerialNumber == string.Empty)
            {
                computerViewModel.SerialNumber = GetDataStringFromObjectQuery("SerialNumber", "Win32_BaseBoard");
            }

            /////
            ///Computer Data View Model
            ///
            computerViewModel.ComputerLastData = GetComputerDataViewModel();

            /////
            ///Computer HardDrive View Model
            ///
            computerViewModel.ComputerHardDrives = GetListOfHardDrive();

            return computerViewModel;
        }

        public List<DeviceSoftwareInstalledWriteViewModel> GetAllSoftwareInstalled(Guid computerId)
        {
            List<DeviceSoftwareInstalledWriteViewModel> listSoftware = new();

            using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
            {
                foreach (string skName in rk.GetSubKeyNames())
                {
                    using (RegistryKey sk = rk.OpenSubKey(skName))
                    {
                        try
                        {
                            var displayName = sk.GetValue("DisplayName");
                            var version = sk.GetValue("DisplayVersion");
                            var publisher = sk.GetValue("Publisher");

                            DeviceSoftwareInstalledWriteViewModel softwareInstalled = new();

                            if (displayName != null)
                            {
                                softwareInstalled.AppName = displayName.ToString();
                            }

                            if (version != null)
                            {
                                softwareInstalled.Version = version.ToString();
                            }

                            if (publisher != null)
                            {
                                softwareInstalled.Publisher = publisher.ToString();
                            }

                            softwareInstalled.DeviceId = computerId;

                            if (softwareInstalled.AppName != string.Empty)
                            {
                                listSoftware.Add(softwareInstalled);
                            }
                        }
                        catch (Exception ex)
                        { }
                    }
                }
            }

            return listSoftware;
        }
        #endregion

        #region Private Methods
        private string GetDataStringFromObjectQuery(string property, string table)
        {
            try
            {
                ObjectQuery wql = new ObjectQuery($"SELECT {property} FROM {table}");
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(wql);
                ManagementObjectCollection results = searcher.Get();

                foreach (ManagementObject result in results)
                {
                    return result[property].ToString();
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Get data from Object Query failed ! {property} from {table}");

                return string.Empty;
            }
        }

        private double GetDataDoubleFromObjectQuery(string property, string table)
        {
            try
            {
                ObjectQuery wql = new ObjectQuery($"SELECT {property} FROM {table}");
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(wql);
                ManagementObjectCollection results = searcher.Get();

                foreach (ManagementObject result in results)
                {
                    return Convert.ToDouble(result[property].ToString());
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Get data from Object Query failed ! {property} from {table}");

                return 0;
            }
        }

        private double GetPerformanceCounter(string categoryName, string counterName, string instanceName, bool isReadOnly = true)
        {
            try
            {
                PerformanceCounter performanceData = new();

                if (instanceName != string.Empty)
                {
                    performanceData = new PerformanceCounter(categoryName, counterName, instanceName, isReadOnly);
                }
                else
                {
                    performanceData = new PerformanceCounter(categoryName, counterName);
                }

                performanceData.NextValue();

                return (double)performanceData.NextValue();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Get information from performance counter failed !");
                return 0;
            }
        }

        private string GetPhysicalIPAdress()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                var addr = ni.GetIPProperties().GatewayAddresses.FirstOrDefault();
                if (addr != null && !addr.Address.ToString().Equals("0.0.0.0"))
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                    {
                        foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                return ip.Address.ToString();
                            }
                        }
                    }
                }
            }
            return String.Empty;
        }

        private ComputerDataViewModel GetComputerDataViewModel()
        {
            try
            {
                return new()
                {
                    MemoryUsed = GetDataDoubleFromObjectQuery("FreePhysicalMemory", "Win32_OperatingSystem"),
                    ProcessorUtilityPourcent = GetPerformanceCounter("Processor Information", "% Processor Utility", "_Total", true),
                    UptimeSinceInSecond = GetPerformanceCounter("System", "System Up Time", string.Empty)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Get Computer Data ViewModel failed !");

                return new()
                {
                    MemoryUsed = 0,
                    ProcessorUtilityPourcent = 0,
                    UptimeSinceInSecond = 0,
                };
            }
        }

        private List<ComputerHardDriveViewModel> GetListOfHardDrive()
        {
            try
            {
                List<ComputerHardDriveViewModel> listHardDrives = new();

                foreach (var drive in DriveInfo.GetDrives().Where(item => item.DriveType == DriveType.Fixed))
                {
                    listHardDrives.Add(new()
                    {
                        Letter = drive.Name.Replace(":\\", string.Empty),
                        SpaceUse = drive.TotalSize - drive.AvailableFreeSpace,
                        TotalSpace = drive.TotalSize
                    });
                }

                return listHardDrives;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Get Computer HardDrive ViewModel failed !");

                return new();
            }
        }
        #endregion
    }
}
