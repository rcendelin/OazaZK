using FluentAssertions;
using Moq;
using Oaza.Application.Audit;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.Audit;

public class AuditLoggerTests
{
    private readonly Mock<IAuditLogRepository> _repo = new();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 9, 27, 10, 30, 0, TimeSpan.Zero));
    private readonly AuditLogger _sut;
    private AuditLogEntry? _written;

    public AuditLoggerTests()
    {
        _repo.Setup(r => r.AppendAsync(It.IsAny<AuditLogEntry>()))
            .Callback<AuditLogEntry>(e => _written = e)
            .Returns(Task.CompletedTask);
        _sut = new AuditLogger(_repo.Object, _time);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private record Sample(string Name, decimal Weight, DayOfWeek Day);

    [Fact]
    public async Task Update_StoresSnapshotsAsCamelCaseJson_WithActorAndTime()
    {
        await _sut.LogAsync("Participation", "p-1", AuditActions.Update,
            new Sample("RD1", 1m, DayOfWeek.Monday), new Sample("RD1", 2.5m, DayOfWeek.Monday),
            new AuditActor("u-1", "Admin"), "  změna váhy  ");

        _written.Should().NotBeNull();
        _written!.Id.Should().HaveLength(32);
        _written.Timestamp.Should().Be(new DateTime(2026, 9, 27, 10, 30, 0, DateTimeKind.Utc));
        _written.UserId.Should().Be("u-1");
        _written.UserName.Should().Be("Admin");
        _written.EntityType.Should().Be("Participation");
        _written.EntityId.Should().Be("p-1");
        _written.OldValue.Should().Be("{\"name\":\"RD1\",\"weight\":1,\"day\":\"Monday\"}");
        _written.NewValue.Should().Be("{\"name\":\"RD1\",\"weight\":2.5,\"day\":\"Monday\"}");
        _written.Reason.Should().Be("změna váhy");
    }

    [Fact]
    public async Task Create_HasNoOldValue_AndStringsAreStoredAsIs()
    {
        await _sut.LogAsync("CashBookEntry", "c-1", AuditActions.Create, null, "{\"raw\":true}", new AuditActor("u-1", null));

        _written!.OldValue.Should().BeNull();
        _written.NewValue.Should().Be("{\"raw\":true}");
        _written.Reason.Should().BeNull();
    }

    [Fact]
    public async Task Correction_RequiresReason()
    {
        var act = () => _sut.LogAsync("CashBookEntry", "c-1", AuditActions.Correction, null, null, new AuditActor("u-1", null), " ");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*reason*");
        _repo.Verify(r => r.AppendAsync(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Fact]
    public async Task UnknownAction_IsRejected()
    {
        var act = () => _sut.LogAsync("X", "1", "Rename", null, null, new AuditActor("u-1", null));

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
