# FR911 Webhook Relay Client

A minimal, working example of receiving FirstResponse911 webhook notifications over the
Service Bus **relay**.

Use the relay when you want FirstResponse911 incident events but cannot expose a public HTTPS
endpoint for us to call. Instead of us pushing to your URL, your application connects outbound
and pulls notifications from a Service Bus subscription that belongs to your agency. Nothing
inbound needs to be opened in your firewall.

The payload you receive is a `WebhookNotificationDTO` and is **identical** whether it arrives by
relay or by direct HTTPS webhook, so you can switch delivery modes later without changing your
handler.

## What DataTech911 gives you

Three values, delivered out of band:

| Value | What it is |
|---|---|
| `ApiUri` | Base address of the FirstResponse911 API. |
| `ApiKey` | Your agency's API key. |
| `ServiceSettingsId` | Identifies your relay configuration. |

That is all you need. The client calls the API with those, retrieves its own Service Bus
connection details, and subscribes. **You never handle a Service Bus connection string** — the
credentials it fetches are listen-only and are refreshed automatically.

## Running this example

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/DataTech911/FR911WebhookRelayClient.git
cd FR911WebhookRelayClient/FR911WebhookRelayReceiver
```

Put your three values in user secrets rather than committing them:

```bash
dotnet user-secrets set "WebhookRelayMessageProcessorOptions:ApiUri" "<provided>"
dotnet user-secrets set "WebhookRelayMessageProcessorOptions:ApiKey" "<provided>"
dotnet user-secrets set "WebhookRelayMessageProcessorOptions:ServiceSettingsId" "<provided>"
dotnet run
```

On success you will see a line per region:

```
Started processing messages on Subscription <namespace>:<topic>:<subscription> using AmqpWebSockets
```

Two connections is expected and correct — see [Why two connections](#why-two-connections).

## How it works

`Program.cs` wires three things:

```csharp
// 1. Bind your three values from configuration
builder.Services.Configure<WebhookRelayMessageProcessorOptions>(
    builder.Configuration.GetSection(nameof(WebhookRelayMessageProcessorOptions)));

// 2. Let the client fetch its Service Bus details from the FR911 API
builder.Services.ConfigureOptions<ConfigureWebhookRelayMessageProcessorOptionsFromApi>();

// 3. Register the processor
builder.Services.AddSingleton<WebhookRelayMessageProcessor>();
```

`Worker.cs` subscribes to the event and starts it:

```csharp
_webhookRelayProcessor.OnWebhookNotification += OnNotification;
await _webhookRelayProcessor.Start();
```

Your handler receives the notification:

```csharp
private async Task OnNotification(object source, WebhookRelayMessageArgs args)
{
    var currentState = args.WebhookNotification.CurrentState;  // always present
    var delta        = args.WebhookNotification.Delta;         // present on Update and Close

    var message = args.WebhookNotification.Interest switch
    {
        WebhookInterests.IncidentNew    => "Received New Incident",
        WebhookInterests.IncidentUpdate => "Received Update Incident",
        WebhookInterests.IncidentClose  => "Received Close Incident",
        _                               => "Received Unknown Interest",
    };
}
```

### Two rules for your handler

**Do not block synchronously.** The message lock is held for 60 seconds. Hand off long work to a
queue or background task and return.

**Be idempotent.** A message can be redelivered — after a lock expiry, a transient fault, or a
regional failover. Processing the same notification twice must be safe.

## Configuration

Set in `appSettings.json`, or in user secrets / environment variables for anything sensitive.

| Setting | Required | Default | Notes |
|---|---|---|---|
| `ApiUri` | Yes | — | Provided by DataTech911. |
| `ApiKey` | Yes | — | Provided by DataTech911. Treat as a secret. |
| `ServiceSettingsId` | Yes | — | Provided by DataTech911. |
| `TransportType` | No | `AmqpWebSockets` | Service Bus transport. See below. |

`SBPrimary`, `SBSecondary`, `SBTopic`, and `SBSubscriptionId` are populated automatically from the
API. Do not set them by hand.

### Network requirements

The client makes **outbound** connections only. Nothing inbound needs to be opened.

| Destination | Port | Purpose |
|---|---|---|
| Your `ApiUri` host | 443 | Fetching relay configuration |
| `*.servicebus.windows.net` | 443 | Receiving notifications (default transport) |

### TransportType

`AmqpWebSockets` (the default) runs Service Bus traffic over **port 443**, which most corporate,
county, and hospital firewalls already permit.

Switch to `AmqpTcp` only if your environment specifically requires raw AMQP. It uses **port 5671**,
which is blocked on many enterprise networks:

```json
"WebhookRelayMessageProcessorOptions": {
  "TransportType": "AmqpTcp"
}
```

### Behind an HTTP proxy

If your host reaches the internet through a proxy, supply it in code. A proxy is only honored on
the default `AmqpWebSockets` transport:

```csharp
builder.Services.Configure<WebhookRelayMessageProcessorOptions>(o =>
{
    o.WebProxy = new WebProxy("http://proxy.internal:8080");
});
```

## Why two connections

Your relay topic exists in **two Azure regions**. The client subscribes to both and processes
whichever delivers, so a regional outage does not interrupt delivery. Seeing two "Started
processing messages" lines, one per namespace, means it is working as designed.

This is also why your handler must be idempotent: a failover can redeliver a notification.

## Troubleshooting

### `SocketException (10060)` / `TimedOut (ServiceCommunicationProblem)`

The TCP connection was never established — something on your network dropped it. This happens
*before* authentication, so it does **not** mean a bad API key or a missing subscription.

It usually means the port is blocked. Check reachability from the machine running the client:

```powershell
Test-NetConnection -ComputerName <namespace>.servicebus.windows.net -Port 443
```

The namespace is named in the error message. If that fails, ask your network team to allow
outbound 443 to `*.servicebus.windows.net`. If you are on `AmqpTcp`, test port 5671 instead — and
consider moving to the default `AmqpWebSockets`, which is far more likely to be permitted.

A different error number means something else: `10061` is an active refusal, and `11001` is a DNS
failure.

### `MessagingEntityNotFound`

The connection and credentials worked, but the named subscription was not found. Contact
DataTech911 — do not re-register, as that can create duplicate deliveries.

### Connected, but nothing arrives

A quiet topic is normal when no qualifying incidents are occurring. Confirm the connection is
healthy by looking for the two "Started processing messages" lines. If those are present and you
still expect traffic, contact DataTech911 and we can publish a test incident to verify delivery
end to end.

## Support

Contact DataTech911 with the `ServiceSettingsId` you were issued and the relevant log output.
