using Atlas_Monitoring.Core.Models.Database;
using Atlas_Monitoring_Reporter.Models.Internal;
using Atlas_Monitoring_Reporter.Models.ViewModels;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using System.Net;
using System.Net.Http.Json;

namespace Atlas_Monitoring_Reporter
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IOptions<ReporterConfiguration> _reporterConfiguration;
        private StaticInformationComputer _staticInformationComputer;

        public Worker(ILogger<Worker> logger, IOptions<ReporterConfiguration> reporterConfiguration, StaticInformationComputer staticInformationComputer)
        {
            _logger = logger;
            _reporterConfiguration = reporterConfiguration;
            _staticInformationComputer = staticInformationComputer;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    //Step 1 : Get Data
                    ComputerWriteViewModel computerWriteViewModel = _staticInformationComputer.GetStaticInformationOfComputer();

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

                    //Step 6 : Update Software
                    await SyncComputerSoftwate(computerId);

                    //Step 7 : Delay between two report
                    int delay = 1000 * _reporterConfiguration.Value.IntervalInSeconds; //Default delay 5 minutes
                    await Task.Delay(delay, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Internal Error");
                    _logger.LogInformation($"{_reporterConfiguration.Value.IntervalInSeconds} seconds before new try");

                    int delay = 1000 * _reporterConfiguration.Value.IntervalInSeconds; //Default delay 5 minutes
                    await Task.Delay(delay, stoppingToken);
                }
            }
        }

        private List<DevicePartsWriteViewModel> GetComputerParts(Guid computerId)
        {
            try
            {
                List<DevicePartsWriteViewModel> listComputerParts = new();

                //Get processor name
                string pathOfRegistryProcessor = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0";
                listComputerParts.Add(new() { DeviceId = computerId, Name = "Processor Name", Labels = Registry.GetValue(pathOfRegistryProcessor, "ProcessorNameString", "Undefined").ToString() });

                return listComputerParts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Get computer parts failed !");
                return new();
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

        private async Task SyncComputerSoftwate(Guid computerId)
        {
            HttpClient client = new HttpClient();
            string path = $"{_reporterConfiguration.Value.URLApi}/DeviceSoftwareInstalled";

            List<DeviceSoftwareInstalledWriteViewModel> listSoftware = _staticInformationComputer.GetAllSoftwareInstalled(computerId);

            HttpResponseMessage response = await client.PostAsJsonAsync(path, listSoftware);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                _logger.LogInformation($"Computer software sync");
            }
        }
    }
}
