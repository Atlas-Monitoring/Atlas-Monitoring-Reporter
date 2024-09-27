using Atlas_Monitoring.Core.Models.Database;
using Atlas_Monitoring_Reporter.Models.Internal;
using Atlas_Monitoring_Reporter.Models.ViewModels;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;

namespace Atlas_Monitoring_Reporter
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IOptions<ReporterConfiguration> _reporterConfiguration;

        public Worker(ILogger<Worker> logger, IOptions<ReporterConfiguration> reporterConfiguration)
        {
            _logger = logger;
            _reporterConfiguration = reporterConfiguration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    //Step 1 : Get Data
                    ComputerWriteViewModel computerWriteViewModel = GetStaticInformationOfComputer();

                    Guid computerId = await CheckIfComputerExist(computerWriteViewModel.Name, computerWriteViewModel.SerialNumber);

                    //Step 2 : If the computer don't exist, create it 
                    if (computerId == Guid.Empty)
                    {
                        computerId = await CreateComputerInDataBase(computerWriteViewModel);
                    }
                    //Step 2.1 : If the computer exist, save the id of the computer and update information (Ip, domain, user, etc...)
                    else
                    {
                        computerWriteViewModel.Id = computerId;
                        computerId = await UpdateComputerInDataBase(computerWriteViewModel);
                    }

                    //Step 3 : Sync Computer Parts
                    List<DevicePartsWriteViewModel> listComputerPart = GetComputerParts(computerId);

                    foreach (DevicePartsWriteViewModel part in listComputerPart)
                    {
                        await SyncComputerPart(part);
                    }

                    //Step 4 : Send Computer Data
                    ComputerDataViewModel computerDataViewModel = computerWriteViewModel.ComputerLastData;
                    computerDataViewModel.ComputerId = computerId;

                    await AddComputerData(computerDataViewModel);

                    //Step 5 : Update Hard drive
                    foreach (ComputerHardDriveViewModel computerHardDriveViewModel in computerWriteViewModel.ComputerHardDrives)
                    {
                        computerHardDriveViewModel.ComputerId = computerId;
                    }

                    await AddComputerHardDrive(computerWriteViewModel.ComputerHardDrives);

                    //Step 5 : Delay between two report
                    int delay = 1000 * _reporterConfiguration.Value.IntervalInSeconds; //5 minutes delay
                    await Task.Delay(delay, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Internal Error");

                    int delay = 1000 * _reporterConfiguration.Value.IntervalInSeconds; //5 minutes delay
                    await Task.Delay(delay, stoppingToken);
                    _logger.LogInformation($"{_reporterConfiguration.Value.IntervalInSeconds} seconds before new try");
                }
            }
        }

        private ComputerWriteViewModel GetStaticInformationOfComputer()
        {
            //Create Object
            ComputerWriteViewModel computerViewModel = new();
            double MaxRam = 0;
            double UsedRam = 0;

            //////////////////////////
            //Get All Information
            ObjectQuery wql = new ObjectQuery("SELECT * FROM Win32_OperatingSystem");
            ObjectQuery wql2 = new ObjectQuery("SELECT * FROM Win32_computersystem");
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(wql);
            ManagementObjectSearcher searcher2 = new ManagementObjectSearcher(wql2);
            ManagementObjectCollection results = searcher.Get();
            ManagementObjectCollection results2 = searcher2.Get();

            //////////////////////////
            ///Write Computer

            foreach (ManagementObject result in results)
            {
                //Update Max Ram
                computerViewModel.MaxRam = Convert.ToDouble(result["TotalVisibleMemorySize"].ToString()) / 1000000; //Transform to Giga Octet
                MaxRam = Convert.ToDouble(result["TotalVisibleMemorySize"].ToString());
                UsedRam = Convert.ToDouble(result["FreePhysicalMemory"].ToString());

                //Update OS
                computerViewModel.OS = result["Caption"].ToString();
            }

            foreach (ManagementObject result in results2)
            {
                //Update Computer Name
                computerViewModel.Name = result["Name"].ToString();

                //Update Domain
                computerViewModel.Domain = result["Domain"].ToString();

                //Update NumberOfLogicalProcessors
                computerViewModel.NumberOfLogicalProcessors = Convert.ToDouble(result["NumberOfLogicalProcessors"].ToString());

                //Update Model of computer
                computerViewModel.Model = result["Model"].ToString();

                //Update Manufacturer
                computerViewModel.Manufacturer = result["Manufacturer"].ToString();

                //Update OSVersion
                string pathOfRegistry = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";

                computerViewModel.OSVersion = $"{Environment.OSVersion.Version} ({Registry.GetValue(pathOfRegistry, "displayVersion", "Undefined").ToString()})";

                //Update UserName
                computerViewModel.UserName = result["UserName"].ToString().Split("\\")[1];
            }

            //Update Ip
            var host = Dns.GetHostEntry(Dns.GetHostName());
            if (host.AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork).Any())
            {
                computerViewModel.Ip = host.AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork).First().ToString();
            }

            //Update SerialNumber
            //Serial Number First Method
            ManagementObjectSearcher mbs = new ManagementObjectSearcher("Select * from Win32_BIOS");
            foreach (ManagementObject mo in mbs.Get())
            {
                computerViewModel.SerialNumber = mo["SerialNumber"].ToString().Trim();
            }

            //Serial Number Second Method
            if (computerViewModel.SerialNumber == "Default string")
            {
                ManagementObjectSearcher mbs2 = new ManagementObjectSearcher("Select * from Win32_BaseBoard");
                foreach (ManagementObject mo in mbs2.Get())
                {
                    computerViewModel.SerialNumber = mo["SerialNumber"].ToString().Trim();
                }
            }

            //////////////////////////
            /// Write Computer Data

            computerViewModel.ComputerLastData = new()
            {
                MemoryUsed = UsedRam,
                ProcessorUtilityPourcent = GetPerformanceCounter("Processor Information", "% Processor Utility", "_Total", true),
                UptimeSinceInSecond = GetPerformanceCounter("System", "System Up Time", string.Empty)
            };

            //////////////////////////
            /// Write Computer HardDrive
            foreach (var drive in DriveInfo.GetDrives().Where(item => item.DriveType == DriveType.Fixed))
            {
                computerViewModel.ComputerHardDrives.Add(new()
                {
                    Letter = drive.Name.Replace(":\\", string.Empty),
                    SpaceUse = drive.TotalSize - drive.AvailableFreeSpace,
                    TotalSpace = drive.TotalSize
                });
            }

            return computerViewModel;
        }

        private List<DevicePartsWriteViewModel> GetComputerParts(Guid computerId)
        {
            List<DevicePartsWriteViewModel> listComputerParts = new();

            //Get processor name
            string pathOfRegistryProcessor = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0";
            listComputerParts.Add(new() { DeviceId = computerId, Name = "Processor Name", Labels = Registry.GetValue(pathOfRegistryProcessor, "ProcessorNameString", "Undefined").ToString() });

            return listComputerParts;
        }

        private double GetPerformanceCounter(string categoryName, string counterName, string instanceName, bool isReadOnly = true)
        {
            try
            {
                if (!PerformanceCounterCategory.Exists(categoryName))
                {
                    PerformanceCounterCategory.Create(categoryName, categoryName, counterName, counterName);
                }

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

        private async Task<Guid> CheckIfComputerExist(string computerName, string serialNumber)
        {
            HttpClient client = new HttpClient();
            string path = $"{_reporterConfiguration.Value.URLApi}/Computers/{computerName}/{serialNumber}";

            HttpResponseMessage response = await client.GetAsync(path);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                string computerId = await response.Content.ReadAsStringAsync();
                computerId = computerId.Replace("\"", string.Empty);
                return Guid.Parse(computerId);
            }
            else
            {
                return Guid.Empty;
            }
        }

        private async Task<Guid> CreateComputerInDataBase(ComputerWriteViewModel computerWriteViewModel)
        {
            HttpClient client = new HttpClient();
            string path = $"{_reporterConfiguration.Value.URLApi}/Computers";

            HttpResponseMessage response = await client.PostAsJsonAsync(path, computerWriteViewModel);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                Device device = await response.Content.ReadFromJsonAsync<Device>();
                return device.Id;
            }
            else
            {
                return Guid.Empty;
            }
        }

        private async Task<Guid> UpdateComputerInDataBase(ComputerWriteViewModel computerWriteViewModel)
        {
            HttpClient client = new HttpClient();
            string path = $"{_reporterConfiguration.Value.URLApi}/Computers/{computerWriteViewModel.Id}";

            HttpResponseMessage response = await client.PutAsJsonAsync(path, computerWriteViewModel);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                ComputerReadViewModel device = await response.Content.ReadFromJsonAsync<ComputerReadViewModel>();
                return device.Id;
            }
            else
            {
                return Guid.Empty;
            }
        }

        private async Task AddComputerData(ComputerDataViewModel computerDataViewModel)
        {
            HttpClient client = new HttpClient();
            string path = $"{_reporterConfiguration.Value.URLApi}/ComputersData";

            HttpResponseMessage response = await client.PostAsJsonAsync(path, computerDataViewModel);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                _logger.LogInformation($"Computer data added");
            }
        }

        private async Task AddComputerHardDrive(List<ComputerHardDriveViewModel> listComputerHardDriveViewModel)
        {
            HttpClient client = new HttpClient();
            string path = $"{_reporterConfiguration.Value.URLApi}/ComputersHardDrive/{listComputerHardDriveViewModel.First().ComputerId}";

            HttpResponseMessage response = await client.PutAsJsonAsync(path, listComputerHardDriveViewModel);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                _logger.LogInformation($"Computer HardData added");
            }
        }

        private async Task SyncComputerPart(DevicePartsWriteViewModel computerPart)
        {
            HttpClient client = new HttpClient();
            string path = $"{_reporterConfiguration.Value.URLApi}/ComputerParts";

            HttpResponseMessage response = await client.PutAsJsonAsync(path, computerPart);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                _logger.LogInformation($"Computer part sync");
            }
        }
    }
}
