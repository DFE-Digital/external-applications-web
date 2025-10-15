using AutoFixture;
using AutoFixture.AutoNSubstitute;
using DfE.ExternalApplications.Application.Interfaces;
using DfE.ExternalApplications.Domain.Models;
using DfE.ExternalApplications.Web.Pages.FormEngine;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NSubstitute;
using Task = System.Threading.Tasks.Task;
using PageModel = DfE.ExternalApplications.Domain.Models.Page;
using TaskModel = DfE.ExternalApplications.Domain.Models.Task;

namespace Dfe.ExternalApplications.Web.UnitTests.Pages.FormEngine;

public class RenderFormModelTests
{
    private readonly IFixture _fixture;
    private readonly INavigationHistoryService _navigationHistoryService;
    private readonly RenderFormModel _model;

    public RenderFormModelTests()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization { ConfigureMembers = true });

        _fixture.Customize<Condition>(ob => ob.Without(rule => rule.Conditions));
        _fixture.Customize<CompiledPageActionDescriptor>(ob => ob
            .Without(desc => desc.HandlerMethods)
            .Without(desc => desc.Parameters)
            .Without(desc => desc.BoundProperties)
        );
        _fixture.Customize<ActionDescriptor>(ob => ob
            .Without(desc => desc.Parameters)
            .Without(desc => desc.BoundProperties)
        );

        var applicationStateService = _fixture.Create<IApplicationStateService>();
        applicationStateService.IsApplicationEditable(Arg.Any<string>()).Returns(true);
        _fixture.Register(() => applicationStateService);

        _navigationHistoryService = _fixture.Create<INavigationHistoryService>();
        _fixture.Register(() => _navigationHistoryService);

        var request = _fixture.Create<HttpRequest>();
        request.Path = PathString.Empty;
        request.QueryString = QueryString.Empty;
        _fixture.Register(() => request);

        _model = _fixture.Create<RenderFormModel>();
    }

    [Fact]
    public async Task OnPostPageAsync_when_last_form_in_task_is_submitted_then_clear_navigation_history_for_scope()
    {
        var flowId = _fixture.Create<string>();
        var instanceId = _fixture.Create<string>();
        var flowPageId = _fixture.Create<string>();

        _model.ReferenceNumber = _fixture.Create<string>();
        _model.TaskId = _fixture.Create<string>();
        _model.CurrentPageId = $"flow/{flowId}/{instanceId}/{flowPageId}";

        var firstPage = _fixture.Create<PageModel>();
        var lastPage = _fixture.Build<PageModel>()
            .With(p => p.PageId, flowPageId)
            .Create();
        var flow = _fixture.Build<MultiCollectionFlowConfiguration>()
            .With(f => f.FlowId, flowId)
            .With(f => f.Pages, [firstPage, lastPage])
            .Create();
        var summary = _fixture.Build<TaskSummaryConfiguration>()
            .With(s => s.Flows, [flow])
            .Create();
        var task = _fixture
            .Build<TaskModel>()
            .With(t => t.TaskId, _model.TaskId)
            .With(t => t.Summary, summary)
            .Create();
        _fixture.Register(() => task);

        await _model.OnPostPageAsync();

        var expectedScope = $"{_model.ReferenceNumber}:{_model.TaskId}:flow:{flowId}:{instanceId}";

        _navigationHistoryService.Received().Clear(expectedScope, Arg.Any<ISession>());
    }

    [Fact]
    public async Task OnPostPageAsync_when_form_in_task_thats_not_the_last_one_is_submitted_then_navigation_history_for_scope_is_pushed()
    {
        var flowId = _fixture.Create<string>();
        var instanceId = _fixture.Create<string>();
        var flowPageId = _fixture.Create<string>();

        _model.ReferenceNumber = _fixture.Create<string>();
        _model.TaskId = _fixture.Create<string>();
        _model.CurrentPageId = $"flow/{flowId}/{instanceId}/{flowPageId}";

        var firstPage = _fixture.Build<PageModel>()
            .With(p => p.PageId, flowPageId)
            .Create();
        var lastPage = _fixture.Create<PageModel>();
        var flow = _fixture.Build<MultiCollectionFlowConfiguration>()
            .With(f => f.FlowId, flowId)
            .With(f => f.Pages, [firstPage, lastPage])
            .Create();
        var summary = _fixture.Build<TaskSummaryConfiguration>()
            .With(s => s.Flows, [flow])
            .Create();
        var task = _fixture
            .Build<TaskModel>()
            .With(t => t.TaskId, _model.TaskId)
            .With(t => t.Summary, summary)
            .Create();
        _fixture.Register(() => task);

        await _model.OnPostPageAsync();

        var expectedScope = $"{_model.ReferenceNumber}:{_model.TaskId}:flow:{flowId}:{instanceId}";
        var expectedUrl =
            $"/applications/{_model.ReferenceNumber}/{_model.TaskId}/flow/{flowId}/{instanceId}/{flowPageId}";

        _navigationHistoryService.Received().Push(expectedScope, expectedUrl, Arg.Any<ISession>());
        _navigationHistoryService.DidNotReceive().Clear(Arg.Any<string>(), Arg.Any<ISession>());
    }
}