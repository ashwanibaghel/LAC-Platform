namespace LAC.Domain;

// A canonical ID relationship. Legacy Matter.KhasraReferenceText never creates this link.
public sealed class MatterKhasra
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatterId { get; set; }
    public Matter Matter { get; set; } = null!;
    public Guid KhasraId { get; set; }
    public Khasra Khasra { get; set; } = null!;
}
