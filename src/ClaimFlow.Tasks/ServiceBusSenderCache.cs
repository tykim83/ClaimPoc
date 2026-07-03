using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;

namespace ClaimFlow.Tasks;

// One sender per queue for the app's lifetime. Activities are transient, so creating
// senders there would leak AMQP links (cap 199 per connection).
public sealed class ServiceBusSenderCache(ServiceBusClient client)
{
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    public ServiceBusSender Get(string queue) => _senders.GetOrAdd(queue, client.CreateSender);
}
