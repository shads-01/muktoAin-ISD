using Microsoft.AspNetCore.Mvc;
using MuktoAin.Web.Models;
using Xunit;

namespace MuktoAin.UnitTests.Models;

public class ApiErrorDtoTests
{
    [Fact]
    public void Friendly_BuildsBilingualErrorShape()
    {
        var dto = ApiErrorDto.Friendly(
            "Sorry — an answer could not be generated right now. Please try again in a moment.",
            "দুঃখিত — এই মুহূর্তে উত্তর তৈরি করা যায়নি। কিছুক্ষণ পর আবার চেষ্টা করুন।");

        Assert.False(dto.Success);
        Assert.Contains("could not be generated", dto.Error);
        Assert.Contains("তৈরি করা যায়নি", dto.ErrorBn);
        Assert.Null(dto.Message);
    }

    [Fact]
    public void Friendly_OptionalDetail_IsCarriedInMessage()
    {
        var dto = ApiErrorDto.Friendly("en-text", "bn-text", detail: "CASE_NOT_FOUND");
        Assert.Equal("CASE_NOT_FOUND", dto.Message);
        Assert.Equal("en-text", dto.Error);
    }

    private class TestController : Controller { }

    [Fact]
    public void ApiErrors_Helper_SetsStatusCodeAndJsonShape()
    {
        var controller = new TestController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            },
        };

        var result = ApiErrors.BadRequest(
            controller: controller,
            errorEn: "Question and session id are required.",
            errorBn: "প্রশ্ন ও সেশন আইডি প্রয়োজন।");

        Assert.Equal(400, controller.Response.StatusCode);
        var json = Assert.IsType<JsonResult>(result);
        var value = Assert.IsType<ApiErrorDto>(json.Value);
        Assert.False(value.Success);
        Assert.Contains("প্রয়োজন", value.ErrorBn);
    }
}
