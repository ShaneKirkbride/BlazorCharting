using EquipmentHubDemo.Messaging;
using Microsoft.Extensions.Hosting;
using NetMQ;
using NetMQ.Sockets;
using static EquipmentHubDemo.Messaging.Zmq;

namespace EquipmentHubDemo.Workers;

/// <summary>
/// In-proc XSUB/XPUB proxy (Publisher(s) → XSUB → PROXY → XPUB → Subscriber(s)).
/// </summary>
public sealed class ZmqBrokerService : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(() =>
    {
        AsyncIO.ForceDotNet.Force();

        using var xsub = new XSubscriberSocket();
        using var xpub = new XPublisherSocket();

        xsub.Bind(Zmq.XSubBind);
        xpub.Bind(Zmq.XPubBind);

        var proxy = new Proxy(xsub, xpub);
        proxy.Start();   // blocks

    }, stoppingToken);
}
