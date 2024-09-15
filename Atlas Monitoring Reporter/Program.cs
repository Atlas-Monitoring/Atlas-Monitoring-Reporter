using Atlas_Monitoring_Reporter;
using Atlas_Monitoring_Reporter.Models.Internal;
using CliWrap;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.ServiceProcess;
using System.Text.Json;

if (args is { Length: 1 })
{
    string executablePath = Path.Combine(AppContext.BaseDirectory, "Atlas Monitoring Reporter.exe");

    if (args[0] is "/Install")
    {
        if (ServiceController.GetServices().Any(x => x.ServiceName == "Atlas Monitoring Reporter"))
        {
            await Cli.Wrap("sc")
            .WithArguments(new[] { "stop", "Atlas Monitoring Reporter" })
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync();

            await Cli.Wrap("sc")
                .WithArguments(new[] { "delete", "Atlas Monitoring Reporter" })
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync();
        }

        //Install the Windows service
        await Cli.Wrap("sc")
            .WithArguments(new[] { "create", "Atlas Monitoring Reporter", $"binPath={executablePath}", "start=auto" })
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync();

        //Get the path of the installer
        var myId = Process.GetCurrentProcess().Id;
        var query = string.Format("SELECT CommandLine,ParentProcessId,ExecutablePath FROM Win32_Process WHERE CommandLine LIKE '%Atlas-Monitoring-Reporter-Installer.msi%'");
        var search = new ManagementObjectSearcher("root\\CIMV2", query);
        var results = search.Get().GetEnumerator();
        results.MoveNext();
        var queryObj = results.Current;
        var parentId = (uint)queryObj["ParentProcessId"];
        string pathOfInstaller = queryObj["CommandLine"].ToString().Replace(@"""C:\Windows\System32\msiexec.exe"" /i """, string.Empty).Replace(@".msi""", ".msi").Trim();
        pathOfInstaller = new FileInfo(pathOfInstaller).Directory.FullName;

        File.WriteAllText(@$"C:\temp\consoleApp-{DateTime.Now.ToString("yyyyMMddhhmmss")}.txt", $@"Path of installer = {pathOfInstaller} - Path of app = {AppContext.BaseDirectory}" +
            $"\n\r MSiWin ID = {parentId}\n\rCommandLine1 = {queryObj["CommandLine"]} - Path = {queryObj["ExecutablePath"]}");

        //Copy of the configuration file
        if (File.Exists($@"{pathOfInstaller}\appsettings.json"))
        {
            File.Copy($@"{pathOfInstaller}\appsettings.json", $@"{AppContext.BaseDirectory}appsettings.json", true);
            File.WriteAllText(@$"C:\temp\FichierExiste-{DateTime.Now.ToString("yyyyMMddhhmmss")}.txt", $@"Oui il existe ! - {pathOfInstaller}\appsettings.json   =====>     {AppContext.BaseDirectory}appsettings.json");
        }
        else
        {
            File.WriteAllText(@$"C:\temp\FichierExistePas-{DateTime.Now.ToString("yyyyMMddhhmmss")}.txt", $@"Oui il existe ! - {pathOfInstaller}\appsettings.json   =====>     {AppContext.BaseDirectory}appsettings.json");
            throw new Exception("Configuration file is missing !");
        }

        //Start the service
        await Cli.Wrap("sc")
            .WithArguments(new[] { "start", "Atlas Monitoring Reporter" })
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync();
    }
    else if (args[0] is "/Uninstall")
    {
        await Cli.Wrap("sc")
            .WithArguments(new[] { "stop", "Atlas Monitoring Reporter" })
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync();

        await Cli.Wrap("sc")
            .WithArguments(new[] { "delete", "Atlas Monitoring Reporter" })
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync();
    }

    return;
}
else
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "Atlas-Monitoring-Reporter";
    });

    //Get appsettings file
    string pathAppSetting = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    if (!File.Exists(pathAppSetting))
    {
        throw new Exception($"Config file don't exist at {pathAppSetting}");
    }

    IConfiguration config = new ConfigurationBuilder()
        .AddJsonFile(pathAppSetting)
        .Build();

    builder.Services.Configure<ReporterConfiguration>(config.GetSection("ReporterConfiguration"));
    builder.Services.AddSingleton<Worker>();
    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    host.Run();
    
}
