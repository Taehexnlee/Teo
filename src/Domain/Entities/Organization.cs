namespace Domain.Entities;

public class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public DateTime CreatedAt { get; set; }

    // 🔐 소유자(작성자) 정보
    public string? CreatedBy { get; set; }       // OID or sub
    public string? CreatedByName { get; set; }   // 표시용 이름/메일
}