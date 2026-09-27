using System.Text;
using FluentAssertions;
using Oaza.Application.BankImport;
using Oaza.Application.Exceptions;

namespace Oaza.Application.Tests.BankImport;

public class FioCsvParserTests
{
    private static byte[] Fixture() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "BankImport", "Fixtures", "fio-vypis-anonym.csv"));

    private const string TableHeader =
        "\"ID operace\";\"Datum\";\"Objem\";\"Měna\";\"Protiúčet\";\"Název protiúčtu\";\"Kód banky\";\"Název banky\";\"KS\";\"VS\";\"SS\";\"Poznámka\";\"Zpráva pro příjemce\";\"Typ\";\"Provedl\";\"Upřesnění\";\"Poznámka\";\"BIC\";\"ID pokynu\"";

    private static string Row(string id, string amount, string message = "RD1", string vs = "", string account = "111111111", string bank = "0300") =>
        $"\"{id}\";\"01.08.2026\";\"{amount}\";\"CZK\";\"{account}\";\"Name\";\"{bank}\";\"Bank\";\"\";\"{vs}\";\"\";\"note1\";\"{message}\";\"Příjem\";\"\";\"\";\"note2\";\"\";\"1\"";

    private static byte[] Csv(string income, string expense, params string[] rows)
    {
        var lines = new List<string>
        {
            "\"Výpis č. 1/2026 z účtu \"\"2601634649/2010\"\"\"",
            "\"Období: 01.08.2026 - 31.08.2026\"",
            $"\"Suma příjmů: {income} CZK\"",
            $"\"Suma výdajů: {expense} CZK\"",
            TableHeader,
        };
        lines.AddRange(rows);
        return Encoding.UTF8.GetBytes(string.Join("\r\n", lines));
    }

    [Fact]
    public void Parse_Fixture_ReadsHeaderAndAllTransactions()
    {
        var statement = FioCsvParser.Parse(Fixture());

        statement.OwnAccount!.ToString().Should().Be("2601634649/2010");
        statement.StatementNumber.Should().Be("8/2026");
        statement.DateFrom.Should().Be(new DateTime(2026, 8, 1));
        statement.DateTo.Should().Be(new DateTime(2026, 8, 31));
        statement.OpeningBalance.Should().Be(65949.13m);
        statement.ClosingBalance.Should().Be(78449.13m);
        statement.TotalIncome.Should().Be(12500m);
        statement.TotalExpense.Should().Be(0m);
        statement.Warnings.Should().BeEmpty("the fixture's movements add up to the header totals");

        statement.Transactions.Should().HaveCount(9);
        statement.Transactions.Sum(t => t.Amount).Should().Be(12500m);
    }

    [Fact]
    public void Parse_Fixture_MapsColumnsByIndex()
    {
        var statement = FioCsvParser.Parse(Fixture());

        var withPrefix = statement.Transactions.Single(t => t.Id == "10000000002");
        withPrefix.CounterAccount.Should().Be("107-2222222222");
        withPrefix.CounterBankCode.Should().Be("0100");
        withPrefix.VariableSymbol.Should().Be("7");
        withPrefix.Message.Should().Be("SPOLEK TEST");
        withPrefix.Date.Should().Be(new DateTime(2026, 8, 12));

        var doplatek = statement.Transactions.Single(t => t.Id == "10000000008");
        doplatek.Amount.Should().Be(500m);
        doplatek.VariableSymbol.Should().Be("05");
        doplatek.Message.Should().Be("26-07 doplatek RD5");
        doplatek.Note.Should().Be("Marie Ukázková");
        doplatek.Note2.Should().Be("Marie Ukázková");
        doplatek.Type.Should().Be("Okamžitá příchozí platba");

        statement.Transactions.Single(t => t.Id == "10000000006").VariableSymbol.Should().BeNull("VS \"0\" means none");
    }

    [Fact]
    public void Parse_DecimalCommaAndNegativeAmounts()
    {
        var statement = FioCsvParser.Parse(Csv("+1500,50", "-200", Row("1", "1500,50"), Row("2", "-200")));

        statement.Transactions.Select(t => t.Amount).Should().Equal(1500.50m, -200m);
        statement.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SumMismatch_AddsWarning()
    {
        var statement = FioCsvParser.Parse(Csv("+3000", "0", Row("1", "1500")));

        statement.Warnings.Should().ContainSingle().Which.Should().Contain("Suma příjmů");
    }

    [Fact]
    public void Parse_QuotedSeparatorsNewlinesAndEscapedQuotes()
    {
        var statement = FioCsvParser.Parse(Csv("+1500", "0", Row("1", "1500", message: "RD1; \"\"srpen\"\"\nzáloha")));

        statement.Transactions.Single().Message.Should().Be("RD1; \"srpen\"\nzáloha");
    }

    [Fact]
    public void Parse_Windows1250File_IsDecoded()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var utf8 = Encoding.UTF8.GetString(Csv("+1500", "0", Row("1", "1500", message: "záloha Čendelínovi")));
        var cp1250 = Encoding.GetEncoding(1250).GetBytes(utf8);

        var statement = FioCsvParser.Parse(cp1250);

        statement.Transactions.Single().Message.Should().Be("záloha Čendelínovi");
    }

    [Fact]
    public void Parse_FileWithoutTransactionTable_Throws()
    {
        var act = () => FioCsvParser.Parse(Encoding.UTF8.GetBytes("a;b;c\r\n1;2;3"));

        act.Should().Throw<AppException>().WithMessage("*Fio*");
    }

    [Fact]
    public void Parse_InvalidDate_ThrowsWithLineNumber()
    {
        var bad = Row("1", "1500").Replace("01.08.2026", "2026-08-01");

        var act = () => FioCsvParser.Parse(Csv("+1500", "0", bad));

        act.Should().Throw<AppException>().WithMessage("*Řádek 6*datum*");
    }
}
