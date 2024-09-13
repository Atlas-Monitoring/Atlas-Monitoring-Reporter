using Atlas_Monitoring.Core.Models.Database;
using Atlas_Monitoring_Reporter.Models.ViewModels;
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
        private readonly string _apiPath = string.Empty;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
            _apiPath = "http://localhost:8080/api";
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

                    //Step 3 : Send Computer Data
                    ComputerDataViewModel computerDataViewModel = computerWriteViewModel.ComputerLastData;
                    computerDataViewModel.ComputerId = computerId;

                    await AddComputerData(computerDataViewModel);

                    //Step 4 : Update Hard drive
                    foreach (ComputerHardDriveViewModel computerHardDriveViewModel in computerWriteViewModel.ComputerHardDrives)
                    {
                        computerHardDriveViewModel.ComputerId = computerId;
                    }

                    await AddComputerHardDrive(computerWriteViewModel.ComputerHardDrives);

                    //Step 5 : Delay between two report
                    int delay = 1000 * 60 * 5; //5 minutes delay
                    await Task.Delay(delay, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Internal Error");
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
            PerformanceCounter pourcentageUseCpu = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true);
            pourcentageUseCpu.NextValue();
            PerformanceCounter theMemCounter = new PerformanceCounter("Memory", "Available MBytes");
            theMemCounter.NextValue();
            PerformanceCounter upTimeSystem = new PerformanceCounter("System", "System Up Time");
            upTimeSystem.NextValue();

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
            }

            foreach (ManagementObject result in results2)
            {
                //Update Computer Name
                computerViewModel.Name = result["Name"].ToString();

                //Update Domain
                computerViewModel.Domain = result["Domain"].ToString();

                //Update NumberOfLogicalProcessors
                computerViewModel.NumberOfLogicalProcessors = Convert.ToDouble(result["NumberOfLogicalProcessors"].ToString());

                //Update OS
                string pathOfRegistry = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
                computerViewModel.OS = Registry.GetValue(pathOfRegistry, "productName", "Undefined").ToString();

                //Update OSVersion
                computerViewModel.OSVersion = $"{Environment.OSVersion.Version} ({Registry.GetValue(pathOfRegistry, "displayVersion", "Undefined").ToString()})";
            }

            //Update Ip
            var host = Dns.GetHostEntry(Dns.GetHostName());
            if (host.AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork).Any())
            {
                computerViewModel.Ip = host.AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork).First().ToString();
            }

            //Update UserName
            computerViewModel.UserName = Environment.UserName;

            //Update SerialNumber
            //Serial Number First Method
            ManagementObjectSearcher mbs = new ManagementObjectSearcher("Select * from Win32_BIOS");
            foreach (ManagementObject mo in mbs.Get())
            {
                computerViewModel.SerialNumber = mo["SerialNumber"].ToString().Trim();
                Console.WriteLine(computerViewModel.SerialNumber);
            }

            //Serial Number Second Method
            if (computerViewModel.SerialNumber == "Default string")
            {
                ManagementObjectSearcher mbs2 = new ManagementObjectSearcher("Select * from Win32_BaseBoard");
                foreach (ManagementObject mo in mbs2.Get())
                {
                    computerViewModel.SerialNumber = mo["SerialNumber"].ToString().Trim();
                    Console.WriteLine(computerViewModel.SerialNumber);
                }
            }

            //Get Processor name
            string pathOfRegistryProcessor = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0";
            computerViewModel.ProcessorName = Registry.GetValue(pathOfRegistryProcessor, "ProcessorNameString", "Undefined").ToString();

            //////////////////////////
            /// Write Computer Data

            computerViewModel.ComputerLastData = new()
            {
                MemoryUsed = UsedRam,
                ProcessorUtilityPourcent = pourcentageUseCpu.NextValue(),
                UptimeSinceInSecond = upTimeSystem.NextValue()
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

        private async Task<Guid> CheckIfComputerExist(string computerName, string serialNumber)
        {
            HttpClient client = new HttpClient();
            string path = $"{_apiPath}/Computers/{computerName}/{serialNumber}";

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
            string path = $"{_apiPath}/Computers";

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
            string path = $"{_apiPath}/Computers/{computerWriteViewModel.Id}";

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
            string path = $"{_apiPath}/ComputersData";

            HttpResponseMessage response = await client.PostAsJsonAsync(path, computerDataViewModel);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                _logger.LogInformation($"Computer data added");
            }
        }

        private async Task AddComputerHardDrive(List<ComputerHardDriveViewModel> listComputerHardDriveViewModel)
        {
            HttpClient client = new HttpClient();
            string path = $"{_apiPath}/ComputersHardDrive/{listComputerHardDriveViewModel.First().ComputerId}";

            HttpResponseMessage response = await client.PutAsJsonAsync(path, listComputerHardDriveViewModel);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                _logger.LogInformation($"Computer HardData added");
            }
        }
    }
}
