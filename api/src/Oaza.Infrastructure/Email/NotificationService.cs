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
        var subject = "Pripominka odectu vodomeru";
        var bodyMonth = $"{monthName} {now.Year}";

        var sentCount = 0;
        foreach (var user in eligibleUsers)
        {
            try
            {
                var plainText = $"""
                    Dobry den {user.Name},

                    nezapomente provest odecet vodomeru za {bodyMonth}.

                    S pozdravem,
                    Portal Oaza Zadni Kopanina
                    """;

                var html = $"""
                    <div style="font-family: sans-serif; max-width: 600px; margin: 0 auto;">
                        <h2 style="color: #2563eb;">Pripominka odectu vodomeru</h2>
                        <p>Dobry den {WebUtility.HtmlEncode(user.Name)},</p>
                        <p>nezapomente provest odecet vodomeru za <strong>{WebUtility.HtmlEncode(bodyMonth)}</strong>.</p>
                        <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 24px 0;" />
                        <p style="color: #9ca3af; font-size: 12px;">
                            Portal Oaza Zadni Kopanina
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
        var subject = "Nove odecty importovany";

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
                                consumptionInfo = $" Vase spotreba za {monthName}: {consumption.ToString("F1", CzCulture)} m3.";
                            }
                        }
                    }
                }

                var plainText = $"""
                    Dobry den {user.Name},

                    odecty vodomeru za {monthName} {year} byly importovany.{consumptionInfo}

                    S pozdravem,
                    Portal Oaza Zadni Kopanina
                    """;

                var html = $"""
                    <div style="font-family: sans-serif; max-width: 600px; margin: 0 auto;">
                        <h2 style="color: #2563eb;">Nove odecty importovany</h2>
                        <p>Dobry den {WebUtility.HtmlEncode(user.Name)},</p>
                        <p>odecty vodomeru za <strong>{WebUtility.HtmlEncode(monthName)} {year}</strong> byly importovany.{WebUtility.HtmlEncode(consumptionInfo)}</p>
                        <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 24px 0;" />
                        <p style="color: #9ca3af; font-size: 12px;">
                            Portal Oaza Zadni Kopanina
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

        var subject = $"Vyuctovani {period.Name} uzavreno";

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
                        balanceInfo = $" Vas doplatek: {settlement.Balance.ToString("F0", CzCulture)} Kc.";
                    }
                    else if (settlement.Balance < 0)
                    {
                        balanceInfo = $" Vas preplatek: {Math.Abs(settlement.Balance).ToString("F0", CzCulture)} Kc.";
                    }
                    else
                    {
                        balanceInfo = " Vas ucet je vyrovnan.";
                    }
                }

                var plainText = $"""
                    Dobry den {user.Name},

                    vyuctovani za obdobi "{period.Name}" bylo uzavreno.{balanceInfo}

                    Podrobnosti a PDF vyuctovani najdete na portalu Oaza.

                    S pozdravem,
                    Portal Oaza Zadni Kopanina
                    """;

                var html = $"""
                    <div style="font-family: sans-serif; max-width: 600px; margin: 0 auto;">
                        <h2 style="color: #2563eb;">Vyuctovani uzavreno</h2>
                        <p>Dobry den {WebUtility.HtmlEncode(user.Name)},</p>
                        <p>vyuctovani za obdobi <strong>"{WebUtility.HtmlEncode(period.Name)}"</strong> bylo uzavreno.{WebUtility.HtmlEncode(balanceInfo)}</p>
                        <p>Podrobnosti a PDF vyuctovani najdete na portalu Oaza.</p>
                        <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 24px 0;" />
                        <p style="color: #9ca3af; font-size: 12px;">
                            Portal Oaza Zadni Kopanina
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
