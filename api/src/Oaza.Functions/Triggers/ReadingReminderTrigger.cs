using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Oaza.Application.Interfaces;

namespace Oaza.Functions.Triggers;

public class ReadingReminderTrigger
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<ReadingReminderTrigger> _logger;

    public ReadingReminderTrigger(
        INotificationService notificationService,
        ILogger<ReadingReminderTrigger> logger)
    {
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("ReadingReminderTimer")]
    public async Task RunAsync(
        [TimerTrigger("0 0 8 1 * *")] TimerInfo timerInfo)
    {
        _logger.LogInformation("Reading reminder timer triggered at {Time}.", DateTime.UtcNow);

        try
        {
            await _notificationService.SendReadingReminderAsync();
            _logger.LogInformation("Reading reminder timer completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in reading reminder timer.");
        }
    }
}
