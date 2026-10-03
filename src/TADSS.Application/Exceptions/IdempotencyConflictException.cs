namespace TADSS.Application.Exceptions;

public class IdempotencyConflictException : Exception
{
    public string IdempotencyKey { get; }

    public IdempotencyConflictException(string idempotencyKey, string message)
        : base(message)
    {
        IdempotencyKey = idempotencyKey;
    }
}
