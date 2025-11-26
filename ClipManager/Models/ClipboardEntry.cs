using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;

namespace ClipManager.Models;

[Table("clip")]
[Index(nameof(ContentHash), IsUnique = true)]
public class ClipboardEntry
{
    private string? _data;
    private string? _imagePath;
    private DateTime _timestamp;

    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("data")]
    public string? Data
    {
        get => _data;
        set
        {
            _data = value;
            ContentHash = ComputeHash();
        }
    }

    [Column("image_path")]
    public string? ImagePath
    {
        get => _imagePath;
        set
        {
            _imagePath = value;
            ContentHash = ComputeHash();
        }
    }

    [Column("username")]
    public string? Username { get; set; }

    [Column("workstation")]
    public string? Workstation { get; set; }

    [Column("week")]
    public string? Week { get; set; }

    [Column("timestamp")]
    public DateTime Timestamp
    {
        get => _timestamp;
        set
        {
            _timestamp = value;
            ContentHash = ComputeHash();
        }
    }

    [Column("content_hash"), MaxLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    public string ComputeHash()
    {
        var input = $"{Data ?? ""}|{ImagePath ?? ""}|{Timestamp:yyyy-MM-dd HH:mm:ss}";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}