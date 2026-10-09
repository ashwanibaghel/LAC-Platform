namespace LAC.Domain;

// Mathematical evidence is append-only; Title is the only editable field.
public sealed class CompensationHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid IdempotencyKey { get; set; }
    public string SubmissionHash { get; set; } = "";
    public string? Title { get; set; }
    public string InputsJson { get; set; } = "";
    public string RequestJson { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public string CalculatorVersion { get; set; } = "";
    public string ConversionVersion { get; set; } = "";
    public string OriginalAreaNotation { get; set; } = "";
    public string MarketRate { get; set; } = "";
    public string FinalAmount { get; set; } = "";
}
