using System.Text.Json;
using FdeTracker.Api.Data;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Repositories;

public sealed class CatalogRepository
{
    public Task<List<StackView>> Stacks(Session s) => s.Query<StackView>("select id, label from stacks order by sort");
    public Task<List<TopicView>> Topics(Session s) => s.Query<TopicView>("select slug, title, description from topics order by sort");
    public Task<int> Ping(Session s) => s.Scalar<int>("select 1");
}

/// <summary>Append-only audit trail. Runs on the caller's session so it commits together with the change it records.</summary>
public sealed class AuditRepository
{
    public Task Record(Session s, Guid actor, string action, string target, object? detail = null) =>
        s.Exec(
            "insert into audit_log (actor_id, action, target, detail) values (@actor, @action, @target, @detail::jsonb)",
            new { actor, action, target, detail = detail is null ? null : JsonSerializer.Serialize(detail) });
}
