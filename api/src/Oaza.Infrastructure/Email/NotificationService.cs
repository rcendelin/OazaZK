using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Oaza.Application.Interfaces;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Email;

public class NotificationService : INotificationService
{
    private readonly IEmailService _emailService;
    private readonly IUserRepository _userRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly IWaterMeterRepository _meterRepository;
    private readonly IMeterReadingRepository _readingRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly ILogger<NotificationService> _logger;

    private static readonly CultureInfo CzCulture = new("cs-CZ");

    private static readonly string[] CzechMonthNames =
    {
        "leden", "únor", "březen", "duben", "květen", "červen",
        "červenec", "srpen", "září", "říjen", "listopad", "prosinec"
    };

    public NotificationService(
        IEmailService emailService,
        IUserRepository userRepository,
        IHouseRepository houseRepository,
        IWaterMeterRepository meterRepository,
        IMeterReadingRepository readingRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ISettlementRepository settlementRepository,
        ILogger<NotificationService> logger)
    {
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _meterRepository = meterRepository ?? throw new ArgumentNullException(nameof(meterRepository));
        _readingRepository = readingRepository ?? throw new ArgumentNullException(nameof(readingRepository));
        _billingPeriodRepository = billingPeriodRepository ?? throw new ArgumentNullException(nameof(billingPeriodRepository));
        _settlementRepository = settlementRepository ?? throw new ArgumentNullException(nameof(settlementRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SendReadingReminderAsync()
    {
        _logger.LogInformation("Sending monthly reading reminder emails.");

        var users = await _userRepository.GetByPartitionKeyAsync(PartitionKeys.User);
        var eligibleUsers = users.Where(u => u.NotificationsEnabled && !string.IsNullOrEmpty(u.Email)).ToList();

        var now = DateTime.UtcNow;
        var monthName = CzechMonthNames[now.Month - 1];
        var subject = "Připomínka odečtu vodoměru";
        var bodyMonth = $"{monthName} {now.Year}";

        var sentCount = 0;
        foreach (var user in eligibleUsers)
        {
            try
            {
                var plainText = $"""
                    Dobrý den {user.Name},

                    nezapomeňte provést odečet vodoměru za {bodyMonth}.

                    S pozdravem,
                    Portál Oáza Zadní Kopanina
                    """;

                var html = $"""
                    <div style="font-family: sans-serif; max-width: 600px; margin: 0 auto;">
                        <h2 style="color: #2563eb;">Připomínka odečtu vodoměru</h2>
                        <p>Dobrý den {WebUtility.HtmlEncode(user.Name)},</p>
                        <p>nezapomeňte provést odečet vodoměru za <strong>{WebUtility.HtmlEncode(bodyMonth)}</strong>.</p>
                        <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 24px 0;" />
                        <p style="color: #9ca3af; font-size: 12px;">
                            Portál Oáza Zadní Kopanina
                        </p>
                    </div>
                    """;

                await _emailService.SendEmailAsync(user.Email, user.Name, subject, plainText, html);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send reading reminder to {Email}.", user.Email);
            }
        }

        _logger.LogInformation("Reading reminder sent to {Count} users.", sentCount);
    }

    public async Task SendImportNotificationAsync(int year, int month)
    {
        _logger.LogInformation("Sending import notification for {Year}-{Month}.", year, month);

        var users = await _userRepository.GetByPartitionKeyAsync(PartitionKeys.User);
        var allHouses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
        var allMeters = await _meterRepository.GetByPartitionKeyAsync(PartitionKeys.Meter);
        var houseLookup = allHouses.ToDictionary(h => h.Id, h => h.Name);

        var monthName = CzechMonthNames[month - 1];
        var subject = "Nové odečty importovány";

        var sentCount = 0;
        foreach (var user in users.Where(u => u.NotificationsEnabled && !string.IsNullOrEmpty(u.Email) && u.Role == UserRole.Member))
        {
            try
            {
                var consumptionInfo = "";
                if (!string.IsNullOrEmpty(user.HouseId))
                {
                    var houseMeters = allMeters.Where(m => m.HouseId == user.HouseId).ToList();
                    foreach (var meter in houseMeters)
                    {
                        var readings = await _readingRepository.GetByMeterIdAsync(meter.Id);
                        var monthReading = readings.FirstOrDefault(r => r.ReadingDate.Year == year && r.ReadingDate.Month == month);
                        if (monthReading is not null)
                        {
                            var previous = readings
                                .Where(r => r.ReadingDate < monthReading.ReadingDate)
                                .OrderByDescending(r => r.ReadingDate)
                                .FirstOrDefault();
                            if (previous is not null)
                            {
                                var consumption = monthReading.Value - previous.Value;
                                consumptionInfo = $" Vaše spotřeba za {monthName}: {consumption.ToString("F1", CzCulture)} m3.";
                            }
                        }
                    }
                }

                var plainText = $"""
                    Dobrý den {user.Name},

                    odečty vodoměru za {monthName} {year} byly importovány.{consumptionInfo}

                    S pozdravem,
                    Portál Oáza Zadní Kopanina
                    """;

                var html = $"""
                    <div style="font-family: sans-serif; max-width: 600px; margin: 0 auto;">
                        <h2 style="color: #2563eb;">Nové odečty importovány</h2>
                        <p>Dobrý den {WebUtility.HtmlEncode(user.Name)},</p>
                        <p>odečty vodoměru za <strong>{WebUtility.HtmlEncode(monthName)} {year}</strong> byly importovány.{WebUtility.HtmlEncode(consumptionInfo)}</p>
                        <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 24px 0;" />
                        <p style="color: #9ca3af; font-size: 12px;">
                            Portál Oáza Zadní Kopanina
                        </p>
                    </div>
                    """;

                await _emailService.SendEmailAsync(user.Email, user.Name, subject, plainText, html);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send import notification to {Email}.", user.Email);
            }
        }

        _logger.LogInformation("Import notification sent to {Count} users.", sentCount);
    }

    public async Task SendSettlementNotificationAsync(string periodId)
    {
        _logger.LogInformation("Sending settlement notification for period {PeriodId}.", periodId);

        var period = await _billingPeriodRepository.GetAsync(PartitionKeys.Period, periodId);
        if (period is null)
        {
            _logger.LogWarning("Billing period {PeriodId} not found. Skipping settlement notification.", periodId);
            return;
        }

        var users = await _userRepository.GetByPartitionKeyAsync(PartitionKeys.User);
        var settlements = await _settlementRepository.GetByPartitionKeyAsync(periodId);
        var settlementByHouse = settlements.ToDictionary(s => s.HouseId, s => s);

        var subject = $"Vyúčtování {period.Name} uzavřeno";

        var sentCount = 0;
        foreach (var user in users.Where(u => u.NotificationsEnabled && !string.IsNullOrEmpty(u.Email) && u.Role == UserRole.Member))
        {
            try
            {
                var balanceInfo = "";
                if (!string.IsNullOrEmpty(user.HouseId) && settlementByHouse.TryGetValue(user.HouseId, out var settlement))
                {
                    if (settlement.Balance > 0)
                    {
                        balanceInfo = $" Váš doplatek: {settlement.Balance.ToString("F0", CzCulture)} Kč.";
                    }
                    else if (settlement.Balance < 0)
                    {
                        balanceInfo = $" Váš přeplatek: {Math.Abs(settlement.Balance).ToString("F0", CzCulture)} Kč.";
                    }
                    else
                    {
                        balanceInfo = " Váš účet je vyrovnán.";
                    }
                }

                var plainText = $"""
                    Dobrý den {user.Name},

                    vyúčtování za období "{period.Name}" bylo uzavřeno.{balanceInfo}

                    Podrobnosti a PDF vyúčtování najdete na portálu Oáza.

                    S pozdravem,
                    Portál Oáza Zadní Kopanina
                    """;

                var html = $"""
                    <div style="font-family: sans-serif; max-width: 600px; margin: 0 auto;">
                        <h2 style="color: #2563eb;">Vyúčtování uzavřeno</h2>
                        <p>Dobrý den {WebUtility.HtmlEncode(user.Name)},</p>
                        <p>vyúčtování za období <strong>"{WebUtility.HtmlEncode(period.Name)}"</strong> bylo uzavřeno.{WebUtility.HtmlEncode(balanceInfo)}</p>
                        <p>Podrobnosti a PDF vyúčtování najdete na portálu Oáza.</p>
                        <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 24px 0;" />
                        <p style="color: #9ca3af; font-size: 12px;">
                            Portál Oáza Zadní Kopanina
                        </p>
                    </div>
                    """;

                await _emailService.SendEmailAsync(user.Email, user.Name, subject, plainText, html);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send settlement notification to {Email}.", user.Email);
            }
        }

        _logger.LogInformation("Settlement notification sent to {Count} users.", sentCount);
    }
}
