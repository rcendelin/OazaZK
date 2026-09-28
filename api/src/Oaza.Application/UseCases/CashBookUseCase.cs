using System.Globalization;
using ClosedXML.Excel;
using Oaza.Application.Audit;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Interfaces;
using Oaza.Application.Ledger;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Domain.Services;
using Oaza.Domain.Time;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace Oaza.Application.UseCases;

/// <summary>
/// The association's cash book (T09, R10): transparent evidence of cash so there is no suspicion of a „black fund“.
/// Deposits and expenses (also without a receipt — then description and counterparty are required); the balance never
/// goes negative; entries are never deleted — a mistake is reversed by a storno. An expense that is a shared cost can
/// create a one-off cost entry paid from cash (T06). Days up to an interim closing are fixed.
/// </summary>
public class CashBookUseCase
{
    public const string CashBookEntity = "CashBookEntry";

    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    private static readonly Dictionary<CashBookEntryType, string> TypeLabels = new()
    {
        [CashBookEntryType.Deposit] = "Vklad",
        [CashBookEntryType.Expense] = "Výdaj",
        [CashBookEntryType.Correction] = "Storno",
    };

    private readonly ICashBookRepository _entries;
    private readonly ICostComponentRepository _components;
    private readonly CostEntriesUseCase _costEntries;
    private readonly IClosingBoundary _closingBoundary;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public CashBookUseCase(
        ICashBookRepository entries,
        ICostComponentRepository components,
        CostEntriesUseCase costEntries,
        IClosingBoundary closingBoundary,
        IAuditLogger audit,
        IClock clock)
    {
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _costEntries = costEntries ?? throw new ArgumentNullException(nameof(costEntries));
        _closingBoundary = closingBoundary ?? throw new ArgumentNullException(nameof(closingBoundary));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Entries in <c>[from, to]</c> (both optional) with the running balance, opening and closing balance.</summary>
    public async Task<CashBookResponse> ListAsync(DateOnly? from, DateOnly? to)
    {
        if (from is { } f && to is { } t && f > t)
            throw new AppException("Datum od musí být nejpozději v den data do.");

        var all = await _entries.GetAllEntriesAsync();
        var lines = CashBook.Lines(all);
        var correctedBy = all.Where(e => e.CorrectionOf is not null).ToDictionary(e => e.CorrectionOf!, e => e.Id);
        var names = (await _components.GetAllComponentsAsync()).ToDictionary(c => c.Id, c => c.Name);

        var inRange = lines.Where(l => (from is null || l.Entry.Date >= from) && (to is null || l.Entry.Date <= to)).ToList();
        var opening = lines.Where(l => from is not null && l.Entry.Date < from).Select(l => l.Balance).LastOrDefault();
        return new CashBookResponse
        {
            From = from,
            To = to,
            OpeningBalance = opening,
            Deposits = inRange.Where(l => l.Effect > 0).Sum(l => l.Effect),
            Expenses = -inRange.Where(l => l.Effect < 0).Sum(l => l.Effect),
            ClosingBalance = inRange.Count > 0 ? inRange[^1].Balance : opening,
            Entries = inRange.Select(l => ToResponse(l, correctedBy, names)).ToList(),
        };
    }

    public async Task<CashBookEntryResponse> CreateAsync(CreateCashBookEntryRequest request, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Type == CashBookEntryType.Correction)
            throw new BusinessRuleException(["Storno se zadává u původního záznamu."]);

        var entry = new CashBookEntry
        {
            Id = Guid.NewGuid().ToString(),
            Date = request.Date,
            Type = request.Type,
            Amount = request.Amount,
            Category = request.Category?.Trim() ?? string.Empty,
            Description = request.Description?.Trim() ?? string.Empty,
            Counterparty = Clean(request.Counterparty),
            HasReceipt = request.HasReceipt,
            DocumentId = Clean(request.DocumentId),
            BankTransactionRef = request.Type == CashBookEntryType.Deposit ? Clean(request.BankTransactionRef) : null,
            ComponentId = request.Type == CashBookEntryType.Expense ? Clean(request.ComponentId) : null,
            CreatedBy = actor.UserId,
            CreatedByName = actor.UserName,
            CreatedAt = _clock.Now.UtcDateTime,
        };
        var errors = CashBook.Check(entry).ToList();
        if (entry.Date > _clock.Today)
            errors.Add("Záznam nemůže být v budoucnosti.");
        if (errors.Count > 0)
            throw new BusinessRuleException(errors);
        await EnsureOpenAsync(entry.Date);

        var all = (await _entries.GetAllEntriesAsync()).Append(entry).ToList();
        if (CashBook.FirstNegative(all) is { } negative)
            throw new BusinessRuleException([$"Výdaj by způsobil záporný zůstatek pokladny ({Kc(negative.Balance)} k {CashBook.Day(negative.Entry.Date)})."]);

        // A shared cost paid from cash becomes a one-off cost entry of the component (T06).
        if (entry.ComponentId is not null)
        {
            var cost = await _costEntries.CreateAsync(entry.ComponentId, new SaveCostEntryRequest
            {
                Type = CostEntryType.OneOff, PeriodFrom = entry.Date, PeriodTo = entry.Date, Amount = entry.Amount,
                Supplier = entry.Counterparty, DocumentId = entry.DocumentId, PaidFrom = PaidFrom.Cash,
                Note = $"Z pokladny: {entry.Description}",
            }, actor);
            entry.CostEntryId = cost.Id;
        }

        await _entries.UpsertAsync(entry);
        await _audit.LogAsync(CashBookEntity, entry.Id, AuditActions.Create, null, entry, actor);
        return await GetAsync(entry.Id);
    }

    /// <summary>Reverses an entry in full, booked today (entries are never deleted). A linked cost entry is corrected too.</summary>
    public async Task<CashBookEntryResponse> StornoAsync(string id, StornoCashBookEntryRequest request, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = Clean(request.Reason) ?? throw new BusinessRuleException(["Uveďte důvod storna."]);
        var all = await _entries.GetAllEntriesAsync();
        var original = all.FirstOrDefault(e => e.Id == id) ?? throw new NotFoundException(CashBookEntity, id);
        if (original.Type == CashBookEntryType.Correction)
            throw new BusinessRuleException(["Storno nelze stornovat — zadejte nový záznam."]);
        if (all.Any(e => e.CorrectionOf == id))
            throw new BusinessRuleException(["Záznam už je stornovaný."]);

        var storno = new CashBookEntry
        {
            Id = Guid.NewGuid().ToString(),
            Date = _clock.Today,
            Type = CashBookEntryType.Correction,
            Amount = original.Amount,
            Category = original.Category,
            Description = $"Storno: {original.Description} — {reason}",
            Counterparty = original.Counterparty,
            HasReceipt = original.HasReceipt,
            CorrectionOf = original.Id,
            ComponentId = original.ComponentId,
            CreatedBy = actor.UserId,
            CreatedByName = actor.UserName,
            CreatedAt = _clock.Now.UtcDateTime,
        };
        if (CashBook.FirstNegative(all.Append(storno)) is { } negative)
            throw new BusinessRuleException([$"Storno by způsobilo záporný zůstatek pokladny ({Kc(negative.Balance)} k {CashBook.Day(negative.Entry.Date)})."]);

        if (original.CostEntryId is not null && original.ComponentId is not null)
        {
            var correction = await _costEntries.CreateAsync(original.ComponentId, new SaveCostEntryRequest
            {
                Type = CostEntryType.OneOff, PeriodFrom = original.Date, PeriodTo = original.Date, Amount = -original.Amount,
                Supplier = original.Counterparty, PaidFrom = PaidFrom.Cash, Note = $"Storno výdaje z pokladny: {original.Description}",
                Reason = reason, CorrectionOf = original.CostEntryId,
            }, actor);
            storno.CostEntryId = correction.Id;
        }

        await _entries.UpsertAsync(storno);
        await _audit.LogAsync(CashBookEntity, storno.Id, AuditActions.Correction, original, storno, actor, reason);
        return await GetAsync(storno.Id);
    }

    /// <summary>The cash book of a period for the accountant: XLSX, or PDF.</summary>
    public async Task<ExportFile> ExportAsync(DateOnly? from, DateOnly? to, string format)
    {
        var book = await ListAsync(from, to);
        var period = book.From is null && book.To is null
            ? "celá kniha"
            : $"{(book.From is { } f ? CashBook.Day(f) : "začátek")} – {(book.To is { } t ? CashBook.Day(t) : "dnes")}";
        var name = $"pokladni-kniha-{book.From:yyyyMMdd}-{book.To:yyyyMMdd}".Replace("--", "-").TrimEnd('-');
        return format.ToLowerInvariant() switch
        {
            "xlsx" => new ExportFile(Xlsx(book, period), LedgerExport.XlsxType, $"{name}.xlsx"),
            "pdf" => new ExportFile(Pdf(book, period), "application/pdf", $"{name}.pdf"),
            _ => throw new AppException("Formát exportu musí být xlsx, nebo pdf."),
        };
    }

    private async Task<CashBookEntryResponse> GetAsync(string id) =>
        (await ListAsync(null, null)).Entries.Single(e => e.Id == id);

    private async Task EnsureOpenAsync(DateOnly date)
    {
        if (await _closingBoundary.GetLastAllHousesClosedDayAsync() is { } closed && date <= closed)
            throw new BusinessRuleException([$"Den {CashBook.Day(date)} je uzavřený mezizávěrkou k {CashBook.Day(closed)}; zapište záznam s pozdějším datem."]);
    }

    private static string[] Header => ["Datum", "Druh", "Kategorie", "Popis", "Komu / od koho", "Doklad", "Příjem (Kč)", "Výdaj (Kč)", "Zůstatek (Kč)"];

    private static object?[] Row(CashBookEntryResponse e) =>
    [
        e.Date, TypeLabels[e.Type], e.Category, e.Description, e.Counterparty,
        e.HasReceipt ? "ano" : "bez dokladu",
        e.Effect > 0 ? e.Effect : null, e.Effect < 0 ? -e.Effect : null, e.Balance,
    ];

    private static byte[] Xlsx(CashBookResponse book, string period)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Pokladní kniha");
        ws.Cell(1, 1).Value = $"Pokladní kniha — {period}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = "Počáteční zůstatek";
        ws.Cell(2, 2).Value = book.OpeningBalance;
        for (var c = 0; c < Header.Length; c++)
        {
            ws.Cell(4, c + 1).Value = Header[c];
            ws.Cell(4, c + 1).Style.Font.Bold = true;
        }
        var r = 5;
        foreach (var entry in book.Entries)
        {
            var values = Row(entry);
            for (var c = 0; c < values.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (values[c])
                {
                    case null: break;
                    case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = "#,##0.00"; break;
                    case DateOnly day: cell.Value = day.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "d.m.yyyy"; break;
                    default: cell.Value = values[c]!.ToString(); break;
                }
            }
            r++;
        }
        ws.Cell(r + 1, 1).Value = "Příjmy";
        ws.Cell(r + 1, 2).Value = book.Deposits;
        ws.Cell(r + 2, 1).Value = "Výdaje";
        ws.Cell(r + 2, 2).Value = book.Expenses;
        ws.Cell(r + 3, 1).Value = "Konečný zůstatek";
        ws.Cell(r + 3, 2).Value = book.ClosingBalance;
        ws.Columns().AdjustToContents(1, 200);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] Pdf(CashBookResponse book, string period)
    {
        var document = new PdfDocument();
        document.Info.Title = $"Pokladní kniha — {period}";
        var fontTitle = new XFont("Arial", 14, XFontStyle.Bold);
        var fontBold = new XFont("Arial", 8, XFontStyle.Bold);
        var font = new XFont("Arial", 8, XFontStyle.Regular);
        double[] widths = [50, 38, 60, 150, 80, 48, 50, 50, 55];

        PdfPage page = document.AddPage();
        page.Orientation = PdfSharpCore.PageOrientation.Landscape;
        var gfx = XGraphics.FromPdfPage(page);
        double y = 40;
        const double left = 30;

        void Line(object?[] values, XFont f)
        {
            if (y > page.Height - 40)
            {
                gfx.Dispose();
                page = document.AddPage();
                page.Orientation = PdfSharpCore.PageOrientation.Landscape;
                gfx = XGraphics.FromPdfPage(page);
                y = 40;
            }
            var x = left;
            for (var i = 0; i < values.Length; i++)
            {
                var text = values[i] switch
                {
                    null => string.Empty,
                    decimal d => d.ToString("N2", Czech),
                    DateOnly day => CashBook.Day(day),
                    _ => values[i]!.ToString() ?? string.Empty,
                };
                var format = values[i] is decimal ? XStringFormats.TopRight : XStringFormats.TopLeft;
                var rect = new XRect(x, y, widths[i] - 4, 12);
                gfx.DrawString(Fit(gfx, text, f, widths[i] - 4), f, XBrushes.Black, rect, format);
                x += widths[i];
            }
            y += 13;
        }

        gfx.DrawString($"Oáza Zadní Kopanina — pokladní kniha ({period})", fontTitle, XBrushes.Black, left, y);
        y += 22;
        gfx.DrawString($"Počáteční zůstatek: {Kc(book.OpeningBalance)}", font, XBrushes.Black, left, y);
        y += 16;
        Line(Header, fontBold);
        foreach (var entry in book.Entries)
            Line(Row(entry), font);
        y += 8;
        Line(["", "", "", "Příjmy / výdaje / konečný zůstatek", "", "", book.Deposits, book.Expenses, book.ClosingBalance], fontBold);
        gfx.Dispose();

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static string Fit(XGraphics gfx, string text, XFont font, double width)
    {
        if (gfx.MeasureString(text, font).Width <= width)
            return text;
        while (text.Length > 1 && gfx.MeasureString(text + "…", font).Width > width)
            text = text[..^1];
        return text + "…";
    }

    private static CashBookEntryResponse ToResponse(CashBookLine line, IReadOnlyDictionary<string, string> correctedBy, IReadOnlyDictionary<string, string> components)
    {
        var e = line.Entry;
        return new CashBookEntryResponse
        {
            Id = e.Id, Date = e.Date, Type = e.Type, Amount = e.Amount, Effect = line.Effect, Balance = line.Balance,
            Category = e.Category, Description = e.Description, Counterparty = e.Counterparty, HasReceipt = e.HasReceipt,
            DocumentId = e.DocumentId, BankTransactionRef = e.BankTransactionRef, CorrectionOf = e.CorrectionOf,
            CorrectedBy = correctedBy.GetValueOrDefault(e.Id), ComponentId = e.ComponentId,
            ComponentName = e.ComponentId is null ? null : components.GetValueOrDefault(e.ComponentId, e.ComponentId),
            CostEntryId = e.CostEntryId, CreatedByName = e.CreatedByName,
        };
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Kc(decimal value) => $"{value.ToString("N2", Czech)} Kč";
}
