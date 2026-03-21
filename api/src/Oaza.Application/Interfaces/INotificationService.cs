namespace Oaza.Application.Interfaces;

public interface INotificationService
{
    Task SendReadingReminderAsync();
    Task SendImportNotificationAsync(int year, int month);
    Task SendSettlementNotificationAsync(string periodId);
}
