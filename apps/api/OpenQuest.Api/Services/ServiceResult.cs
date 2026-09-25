namespace OpenQuest.Api.Services;

public sealed record ApiError(int Status, string Code, string? Message = null, object? Details = null);

public readonly record struct ServiceResult<T>(T? Value, ApiError? Error)
{
    public bool Ok => Error is null;
    public static ServiceResult<T> Success(T value) => new(value, null);
    public static ServiceResult<T> Fail(int status, string code, string? message = null, object? details = null)
        => new(default, new ApiError(status, code, message, details));

    public IResult ToHttp(Func<T, IResult> onOk)
        => Error is { } e
            ? Results.Json(new { error = e.Code, message = e.Message, details = e.Details }, statusCode: e.Status)
            : onOk(Value!);
}
