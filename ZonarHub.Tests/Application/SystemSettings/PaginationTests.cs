using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.SystemSettings.Create;
using ZonarHub.Application.Features.SystemSettings.GetAll;
using ZonarHub.Domain.SystemSettings;

namespace ZonarHub.Tests.Application.SystemSettings;

public class PaginationTests
{
    private static readonly DateTime Now = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PageResult_ComputesTotalsAndFlags()
    {
        var page = new PageResult<int>(new[] { 1, 2, 3 }, Page: 2, PageSize: 3, TotalCount: 10);
        Assert.Equal(4, page.TotalPages);
        Assert.True(page.HasPreviousPage);
        Assert.True(page.HasNextPage);
    }

    [Fact]
    public void PageResult_LastPage_HasNoNext()
    {
        var page = new PageResult<int>(new[] { 1 }, Page: 4, PageSize: 3, TotalCount: 10);
        Assert.False(page.HasNextPage);
        Assert.True(page.HasPreviousPage);
    }

    [Fact]
    public void PageRequest_Normalize_ClampsToValidRange()
    {
        var normalized = new PageRequest(Page: 0, PageSize: -5).Normalize();
        Assert.Equal(PageRequest.DefaultPage, normalized.Page);
        Assert.Equal(PageRequest.DefaultPageSize, normalized.PageSize);

        var oversized = new PageRequest(Page: 1, PageSize: 1000).Normalize();
        Assert.Equal(PageRequest.MaxPageSize, oversized.PageSize);
    }

    [Fact]
    public async Task ListHandler_PagesAndReportsTotals()
    {
        var h = new SystemSettingsTestHarness(Now);
        for (var i = 0; i < 5; i++)
        {
            await h.Create.Handle(
                new CreateSystemSettingCommand($"key.{i:D2}", "v", SystemSettingScope.Global, null, null),
                CancellationToken.None);
        }

        var page1 = await h.List.Handle(new GetSystemSettingsQuery(new SystemSettingsFilter(Page: 1, PageSize: 2)), CancellationToken.None);
        var page2 = await h.List.Handle(new GetSystemSettingsQuery(new SystemSettingsFilter(Page: 2, PageSize: 2)), CancellationToken.None);
        var page3 = await h.List.Handle(new GetSystemSettingsQuery(new SystemSettingsFilter(Page: 3, PageSize: 2)), CancellationToken.None);

        Assert.Equal(5, page1.Value.TotalCount);
        Assert.Equal(2, page1.Value.Items.Count);
        Assert.Equal(3, page1.Value.TotalPages);
        Assert.True(page1.Value.HasNextPage);
        Assert.False(page1.Value.HasPreviousPage);

        Assert.Equal(2, page2.Value.Items.Count);
        Assert.True(page2.Value.HasPreviousPage);

        Assert.Single(page3.Value.Items);
        Assert.False(page3.Value.HasNextPage);
    }
}
