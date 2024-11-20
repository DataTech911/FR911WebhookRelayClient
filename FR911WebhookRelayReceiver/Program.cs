// See https://aka.ms/new-console-template for more information
using FR911.Api.Client;
using FR911.Webhooks.Integration.Common;
using FR911.Webhooks.Integration.ServiceBus;
using FR911WebhookRelayReceiver;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// See appsettings.json for required configuration values.

var builder = Host.CreateApplicationBuilder(args); // Or WebApplication.CreateBuilder(args) if you're boostrapping AspNet
builder.Configuration.AddUserSecrets<Worker>();

builder.Logging.AddSimpleConsole(consoleOptions =>
{
    consoleOptions.SingleLine = false;
    consoleOptions.TimestampFormat = "HH:mm:ss.fff ";
    consoleOptions.UseUtcTimestamp = true;
});

// Get a partial copy from local settings (secrets) to bootstrap the WebhookRelayMessageProcessorOptions
var relayOptionsFileConfig = builder.Configuration.GetSection(nameof(WebhookRelayMessageProcessorOptions))
    .Get<WebhookRelayMessageProcessorOptions>();

builder.Services.AddHttpClient<FR911ApiClient>(FR911ApiClient.Name, (services, client) =>
{
    client.BaseAddress = new Uri(relayOptionsFileConfig!.ApiUri);
    client.DefaultRequestHeaders.Add(HttpConstants.FR911ApiKeyHeader, relayOptionsFileConfig.ApiKey);
})
    .AddStandardResilienceHandler();

builder.Services.Configure<WebhookRelayMessageProcessorOptions>(builder.Configuration.GetSection(nameof(WebhookRelayMessageProcessorOptions)));
builder.Services.ConfigureOptions<ConfigureWebhookRelayMessageProcessorOptionsFromApi>();

builder.Services.AddSingleton<WebhookRelayMessageProcessor>();
builder.Services.AddHostedService<Worker>();

var app = builder.Build();
await app.RunAsync();