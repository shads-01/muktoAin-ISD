using Microsoft.Extensions.Logging;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using Moq;

namespace MuktoAin.UnitTests.Services;

// AUD-7: the audit service is contractually fail-safe — an audit-write failure
// (DB down, transient fault) must never break the admin action that triggered it.
public class AdminAuditServiceTests
{
    private readonly Mock<IRepository<AdminAuditLog>> _repo = new();
    private readonly AdminAuditService _service;

    public AdminAuditServiceTests()
    {
        _service = new AdminAuditService(_repo.Object, Mock.Of<ILogger<AdminAuditService>>());
    }

    [Fact]
    public async Task LogAdminActionAsync_WritesRowWithAllFields()
    {
        AdminAuditLog? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<AdminAuditLog>()))
            .Callback<AdminAuditLog>(l => captured = l)
            .Returns(Task.CompletedTask);

        await _service.LogAdminActionAsync(1, "SuspendUser",
            targetUserId: 5, targetEntityId: null, details: "spam account");

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.AdminUserId);
        Assert.Equal("SuspendUser", captured.Action);
        Assert.Equal(5, captured.TargetUserId);
        Assert.Null(captured.TargetEntityId);
        Assert.Equal("spam account", captured.Details);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task LogAdminActionAsync_WhenRepoThrows_DoesNotPropagate()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<AdminAuditLog>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        // Must complete without throwing — audit failure can never break the
        // admin action it records.
        await _service.LogAdminActionAsync(1, "RefundOrder", targetEntityId: 9);
    }
}
