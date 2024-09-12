using Atlas_Monitoring_Reporter;
using CliWrap;

if (args is { Length: 1 })
{
    try
    {
        string executablePath =
            Path.Combine(AppContext.BaseDirectory, "Atlas Monitoring Reporter.exe");

        if (args[0] is "/Install")
        {
            await Cli.Wrap("sc")
                .WithArguments(new[] { "create", "Atlas Monitoring Reporter", $"binPath={executablePath}", "start=auto" })
                .ExecuteAsync();

            await Cli.Wrap("sc")
                .WithArguments(new[] { "start", "Atlas Monitoring Reporter" })
                .ExecuteAsync();
        }
        else if (args[0] is "/Uninstall")
        {
            await Cli.Wrap("sc")
                .WithArguments(new[] { "stop", "Atlas Monitoring Reporter" })
                .ExecuteAsync();
            await Cli.Wrap("sc")
                .WithArguments(new[] { "delete", "Atlas Monitoring Reporter" })
                .ExecuteAsync();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
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

    builder.Services.AddSingleton<Worker>();
    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    host.Run();
}
