namespace SantiyeAPI.DTOs;

public class AvansVerRequest
{
    public int IsciId { get; set; }
    public int PatronId { get; set; } 
    public decimal Tutar { get; set; }
    public int? SantiyeId { get; set; }
    public string OdemeTuru { get; set; } = "Nakit";
    public string? Aciklama { get; set; }
    public DateTime? Tarih { get; set; }
}