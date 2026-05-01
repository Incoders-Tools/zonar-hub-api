using ZonarHub.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ZonarHub.ApiService.Endpoints.Common;

/// <summary>
/// Maps <see cref="Result"/> and <see cref="Result{TValue}"/> failures into RFC 7807 problem responses.
/// </summary>
internal static class ResultExtensions
{
    public static IResult ToProblem(this Error error) => TypedResults.Problem(
        title: TitleFor(error.Type),
        detail: error.MessageKey,
        statusCode: StatusCodeFor(error.Type),
        extensions: new Dictionary<string, object?>
        {
            ["code"] = error.Code,
        });

    public static IResult Match<TValue>(this Result<TValue> result, Func<TValue, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToProblem();

    public static IResult Match(this Result result, Func<IResult> onSuccess) =>
        result.IsSuccess ? onSuccess() : result.Error.ToProblem();

    private static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "Validation failed",
        ErrorType.NotFound => "Resource not found",
        ErrorType.Conflict => "Conflict",
        _ => "Unexpected error",
    };
}
