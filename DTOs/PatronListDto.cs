namespace SantiyeAPI.DTOs;

public class PatronListDto
{
    public int Id { get; set; }
    public string Ad { get; set; } = string.Empty;
    public string Soyad { get; set; } = string.Empty;
    public string Telefon { get; set; } = string.Empty;
    public string Unvan { get; set; } = string.Empty;
    public int? KasaId { get; set; }
    public int? SorumluOlduguSantiyeId { get; set; }
    public string SantiyeAd { get; set; } = string.Empty;
    public decimal Bakiye { get; set; }
}