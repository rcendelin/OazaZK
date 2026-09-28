using Oaza.Application.Ledger;
using Oaza.Domain.Constants;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Readings;

/// <summary>
/// Export of meter readings to XLSX / CSV (T04): every reading in the range with the consumption since the previous
/// reading of the meter and the estimate flag with its description — so an estimate is visible in the export too.
/// </summary>
public class ReadingsExportUseCase
{
    private readonly IWaterMeterRepository _meters;
    private readonly IMeterReadingRepository _readings;
    private readonly IHouseRepository _houses;

    public ReadingsExportUseCase(IWaterMeterRepository meters, IMeterReadingRepository readings, IHouseRepository houses)
    {
        _meters = meters ?? throw new ArgumentNullException(nameof(meters));
        _readings = readings ?? throw new ArgumentNullException(nameof(readings));
        _houses = houses ?? throw new ArgumentNullException(nameof(houses));
    }

    public async Task<ExportFile> ExportAsync(DateOnly? from, DateOnly? to, string format)
    {
        if (from is { } f && to is { } t && f > t)
            throw new ArgumentException("Datum od musí být nejpozději datum do.", nameof(from));

        var houses = (await _houses.GetByPartitionKeyAsync(PartitionKeys.House)).ToDictionary(h => h.Id, h => h.Name);
        var meters = (await _meters.GetByPartitionKeyAsync(PartitionKeys.Meter))
            .OrderBy(m => m.Type == MeterType.Main ? 0 : 1)
            .ThenBy(m => m.HouseId is null ? string.Empty : houses.GetValueOrDefault(m.HouseId, string.Empty), StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("cs-CZ"), false))
            .ThenBy(m => m.MeterNumber, StringComparer.Ordinal);

        var rows = new List<object?[]>();
        foreach (var meter in meters)
        {
            var place = meter.Type == MeterType.Main
                ? "hlavní vodoměr"
                : meter.HouseId is null ? string.Empty : houses.GetValueOrDefault(meter.HouseId, meter.HouseId);
            decimal? previous = null;
            foreach (var reading in (await _readings.GetByMeterIdAsync(meter.Id)).OrderBy(r => r.ReadingDate))
            {
                var day = DateOnly.FromDateTime(reading.ReadingDate);
                var consumption = previous is { } p ? reading.Value - p : (decimal?)null;
                previous = reading.Value;
                if (day < from || day > to)
                    continue;
                rows.Add(
                [
                    day, meter.MeterNumber, place, new CubicMetres(reading.Value),
                    consumption is { } c ? new CubicMetres(c) : null,
                    reading.IsEstimate ? "ano" : "ne", reading.EstimateNote,
                    reading.Source == ReadingSource.Import ? "import" : "ručně",
                ]);
            }
        }

        string[] header = ["Datum", "Vodoměr", "Dům", "Stav (m³)", "Spotřeba od předchozího (m³)", "Odhad", "Popis odhadu", "Zdroj"];
        var range = (from, to) switch
        {
            (null, null) => "všechny",
            _ => $"{(from is { } a ? LedgerExport.Day(a) : "…")} – {(to is { } b ? LedgerExport.Day(b) : "…")}",
        };
        var name = $"odecty-{from?.ToString("yyyyMMdd") ?? "zacatek"}-{to?.ToString("yyyyMMdd") ?? "dnes"}";
        return LedgerExport.Table(format, name, "Odečty", $"Odečty vodoměrů ({range}); odhad = dopočtená hodnota, ne fyzický odečet", header, rows);
    }
}
