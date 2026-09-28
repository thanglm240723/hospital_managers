namespace QuanLyBenhVien.Application.Common.Results;

public class Result
{
    protected Result(Error? error)
    {
        if (error is null)
        {
            IsSuccess = true;
        }
        else
        {
            IsSuccess = false;
            Error = error;
        }
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error? Error { get; }

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, null);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, error);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, Error? error) : base(error)
    {
        _value = value;
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    public static implicit operator Result<TValue>(TValue value) => new(value, null);

    public static implicit operator Result<TValue>(Error error) => new(default, error);
}
