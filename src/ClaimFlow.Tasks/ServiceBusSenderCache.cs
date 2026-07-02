using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;

namespace ClaimFlow.Tasks;

// Singleton cache of ServiceBusSenders keyed by queue name. Durable activities are
// transient (a new instance per invocation), so creating a sender inside the activity
// would open a fresh AMQP link every time and leak handles -> QuotaExceeded (max 199
// per connection). Reuse one sender per queue for the app's lifetime instead.
public sealed class ServiceBusSenderCache(ServiceBusClient client)
{
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    public ServiceBusSender Get(string queue) => _senders.GetOrAdd(queue, client.CreateSender);
}
