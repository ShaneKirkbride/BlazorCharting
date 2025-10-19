using System;
using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Workers;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Tests.Workers;

public sealed class TtlCleanupServiceTests
{
    [Fact]
    public void Constructor_ValidatesOptions()
    {
        var repository = new StubMeasurementRepository();
        var options = Options.Create(new TtlWorkerOptions { HistoryRetention = TimeSpan.Zero });

        Assert.Throws<ArgumentOutOfRangeException>(() => new TtlCleanupService(repository, options));
    }

    [Fact]
    public void Cleanup_DeletesUsingConfiguredRetention()
    {
        var repository = new StubMeasurementRepository();
        repository.ReturnedDeletedCount = 3;
        var retention = TimeSpan.FromHours(2);
        var options = Options.Create(new TtlWorkerOptions { HistoryRetention = retention });
        var service = new TtlCleanupService(repository, options);
        var nowUtc = new DateTime(2024, 11, 5, 8, 0, 0, DateTimeKind.Utc);

        var result = service.Cleanup(nowUtc);

        Assert.Equal(nowUtc - retention, repository.LastCutoffUtc);
        Assert.Equal(3, result.DeletedCount);
        Assert.Equal(repository.LastCutoffUtc, result.CutoffUtc);
    }

    private sealed class StubMeasurementRepository : IMeasurementRepository
    {
        public DateTime? LastCutoffUtc { get; private set; }
        public int ReturnedDeletedCount { get; set; }

        public void AppendHistory(FilteredMeasurement f)
            => throw new NotSupportedException();

        public void UpsertLatest(FilteredMeasurement f)
            => throw new NotSupportedException();

        public int DeleteHistoryOlderThan(DateTime cutoffUtc)
        {
            LastCutoffUtc = cutoffUtc;
            return ReturnedDeletedCount;
        }
    }
}
