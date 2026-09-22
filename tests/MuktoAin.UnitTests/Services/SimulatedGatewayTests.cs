using MuktoAin.Infrastructure.Payments;

namespace MuktoAin.UnitTests.Services;

// The built-in simulated gateway: session lifecycle, test credentials,
// retry limits, and the server-to-server ValidateAsync contract.
public class SimulatedGatewayTests
{
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero));
    private readonly SimulatedGateway _gateway;

    public SimulatedGatewayTests() => _gateway = new SimulatedGateway(_time);

    private async Task<SimulatedGatewaySession> NewSessionAsync(decimal amount = 500m)
    {
        var result = await _gateway.InitSessionAsync(
            "MA-1-x", amount, "Honorarium", "https://app/s", "https://app/f", "https://app/c");
        Assert.True(result.Success);
        var key = result.GatewayPageUrl!.Split('/').Last();
        return _gateway.Find(key)!;
    }

    [Fact]
    public async Task InitSessionAsync_ReturnsCheckoutUrl_ForAnOpenSession()
    {
        var result = await _gateway.InitSessionAsync(
            "MA-1-x", 500m, "Honorarium", "https://app/s", "https://app/f", "https://app/c");

        Assert.True(result.Success);
        Assert.StartsWith("/GatewaySim/Checkout/", result.GatewayPageUrl);
        var session = _gateway.Find(result.GatewayPageUrl!.Split('/').Last());
        Assert.NotNull(session);
        Assert.Equal(SimulatedGatewayState.AwaitingCredentials, session!.State);
        Assert.Equal(500m, session.Amount);
        Assert.Equal("MA-1-x", session.TransactionId);
    }

    [Fact]
    public async Task WalletHappyPath_SucceedsAndValidates()
    {
        var s = await NewSessionAsync();

        var step1 = _gateway.SubmitCredentials(s.Key, "bkash", "01711111111", "12121");
        var step2 = _gateway.SubmitOtp(s.Key, "123456");

        Assert.Equal(SimulatedStepOutcome.Continue, step1.Outcome);
        Assert.Equal(SimulatedStepOutcome.Completed, step2.Outcome);
        Assert.Equal(SimulatedGatewayState.Succeeded, s.State);
        Assert.StartsWith("BKS", s.BankTransactionId);

        var v = await _gateway.ValidateAsync(s.ValId!);
        Assert.True(v.Success);
        Assert.Equal("MA-1-x", v.TransactionId);
        Assert.Equal(500m, v.Amount);
        Assert.Equal(s.BankTransactionId, v.BankTransactionId);
        Assert.Equal("VALID", v.Status);
    }

    [Fact]
    public async Task CardHappyPath_Succeeds()
    {
        var s = await NewSessionAsync();

        var step1 = _gateway.SubmitCredentials(s.Key, "card", "4111 1111 1111 1111", "123", "12/30");
        _gateway.SubmitOtp(s.Key, "123456");

        Assert.Equal(SimulatedStepOutcome.Continue, step1.Outcome);
        Assert.Equal(SimulatedGatewayState.Succeeded, s.State);
        Assert.StartsWith("CRD", s.BankTransactionId);
    }

    [Fact]
    public async Task ExpiredCard_IsRejectedWithRetry()
    {
        var s = await NewSessionAsync();

        var step = _gateway.SubmitCredentials(s.Key, "card", "4111111111111111", "123", "01/20");

        Assert.Equal(SimulatedStepOutcome.Retry, step.Outcome);
        Assert.Equal(SimulatedGatewayState.AwaitingCredentials, s.State);
    }

    [Fact]
    public async Task WrongPin_ThreeTimes_Fails()
    {
        var s = await NewSessionAsync();

        var a = _gateway.SubmitCredentials(s.Key, "nagad", "01711111111", "00000");
        var b = _gateway.SubmitCredentials(s.Key, "nagad", "01711111111", "00000");
        var c = _gateway.SubmitCredentials(s.Key, "nagad", "01711111111", "00000");

        Assert.Equal(SimulatedStepOutcome.Retry, a.Outcome);
        Assert.Equal(SimulatedStepOutcome.Retry, b.Outcome);
        Assert.Equal(SimulatedStepOutcome.Completed, c.Outcome);
        Assert.Equal(SimulatedGatewayState.Failed, s.State);
    }

    [Fact]
    public async Task WrongOtp_CountsTowardsTheSameLimit()
    {
        var s = await NewSessionAsync();

        _gateway.SubmitCredentials(s.Key, "rocket", "01711111111", "00000"); // 1
        _gateway.SubmitCredentials(s.Key, "rocket", "01711111111", "12121");
        _gateway.SubmitOtp(s.Key, "000000"); // 2
        var last = _gateway.SubmitOtp(s.Key, "000000"); // 3

        Assert.Equal(SimulatedStepOutcome.Completed, last.Outcome);
        Assert.Equal(SimulatedGatewayState.Failed, s.State);
    }

    [Fact]
    public async Task InsufficientBalanceWallet_FailsImmediately()
    {
        var s = await NewSessionAsync();

        var step = _gateway.SubmitCredentials(s.Key, "bkash", SimulatedGateway.InsufficientBalanceWallet, "12121");

        Assert.Equal(SimulatedStepOutcome.Completed, step.Outcome);
        Assert.Equal(SimulatedGatewayState.Failed, s.State);
    }

    [Fact]
    public async Task DeclinedCard_FailsImmediately()
    {
        var s = await NewSessionAsync();

        var step = _gateway.SubmitCredentials(s.Key, "card", SimulatedGateway.DeclinedCard, "123", "12/30");

        Assert.Equal(SimulatedStepOutcome.Completed, step.Outcome);
        Assert.Equal(SimulatedGatewayState.Failed, s.State);
    }

    [Fact]
    public async Task InvalidWalletNumber_Retries_WithoutCountingAnAttempt()
    {
        var s = await NewSessionAsync();

        var step = _gateway.SubmitCredentials(s.Key, "bkash", "12345", "12121");

        Assert.Equal(SimulatedStepOutcome.Retry, step.Outcome);
        Assert.Equal(0, s.FailedAttempts);
    }

    [Fact]
    public async Task OtpBeforeCredentials_IsRejected()
    {
        var s = await NewSessionAsync();

        var step = _gateway.SubmitOtp(s.Key, "123456");

        Assert.Equal(SimulatedStepOutcome.Invalid, step.Outcome);
        Assert.Equal(SimulatedGatewayState.AwaitingCredentials, s.State);
    }

    [Fact]
    public async Task Cancel_MarksCancelled_AndCannotBeValidated()
    {
        var s = await NewSessionAsync();

        var step = _gateway.Cancel(s.Key);

        Assert.Equal(SimulatedStepOutcome.Completed, step.Outcome);
        Assert.Equal(SimulatedGatewayState.Cancelled, s.State);
        Assert.Null(s.ValId);
    }

    [Fact]
    public async Task CompletedSession_IgnoresFurtherInput()
    {
        var s = await NewSessionAsync();
        _gateway.SubmitCredentials(s.Key, "bkash", "01711111111", "12121");
        _gateway.SubmitOtp(s.Key, "123456");

        var step = _gateway.Cancel(s.Key);

        Assert.Equal(SimulatedStepOutcome.Invalid, step.Outcome);
        Assert.Equal(SimulatedGatewayState.Succeeded, s.State);
    }

    [Fact]
    public async Task ValidateAsync_UnknownValId_Fails()
    {
        var v = await _gateway.ValidateAsync("SIMVAL-nope");

        Assert.False(v.Success);
    }

    [Fact]
    public async Task ValidateAsync_FailedSession_Fails()
    {
        var s = await NewSessionAsync();
        _gateway.SubmitCredentials(s.Key, "bkash", SimulatedGateway.InsufficientBalanceWallet, "12121");

        // A failed session never gets a val_id, so there is nothing to validate.
        Assert.Null(s.ValId);
    }

    [Fact]
    public async Task ExpiredSession_IsNotFound_AndCannotBeValidated()
    {
        var s = await NewSessionAsync();
        _gateway.SubmitCredentials(s.Key, "bkash", "01711111111", "12121");
        _gateway.SubmitOtp(s.Key, "123456");
        var valId = s.ValId!;

        _time.Advance(SimulatedGateway.SessionLifetime + TimeSpan.FromSeconds(1));

        Assert.Null(_gateway.Find(s.Key));
        Assert.False((await _gateway.ValidateAsync(valId)).Success);
    }

    [Fact]
    public void UnknownSession_StepsReturnNotFound()
    {
        Assert.Equal(SimulatedStepOutcome.NotFound, _gateway.SubmitCredentials("nope", "bkash", "01711111111", "12121").Outcome);
        Assert.Equal(SimulatedStepOutcome.NotFound, _gateway.SubmitOtp("nope", "123456").Outcome);
        Assert.Equal(SimulatedStepOutcome.NotFound, _gateway.Cancel("nope").Outcome);
    }

    [Fact]
    public async Task CallbackFor_Success_TargetsSuccessUrl_WithGatewayFields()
    {
        var s = await NewSessionAsync();
        _gateway.SubmitCredentials(s.Key, "bkash", "01711111111", "12121");
        _gateway.SubmitOtp(s.Key, "123456");

        var cb = SimulatedGateway.CallbackFor(s);

        Assert.Equal("https://app/s", cb.Url);
        Assert.Equal("MA-1-x", cb.Fields["tran_id"]);
        Assert.Equal(s.ValId, cb.Fields["val_id"]);
        Assert.Equal("500.00", cb.Fields["amount"]);
        Assert.Equal("VALID", cb.Fields["status"]);
    }

    [Fact]
    public async Task CallbackFor_FailAndCancel_TargetTheirUrls()
    {
        var failed = await NewSessionAsync();
        _gateway.SubmitCredentials(failed.Key, "bkash", SimulatedGateway.InsufficientBalanceWallet, "12121");
        var cancelled = await NewSessionAsync();
        _gateway.Cancel(cancelled.Key);

        Assert.Equal("https://app/f", SimulatedGateway.CallbackFor(failed).Url);
        Assert.Equal("FAILED", SimulatedGateway.CallbackFor(failed).Fields["status"]);
        Assert.Equal("https://app/c", SimulatedGateway.CallbackFor(cancelled).Url);
        Assert.Equal("CANCELLED", SimulatedGateway.CallbackFor(cancelled).Fields["status"]);
    }

    private class ManualTime : TimeProvider
    {
        private DateTimeOffset _now;
        public ManualTime(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
