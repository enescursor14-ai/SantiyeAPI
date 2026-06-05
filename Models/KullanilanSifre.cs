namespace SantiyeAPI.Models;

/// <summary>
/// Kullanılmış şifreleri tutar.
/// Aynı şifre ikinci kez girilirse engeller.
/// </summary>
public class KullanilanSifre
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Sifre { get; set; } = string.Empty;
    public DateTime KullanımTarihi { get; set; }

    public Company? Company { get; set; }
}