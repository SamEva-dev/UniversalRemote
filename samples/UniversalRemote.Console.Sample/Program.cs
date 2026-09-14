using DomainRelay.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Abstractions;
using UniversalRemote.Application;
using UniversalRemote.Provider.Simulator;

var services = new ServiceCollection();
services.AddUniversalRemoteApplication();
services.AddSingleton<IDeviceRepository, DemoDeviceRepository>();
services.AddSingleton<IRemoteProvider, SimulatorProvider>();
using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
using var scope = container.CreateScope();
var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
var devices = await mediator.Send(new ListDevices());
foreach (var device in devices) Console.WriteLine($"{device.DisplayName} ({device.Id})");
if (args.Contains("--smoke"))
{
    var result = await mediator.Send(new ExecuteRemoteAction(DemoDeviceRepository.DemoDeviceId, RemoteActions.VolumeUp));
    Console.WriteLine($"Simulation: {result.Error} / {result.Delivery}");
    return result.IsSuccess && devices.Count == 1 ? 0 : 1;
}
Console.WriteLine("SIMULATION uniquement. p = Power, + / - = Volume, o = OK, h = Home non supporté, q = Quitter");
while (Console.ReadLine() is { } input && input != "q")
{
    RemoteAction? action = input switch
    {
        "p" => RemoteActions.PowerToggle,
        "+" => RemoteActions.VolumeUp,
        "-" => RemoteActions.VolumeDown,
        "o" => RemoteActions.Ok,
        "h" => RemoteActions.Home,
        _ => null
    };
    if (action is null) { Console.WriteLine("Commande inconnue."); continue; }
    var result = await mediator.Send(new ExecuteRemoteAction(DemoDeviceRepository.DemoDeviceId, action));
    Console.WriteLine($"{action.Id}: {result.Error} / {result.Delivery}");
}
return 0;

