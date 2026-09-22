using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using MuktoAin.Domain.Interfaces.Services;

namespace MuktoAin.Infrastructure.Payments;

// Built-in payment gateway simulation (Payments:Mode=Simulator, the default).
// Behaves like a hosted gateway: InitSessionAsync hands back a checkout page
// URL (served by GatewaySimulatorController), the citizen pays there with test
// credentials, the page posts the browser back to our success/fail/cancel URL
// with SSLCommerz-shaped fields, and ValidateAsync answers the server-to-server
// check. No real money moves. Sessions live in memory for SessionLifetime; a
// restart mid-checkout loses them and the citizen sees a failure.
//
// Test credentials:
//   wallets (bkash/nagad/rocket): any 01XXXXXXXXX number, PIN 12121
//   card: 4111 1111 1111 1111, CVV 123, any future MM/YY expiry
//   OTP 123456 for both
//   InsufficientBalanceWallet / DeclinedCard fail at once
//   MaxAttempts wrong PIN/card/OTP entries fail the session
public class SimulatedGateway : IPaymentGatewayClient
{
    public const string CheckoutPath = "/GatewaySim/Checkout/";
    public const string WalletPin = "12121";
    public const string Otp = "123456";
    public const string TestCard = "4111111111111111";
    public const string TestCardCvv = "123";
    public const string InsufficientBalanceWallet = "01700000099";
    public const string DeclinedCard = "4000000000000002";
    public const int MaxAttempts = 3;
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);

    public static readonly IReadOnlyDictionary<string, string> Methods = new Dictionary<string, string>
    {
        ["bkash"] = "BKS",
        ["nagad"] = "NGD",
        ["rocket"] = "RKT",
        ["card"] = "CRD",
    };

    private readonly ConcurrentDictionary<string, SimulatedGatewaySession> _sessions = new();
    private readonly TimeProvider _time;

    public SimulatedGateway(TimeProvider time) => _time = time;

    public Task<GatewaySessionResult> InitSessionAsync(
        string transactionId,
        decimal amount,
        string purposeLabel,
        string successUrl,
        string failUrl,
        string cancelUrl,
        GatewayCustomer? customer = null)
    {
        PruneExpired();
        var session = new SimulatedGatewaySession
        {
            Key = NewToken(),
            TransactionId = transactionId,
            Amount = amount,
            PurposeLabel = purposeLabel,
            SuccessUrl = successUrl,
            FailUrl = failUrl,
            CancelUrl = cancelUrl,
            ExpiresAt = _time.GetUtcNow() + SessionLifetime,
        };
        _sessions[session.Key] = session;
        return Task.FromResult(new GatewaySessionResult(true, CheckoutPath + session.Key, null));
    }

    public Task<GatewayValidationResult> ValidateAsync(string valId)
    {
        var session = _sessions.Values.FirstOrDefault(s => s.ValId != null && s.ValId == valId);
        if (session == null || IsExpired(session) || session.State != SimulatedGatewayState.Succeeded)
        {
            return Task.FromResult(new GatewayValidationResult(
                false, null, null, "INVALID_TRANSACTION", null, "Payment not valid"));
        }

        return Task.FromResult(new GatewayValidationResult(
            true, session.TransactionId, session.BankTransactionId, "VALID", session.Amount, null));
    }

    public SimulatedGatewaySession? Find(string key) =>
        _sessions.TryGetValue(key, out var s) && !IsExpired(s) ? s : null;

    public SimulatedStep SubmitCredentials(
        string key, string method, string account, string secret, string? cardExpiry = null)
    {
        var session = Find(key);
        if (session == null) return SimulatedStep.NotFound;

        lock (session)
        {
            if (session.State != SimulatedGatewayState.AwaitingCredentials) return SimulatedStep.Invalid;

            method = (method ?? "").Trim().ToLowerInvariant();
            if (!Methods.ContainsKey(method))
            {
                return SimulatedStep.Retry("একটি পেমেন্ট পদ্ধতি বেছে নিন।", "Choose a payment method.");
            }
            session.Method = method;
            account = new string((account ?? "").Where(char.IsDigit).ToArray());

            if (method == "card")
            {
                if (account.Length is < 12 or > 19)
                {
                    return SimulatedStep.Retry("কার্ড নম্বরটি সঠিক নয়।", "The card number is not valid.");
                }
                if (!IsFutureExpiry(cardExpiry))
                {
                    return SimulatedStep.Retry("কার্ডের মেয়াদ সঠিক নয় বা শেষ হয়ে গেছে (MM/YY)।",
                        "The card expiry is invalid or has passed (MM/YY).");
                }
                if (account == DeclinedCard)
                {
                    return Fail(session, "ব্যাংক কার্ডটি প্রত্যাখ্যান করেছে।", "The bank declined this card.");
                }
                if (account != TestCard || secret != TestCardCvv)
                {
                    return WrongEntry(session, "কার্ডের তথ্য মেলেনি।", "Card details do not match.");
                }
            }
            else
            {
                if (account.Length != 11 || !account.StartsWith("01"))
                {
                    return SimulatedStep.Retry("১১ সংখ্যার একটি মোবাইল নম্বর দিন (01XXXXXXXXX)।",
                        "Enter an 11-digit mobile number (01XXXXXXXXX).");
                }
                if (account == InsufficientBalanceWallet)
                {
                    return Fail(session, "অ্যাকাউন্টে পর্যাপ্ত ব্যালান্স নেই।", "Insufficient balance in this account.");
                }
                if (secret != WalletPin)
                {
                    return WrongEntry(session, "পিন ভুল হয়েছে।", "Wrong PIN.");
                }
            }

            session.Account = Mask(account);
            session.State = SimulatedGatewayState.AwaitingOtp;
            return new SimulatedStep(SimulatedStepOutcome.Continue, null, null);
        }
    }

    public SimulatedStep SubmitOtp(string key, string otp)
    {
        var session = Find(key);
        if (session == null) return SimulatedStep.NotFound;

        lock (session)
        {
            if (session.State != SimulatedGatewayState.AwaitingOtp) return SimulatedStep.Invalid;
            if ((otp ?? "").Trim() != Otp)
            {
                return WrongEntry(session, "ওটিপি ভুল হয়েছে।", "Wrong OTP.");
            }

            session.State = SimulatedGatewayState.Succeeded;
            session.ValId = "SIMVAL-" + NewToken();
            session.BankTransactionId = Methods[session.Method!] +
                RandomNumberGenerator.GetInt32(0, 1_000_000_000).ToString("D10", CultureInfo.InvariantCulture);
            return SimulatedStep.Completed;
        }
    }

    public SimulatedStep Cancel(string key)
    {
        var session = Find(key);
        if (session == null) return SimulatedStep.NotFound;

        lock (session)
        {
            if (session.State is not (SimulatedGatewayState.AwaitingCredentials or SimulatedGatewayState.AwaitingOtp))
            {
                return SimulatedStep.Invalid;
            }
            session.State = SimulatedGatewayState.Cancelled;
            return SimulatedStep.Completed;
        }
    }

    // Where the browser goes when the session is finished, and which fields it
    // posts (the same names SSLCommerz posts to its merchants).
    public static SimulatedCallback CallbackFor(SimulatedGatewaySession s)
    {
        var (url, status) = s.State switch
        {
            SimulatedGatewayState.Succeeded => (s.SuccessUrl, "VALID"),
            SimulatedGatewayState.Cancelled => (s.CancelUrl, "CANCELLED"),
            _ => (s.FailUrl, "FAILED"),
        };
        var fields = new Dictionary<string, string>
        {
            ["tran_id"] = s.TransactionId,
            ["val_id"] = s.ValId ?? "",
            ["amount"] = s.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            ["currency"] = "BDT",
            ["status"] = status,
            ["bank_tran_id"] = s.BankTransactionId ?? "",
            ["card_type"] = s.Method ?? "",
        };
        return new SimulatedCallback(url, fields);
    }

    private static SimulatedStep WrongEntry(SimulatedGatewaySession s, string bn, string en)
    {
        s.FailedAttempts++;
        if (s.FailedAttempts >= MaxAttempts)
        {
            return Fail(s, "অনেকবার ভুল তথ্য দেওয়া হয়েছে।", "Too many wrong attempts.");
        }
        var left = MaxAttempts - s.FailedAttempts;
        return SimulatedStep.Retry($"{bn} আর {left} বার চেষ্টা করা যাবে।", $"{en} {left} attempt(s) left.");
    }

    private static SimulatedStep Fail(SimulatedGatewaySession s, string bn, string en)
    {
        s.State = SimulatedGatewayState.Failed;
        s.FailureReasonBn = bn;
        s.FailureReasonEn = en;
        return SimulatedStep.Completed;
    }

    private bool IsFutureExpiry(string? mmYy)
    {
        var parts = (mmYy ?? "").Split('/');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var month) || month is < 1 or > 12
            || !int.TryParse(parts[1], out var year) || year is < 0 or > 99)
        {
            return false;
        }
        var now = _time.GetUtcNow();
        var lastDay = new DateTime(2000 + year, month, 1).AddMonths(1);
        return lastDay > now.UtcDateTime;
    }

    private bool IsExpired(SimulatedGatewaySession s) => _time.GetUtcNow() > s.ExpiresAt;

    private void PruneExpired()
    {
        foreach (var s in _sessions.Values.Where(IsExpired).ToList())
        {
            _sessions.TryRemove(s.Key, out _);
        }
    }

    private static string Mask(string digits) =>
        digits.Length <= 4 ? digits : new string('•', digits.Length - 4) + digits[^4..];

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}

public enum SimulatedGatewayState
{
    AwaitingCredentials,
    AwaitingOtp,
    Succeeded,
    Failed,
    Cancelled,
}

public class SimulatedGatewaySession
{
    public string Key { get; init; } = "";
    public string TransactionId { get; init; } = "";
    public decimal Amount { get; init; }
    public string PurposeLabel { get; init; } = "";
    public string SuccessUrl { get; init; } = "";
    public string FailUrl { get; init; } = "";
    public string CancelUrl { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }

    public SimulatedGatewayState State { get; set; } = SimulatedGatewayState.AwaitingCredentials;
    public string? Method { get; set; }
    public string? Account { get; set; } // masked
    public int FailedAttempts { get; set; }
    public string? ValId { get; set; }
    public string? BankTransactionId { get; set; }
    public string? FailureReasonBn { get; set; }
    public string? FailureReasonEn { get; set; }

    public bool IsFinished => State is SimulatedGatewayState.Succeeded
        or SimulatedGatewayState.Failed or SimulatedGatewayState.Cancelled;
}

public enum SimulatedStepOutcome
{
    Continue,   // step accepted, show the next step
    Retry,      // wrong input, same step again (see message)
    Completed,  // session finished; post the browser back to the merchant
    Invalid,    // step not allowed in the session's current state
    NotFound,   // unknown or expired session
}

public record SimulatedStep(SimulatedStepOutcome Outcome, string? MessageBn, string? MessageEn)
{
    public static readonly SimulatedStep Completed = new(SimulatedStepOutcome.Completed, null, null);
    public static readonly SimulatedStep Invalid = new(SimulatedStepOutcome.Invalid, null, null);
    public static readonly SimulatedStep NotFound = new(SimulatedStepOutcome.NotFound, null, null);

    public static SimulatedStep Retry(string bn, string en) => new(SimulatedStepOutcome.Retry, bn, en);
}

public record SimulatedCallback(string Url, IReadOnlyDictionary<string, string> Fields);
