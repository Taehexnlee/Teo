using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public class OrgMember
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();

    [Required] public Guid OrgId { get; set; }

    [Required, MaxLength(200)]
    public string UserSub { get; set; } = default!; // CIAM의 sub (고정 ID)

    [Required, MaxLength(200)]
    public string UserName { get; set; } = default!;

    // "Owner" | "Member"
    [Required, MaxLength(20)]
    public string Role { get; set; } = "Member";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(OrgId))]
    public Organization? Organization { get; set; }
}