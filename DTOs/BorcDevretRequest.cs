public class BorcDevretRequest
{
    public int IsciId { get; set; }
    public required string Ay { get; set; } // Örn: "2026-03"
    public decimal BorcTutari { get; set; }
    public int PatronId { get; set; }
    public int? SantiyeId { get; set; } // 🚀 YENİ EKLENDİ
}