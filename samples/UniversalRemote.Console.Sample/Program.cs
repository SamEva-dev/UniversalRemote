using DomainRelay.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Application;
using UniversalRemote.Remote.Core;
using UniversalRemote.Remote.Provider.Simulator;

var services = new ServiceCollection();
services.AddUniversalRemoteApplication();
var repository = new InMemoryDeviceRepository(await new DemoDeviceRepository().ListAsync());
services.AddSingleton<IDeviceRepository>(repository);
services.AddSingleton<IDeviceRegistrar>(repository);
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

