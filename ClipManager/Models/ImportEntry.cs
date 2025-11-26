using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClipManager.Models;

[Table("imports")]
public class ImportEntry
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    public string? Name { get; set; }

    [Column("imported_at")]
    public DateTime? ImportedAt { get; set; }

    [Column("imported_by")]
    public string? ImportedBy { get; set; }

    [Column("path")]
    public string? Path { get; set; }

    [Column("entry_count")]
    public int EntryCount { get; set; }

    [Column("workstation")]
    public string? Workstation { get; set; }
}