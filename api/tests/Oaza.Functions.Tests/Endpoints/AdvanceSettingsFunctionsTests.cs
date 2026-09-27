using System.Reflection;
using Azure;
using Azure.Data.Tables;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Moq;
using Oaza.Application.Exceptions;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;
using Oaza.Functions.Endpoints;
using Oaza.Infrastructure.Persistence;

namespace Oaza.Functions.Tests.Endpoints;

/// <summary>
/// #11: per-house advance overrides are a read-modify-write of the stored map with optimistic concurrency, so
/// two admins editing different houses never overwrite each other. Writes are Admin only.
/// </summary>
public class AdvanceSettingsFunctionsTests
{
    private static readonly HouseAdvanceOverride Override100 = new() { WaterAdvance = 100, ElectricityAdvance = 20, CommonAdvance = 30 };
    private static readonly HouseAdvanceOverride Override200 = new() { WaterAdvance = 200, ElectricityAdvance = 0, CommonAdvance = 0 };

    [Theory]
    [InlineData(nameof(AdvanceSettingsFunctions.UpdateAdvanceSettingsAsync))]
    [InlineData(nameof(AdvanceSettingsFunctions.SetHouseAdvanceOverrideAsync))]
    [InlineData(nameof(AdvanceSettingsFunctions.DeleteHouseAdvanceOverrideAsync))]
    public void WritesAreAdminOnly(string method)
    {
        typeof(AdvanceSettingsFunctions).GetMethod(method)!.GetCustomAttribute<RequireRoleAttribute>()!.Roles
            .Should().Equal(UserRole.Admin);
    }

    [Fact]
    public void PerHouseRoutes()
    {
        Route(nameof(AdvanceSettingsFunctions.SetHouseAdvanceOverrideAsync)).Should().Be(("put", "advance-settings/overrides/{houseId}"));
        Route(nameof(AdvanceSettingsFunctions.DeleteHouseAdvanceOverrideAsync)).Should().Be(("delete", "advance-settings/overrides/{houseId}"));
    }

    [Fact]
    public void IsValid_RejectsNegativeAmountsAndNull()
    {
        AdvanceSettingsFunctions.IsValid(Override100).Should().BeTrue();
        AdvanceSettingsFunctions.IsValid(new HouseAdvanceOverride()).Should().BeTrue();
        AdvanceSettingsFunctions.IsValid(new HouseAdvanceOverride { CommonAdvance = -1 }).Should().BeFalse();
        AdvanceSettingsFunctions.IsValid(null).Should().BeFalse();
    }

    [Fact]
    public void ApplyOverride_ChangesOnlyTheGivenHouse()
    {
        var settings = new AdvanceSettings { HouseOverrides = { ["h1"] = Override100, ["h2"] = Override200 } };

        AdvanceSettingsFunctions.ApplyOverride(settings, "h1", Override200).Should().BeSameAs(Override100);
        AdvanceSettingsFunctions.ApplyOverride(settings, "h2", null).Should().BeSameAs(Override200);

        settings.HouseOverrides.Should().ContainSingle().Which.Key.Should().Be("h1");
        settings.HouseOverrides["h1"].Should().BeSameAs(Override200);
    }

    [Fact]
    public async Task Modify_FirstWrite_Inserts()
    {
        var table = new FakeTable();

        var (old, settings) = await AdvanceSettingsFunctions.ModifyOverrideAsync(table.Client, "h1", Override100);

        old.Should().BeNull();
        settings.HouseOverrides.Keys.Should().Equal("h1");
        table.Stored!.HouseOverrides["h1"].WaterAdvance.Should().Be(100);
        table.Adds.Should().Be(1);
    }

    [Fact]
    public async Task Modify_ConcurrentChangeOfAnotherHouse_IsKept()
    {
        // Admin A saves h1 while admin B has just saved h2: A's conditional update fails once (412), re-reads the
        // map with h2 in it and writes both.
        var table = new FakeTable(new AdvanceSettings());
        table.BeforeNextUpdate = () => table.Store(new AdvanceSettings { HouseOverrides = { ["h2"] = Override200 } });

        var (_, settings) = await AdvanceSettingsFunctions.ModifyOverrideAsync(table.Client, "h1", Override100);

        settings.HouseOverrides.Keys.Should().BeEquivalentTo("h1", "h2");
        table.Stored!.HouseOverrides.Keys.Should().BeEquivalentTo("h1", "h2");
        table.Updates.Should().Be(2);
    }

    [Fact]
    public async Task Modify_Delete_RemovesOnlyThatHouse()
    {
        var table = new FakeTable(new AdvanceSettings { HouseOverrides = { ["h1"] = Override100, ["h2"] = Override200 } });

        var (old, _) = await AdvanceSettingsFunctions.ModifyOverrideAsync(table.Client, "h1", null);

        old!.WaterAdvance.Should().Be(100);
        table.Stored!.HouseOverrides.Keys.Should().Equal("h2");
    }

    [Fact]
    public async Task Modify_NothingToChange_DoesNotWrite()
    {
        var table = new FakeTable(new AdvanceSettings { HouseOverrides = { ["h2"] = Override200 } });

        var (old, _) = await AdvanceSettingsFunctions.ModifyOverrideAsync(table.Client, "h1", null);

        old.Should().BeNull();
        table.Updates.Should().Be(0);
        table.Adds.Should().Be(0);
    }

    [Fact]
    public async Task Modify_PersistentConflict_Gives409()
    {
        var table = new FakeTable(new AdvanceSettings()) { AlwaysConflict = true };

        var act = () => AdvanceSettingsFunctions.ModifyOverrideAsync(table.Client, "h1", Override100);

        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);
        table.Updates.Should().Be(AdvanceSettingsFunctions.MaxWriteAttempts);
    }

    private static (string Verb, string? Route) Route(string method)
    {
        var trigger = typeof(AdvanceSettingsFunctions).GetMethod(method)!.GetParameters()
            .Select(p => p.GetCustomAttribute<HttpTriggerAttribute>()).Single(a => a is not null)!;
        return (trigger.Methods!.Single(), trigger.Route);
    }

    /// <summary>An in-memory settings row behind a mocked <see cref="TableClient"/> with ETag checks.</summary>
    private sealed class FakeTable
    {
        private TableEntity? _row;
        private int _version;

        public FakeTable(AdvanceSettings? initial = null)
        {
            if (initial is not null) Store(initial);
            var mock = new Mock<TableClient>();
            mock.Setup(t => t.GetEntityAsync<TableEntity>("SETTINGS", "advances", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _row is null
                    ? throw new RequestFailedException(404, "not found")
                    : Response.FromValue(Copy(_row), Mock.Of<Response>()));
            mock.Setup(t => t.AddEntityAsync(It.IsAny<TableEntity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((TableEntity e, CancellationToken _) =>
                {
                    Adds++;
                    if (_row is not null) throw new RequestFailedException(409, "exists");
                    Put(e);
                    return Mock.Of<Response>();
                });
            mock.Setup(t => t.UpdateEntityAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>(), TableUpdateMode.Replace, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TableEntity e, ETag etag, TableUpdateMode _, CancellationToken _) =>
                {
                    Updates++;
                    var before = BeforeNextUpdate;
                    BeforeNextUpdate = null;
                    before?.Invoke();
                    if (AlwaysConflict || _row is null || etag != _row.ETag) throw new RequestFailedException(412, "precondition failed");
                    Put(e);
                    return Mock.Of<Response>();
                });
            Client = mock.Object;
        }

        public TableClient Client { get; }
        public int Adds { get; private set; }
        public int Updates { get; private set; }
        public bool AlwaysConflict { get; init; }
        public Action? BeforeNextUpdate { get; set; }
        public AdvanceSettings? Stored => _row is null ? null : TableEntityMapper.ToAdvanceSettings(_row);

        public void Store(AdvanceSettings settings) => Put(TableEntityMapper.ToTableEntity(settings));

        private void Put(TableEntity entity)
        {
            _row = Copy(entity);
            _row.ETag = new ETag($"v{++_version}");
        }

        private static TableEntity Copy(TableEntity entity)
        {
            var copy = new TableEntity(entity);
            copy.ETag = entity.ETag;
            return copy;
        }
    }
}
