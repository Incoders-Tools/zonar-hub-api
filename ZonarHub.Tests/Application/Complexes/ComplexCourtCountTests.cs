using System.Text.Json;
using ZonarHub.Application.Features.Complexes;
using ZonarHub.Application.Features.Complexes.Update;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Organizations;
using Xunit;

namespace ZonarHub.Tests.Application.Complexes;

public class ComplexCourtCountTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(0)]
    public void ResponseProjectsCountAsCamelCase(int count)
    {
        var complex = Complex.Reconstitute(new ComplexId(Guid.NewGuid()), new OrganizationId(Guid.NewGuid()),
            "Venue", null, "Address", null, null, 0, 0, null, null, null, true,
            DateTime.UtcNow, DateTime.UtcNow, count);
        var response = ComplexResponse.FromDomain(complex);
        Assert.Equal(count, response.CourtCount);
        Assert.Equal(count, JsonDocument.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            .RootElement.GetProperty("courtCount").GetInt32());
    }

    [Fact]
    public async Task UpdatePreservesExistingCourtCountInResponse()
    {
        var id = new ComplexId(Guid.NewGuid());
        var repository = new InMemoryComplexRepository(new InMemoryComplexStore());
        var existing = Complex.Reconstitute(id, new OrganizationId(Guid.NewGuid()),
            "Venue", null, "Address", null, null, 0, 0, null, null, null, true,
            DateTime.UtcNow, DateTime.UtcNow, 4);
        await repository.AddAsync(existing);

        var result = await new UpdateComplexHandler(repository).Handle(
            new UpdateComplexCommand(id.Value, "Renamed", null, "New address", null, null,
                0, 0, null, null, null, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.CourtCount);
        Assert.Equal(4, (await repository.GetByIdAsync(id))!.CourtCount);
    }

    [Fact]
    public void ExistingCreateDefaultsToZero()
    {
        var result = Complex.Create(new ComplexId(Guid.NewGuid()), new OrganizationId(Guid.NewGuid()),
            "Venue", null, "Address", null, null, 0, 0, null, null, null, DateTime.UtcNow);
        Assert.Equal(0, ComplexResponse.FromDomain(result.Value).CourtCount);
    }
}
