using Atlas_Monitoring_Reporter;
using Atlas_Monitoring_Reporter.Models.Internal;
using CliWrap;
using Microsoft.Win32;
using System.ServiceProcess;

if (args is { Length: 1 })
{
    string executablePath = Path.Combine(AppContext.BaseDirectory, "Atlas Monitoring Reporter.exe");

    if (args[0] is "/Install")
    {
        //If the service exist, remove it before creation
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

    if (!builder.Environment.IsDevelopment())
    {
        builder.Services.Configure<ReporterConfiguration>(x =>
        {
            x.IntervalInSeconds = Convert.ToInt32(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Atlas_Monitoring", "IntervalInSeconds", 300));
            x.URLApi = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Atlas_Monitoring", "ApiURL", string.Empty).ToString();
        });
    }
    else
    {
        builder.Services.Configure<ReporterConfiguration>(x =>
        {
            x.IntervalInSeconds = 300;
            x.URLApi = "http://localhost:5241/api";
        });
    }

    
    builder.Services.AddSingleton<Worker>();
    builder.Services.AddHostedService<Worker>();

    builder.Services.AddSingleton<StaticInformationComputer>();

    var host = builder.Build();
    host.Run();

}
