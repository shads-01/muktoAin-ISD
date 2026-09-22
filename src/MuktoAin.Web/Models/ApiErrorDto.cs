using Microsoft.AspNetCore.Mvc;

namespace MuktoAin.Web.Models;

/// <summary>
/// Unified JSON error shape for the fetch-based endpoints (Chat, Payment, Case
/// SubmitOptions, Document). Client JS reads `success` and renders `error`
/// (English) or `errorBn` (Bangla) per the mkt-lang toggle. `message` carries a
/// short non-sensitive detail code — NEVER raw exception text (A-3.12: no
/// internal error leakage to the client).
/// </summary>
public record ApiErrorDto(bool Success, string Error, string? ErrorBn = null, string? Message = null)
{
    public static ApiErrorDto Friendly(string errorEn, string errorBn, string? detail = null) =>
        new(false, errorEn, errorBn, detail);
}

public static class ApiErrors
{
    public const string AiUnavailableEn =
        "Sorry — an answer could not be generated right now. Please try again in a moment.";
    public const string AiUnavailableBn =
        "দুঃখিত — এই মুহূর্তে উত্তর তৈরি করা যায়নি। কিছুক্ষণ পর আবার চেষ্টা করুন।";

    public const string PaymentFailedEn =
        "The payment could not be completed. No amount was charged — please try again.";
    public const string PaymentFailedBn =
        "পেমেন্ট সম্পন্ন করা যায়নি। আপনার কোনো টাকা কাটা হয়নি — কিছুক্ষণ পর আবার চেষ্টা করুন।";

    public static JsonResult BadRequest(Controller controller, string errorEn, string errorBn, string? detail = null) =>
        Result(controller, 400, errorEn, errorBn, detail);

    public static JsonResult NotFound(Controller controller, string errorEn, string errorBn, string? detail = null) =>
        Result(controller, 404, errorEn, errorBn, detail);

    public static JsonResult ServerError(Controller controller, string errorEn, string errorBn, string? detail = null) =>
        Result(controller, 500, errorEn, errorBn, detail);

    private static JsonResult Result(Controller controller, int statusCode, string errorEn, string errorBn, string? detail)
    {
        controller.Response.StatusCode = statusCode;
        return new JsonResult(ApiErrorDto.Friendly(errorEn, errorBn, detail));
    }
}
