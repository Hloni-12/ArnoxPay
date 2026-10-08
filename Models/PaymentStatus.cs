namespace ArnoxPay.Models;

/// <summary>
/// Payment lifecycle. Gateway success does not mean PAID —
/// Financial Officer acceptance is required for Accepted.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Awaiting Financial Officer review (or legacy queue).</summary>
    Pending = 0,

    /// <summary>Officially paid after FO acceptance.</summary>
    Accepted = 1,

    /// <summary>Rejected by Financial Officer.</summary>
    Rejected = 2,

    /// <summary>Checkout created; customer must authenticate.</summary>
    AwaitingAuthentication = 3,

    /// <summary>Gateway processing in progress.</summary>
    Processing = 4,

    /// <summary>Authentication or processing failed.</summary>
    Failed = 5,

    /// <summary>Cancelled by customer.</summary>
    Cancelled = 6
}
