using FR911.Webhooks.Integration.Dtos;
using FR911.Webhooks.Integration.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FR911WebhookRelayReceiver;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly WebhookRelayMessageProcessor _webhookRelayProcessor;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public Worker(WebhookRelayMessageProcessor processor, IServiceScopeFactory serviceScopeFactory, ILogger<Worker> logger)
    {        
        _webhookRelayProcessor = processor;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _jsonSerializerOptions = new JsonSerializerOptions() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true };        

        _webhookRelayProcessor.OnWebhookNotification += _webhookRelayProcessor_OnWebhookNotification;
    }

    private async Task _webhookRelayProcessor_OnWebhookNotification(object source, WebhookRelayMessageArgs args)
    {     
        _logger.LogDebug($"Received WebhookNotification: \n{args.WebhookNotification}");

        // All Interests Notifications include Notification.CurrentState
        var currentState = args.WebhookNotification!.CurrentState;

        // If Interest is Incident.Update or Incident.Close there will be Notification.Delta
        var difference = args.WebhookNotification.Delta;

        // Do something useful with the payload.  DO NOT synchronously block.  Message lock will be lost in 60s
        await Task.Delay(100);// Simulate work

        var message = args?.WebhookNotification?.Interest switch
        {
            WebhookInterests.IncidentNew => "Received New Incident",
            WebhookInterests.IncidentUpdate => "Received Update Incident",
            WebhookInterests.IncidentClose => "Received Close Incident",
            _ => "Received Unknown Interest",
        };        
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting WebhookRelayMessageProcessor Worker");
        await _webhookRelayProcessor.Start();
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // await UpdateSettings(); // Optional.  Settngs should probably never change
                await Task.Delay(WebhookRelayMessageProcessor.OptionsUpdatePeriod, stoppingToken); // 5mins
            }
            catch (TaskCanceledException) { break; }
        }
        _webhookRelayProcessor.OnWebhookNotification -= _webhookRelayProcessor_OnWebhookNotification;
        await _webhookRelayProcessor.Stop();
        _logger.LogDebug("WebhookRelayMessageProcessor Worker finished.");
    }

    private async Task UpdateSettings()
    {
        // Optionally, Periodically Check for updated Configuration from Api
        using (IServiceScope scope = _serviceScopeFactory.CreateScope())
        {
            IOptionsSnapshot<WebhookRelayMessageProcessorOptions> options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<WebhookRelayMessageProcessorOptions>>();
            await _webhookRelayProcessor.UpdateConfiguration(options.Value);
        }
    }
}
