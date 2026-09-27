using FluentAssertions;
using Oaza.Application.Seed;
using Oaza.Domain.Enums;

namespace Oaza.Application.Tests.Seed;

public class SeedCsvTests
{
    private static readonly string[] Columns = ["a", "b", "c"];

    [Fact]
    public void CommaSeparatorQuotesBomCommentsAndCrLf()
    {
        var (rows, errors) = SeedCsv.Parse("x.csv", "﻿# komentář\r\nA,B,C\r\n\"1,5\",\"řekl \"\"ahoj\"\"\",\r\n\r\n2,x\r\n", Columns);

        errors.Should().BeEmpty();
        rows.Should().HaveCount(2);
        rows[0].Line.Should().Be(3);
        rows[0].Number("a").Should().Be(1.5m);
        rows[0].Text("b").Should().Be("řekl \"ahoj\"");
        rows[0].Text("c").Should().BeNull();
        rows[1].Text("c").Should().BeNull(); // missing trailing cells are empty
    }

    [Fact]
    public void TooManyValuesAndWrongHeader_AreErrors()
    {
        SeedCsv.Parse("x.csv", "a;b;c\n1;2;3;4\n", Columns).Errors.Should().ContainSingle().Which.Should().Contain("řádek 2");
        SeedCsv.Parse("x.csv", "a;b\n", Columns).Errors.Should().ContainSingle().Which.Should().Contain("chybí sloupce c");
        SeedCsv.Parse("x.csv", "a;b;c;d\n", Columns).Errors.Should().ContainSingle().Which.Should().Contain("neznámé sloupce d");
        SeedCsv.Parse("x.csv", "", Columns).Rows.Should().BeEmpty();
    }

    [Fact]
    public void ValueReaders()
    {
        var row = SeedCsv.Parse("x.csv", "a;b;c\n-1 234,5;1.2.2024;ne\n", Columns).Rows.Single();

        row.Number("a").Should().Be(-1234.5m);
        row.Day("b").Should().Be(new DateOnly(2024, 2, 1));
        row.Flag("c", true).Should().BeFalse();
        row.Flag("missing", true).Should().BeTrue();
        row.Number("missing", required: false).Should().BeNull();
        row.Day("missing", required: false).Should().BeNull();
        row.Choice<MeterType>("missing", required: false).Should().BeNull();
        row.Choice<MeterType>("c").Should().BeNull();
        row.Choice<MeterType>("a").Should().BeNull(); // numbers are not enum names
        row.Number("b").Should().BeNull();
        row.Errors.Should().HaveCount(3);
    }
}
