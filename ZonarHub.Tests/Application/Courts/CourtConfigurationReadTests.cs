using MediatR;
using System.Reflection;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Courts.GetAll;
using ZonarHub.Application.Features.Courts.GetByComplexId;
using ZonarHub.Application.Features.Courts.GetById;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Courts;
using Xunit;

namespace ZonarHub.Tests.Application.Courts;

public class CourtConfigurationReadTests
{
    [Theory]
    [InlineData("GetAll", true)]
    [InlineData("GetById", true)]
    [InlineData("GetByComplexId", true)]
    [InlineData("GetAll", false)]
    [InlineData("GetById", false)]
    [InlineData("GetByComplexId", false)]
    public async Task ReadProjectsCourtConfiguration(string route, bool configured)
    {
        var id = new CourtId(Guid.NewGuid());
        var complexId = new ComplexId(Guid.NewGuid());
        var sportIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var created = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var court = configured
            ? Court.Reconstitute(id, complexId, "Court", true, created, true, "hard", sportIds)
            : Court.Reconstitute(id, complexId, "Court", true, created);
        var repository = new StubRepository(court);

        CourtDto dto = route switch
        {
            "GetAll" => Assert.Single(await Handler<GetAllCourtsQuery, IReadOnlyList<CourtDto>>("ZonarHub.Application.Features.Courts.GetAll.GetAllCourtsHandler", repository).Handle(new GetAllCourtsQuery(), CancellationToken.None)),
            "GetById" => (await Handler<GetCourtByIdQuery, ZonarHub.Domain.Common.Result<CourtDto>>("ZonarHub.Application.Features.Courts.GetById.GetCourtByIdHandler", repository).Handle(new GetCourtByIdQuery(id.Value), CancellationToken.None)).Value,
            _ => Assert.Single(await Handler<GetCourtsByComplexIdQuery, IReadOnlyList<CourtDto>>("ZonarHub.Application.Features.Courts.GetByComplexId.GetCourtsByComplexIdHandler", repository).Handle(new GetCourtsByComplexIdQuery(complexId.Value), CancellationToken.None))
        };

        Assert.Equal(id.Value, dto.Id);
        Assert.Equal(complexId.Value, dto.ComplexId);
        Assert.Equal(configured, dto.IsIndoor);
        Assert.Equal(configured ? "hard" : null, dto.SurfaceType);
        Assert.Equal(configured ? sportIds : [], dto.SportIds);
    }

    private static IRequestHandler<TRequest, TResponse> Handler<TRequest, TResponse>(string typeName, ICourtRepository repository)
        where TRequest : IRequest<TResponse>
    {
        var type = typeof(GetAllCourtsQuery).Assembly.GetType(typeName, throwOnError: true)!;
        return (IRequestHandler<TRequest, TResponse>)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [repository], null)!;
    }

    private sealed class StubRepository(Court court) : ICourtRepository
    {
        public Task<IReadOnlyList<Court>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Court>>([court]);
        public Task<Court?> GetByIdAsync(CourtId id, CancellationToken cancellationToken = default) => Task.FromResult<Court?>(court);
        public Task<IReadOnlyList<Court>> ListByComplexIdAsync(ComplexId id, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Court>>([court]);
        public Task AddAsync(Court value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Update(Court value) => throw new NotSupportedException();
        public void Remove(Court value) => throw new NotSupportedException();
    }
}
