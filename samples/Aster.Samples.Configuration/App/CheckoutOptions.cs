namespace Aster.Samples.Configuration.App;

/// <summary>
/// Ordinary options class bound from the <c>Checkout</c> section, which an <c>AppSettings</c>
/// resource provides. Nothing here knows about Aster.
/// </summary>
public sealed class CheckoutOptions
{
    /// <summary>The section name.</summary>
    public const string Section = "Checkout";

    /// <summary>The payment provider checkout calls.</summary>
    public string PaymentProvider { get; set; } = "none";

    /// <summary>Retry settings for payment calls.</summary>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>The currencies checkout accepts.</summary>
    public string[] Currencies { get; set; } = [];
}

/// <summary>Retry settings.</summary>
public sealed class RetryOptions
{
    /// <summary>The maximum number of attempts.</summary>
    public int MaxAttempts { get; set; } = 1;
}
