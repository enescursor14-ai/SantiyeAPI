using SantiyeAPI.Models;

public class Patron
{
    public int Id { get; set; }
    public string Ad { get; set; } = string.Empty; // Baban ve 3 Ortağı
    public string Soyad { get; set; } = string.Empty;
    public string Telefon { get; set; } = string.Empty;

    public string Unvan { get; set; } = string.Empty; // "Yönetici Ortak" vb.
    public int? KasaId { get; set; }
    public Kasa? Kasa { get; set; }
    public int? SorumluOlduguSantiyeId { get; set; }
    public virtual Santiye? SorumluOlduguSantiye { get; set; }
    // Patronun yaptığı tüm harcamalar
    public ICollection<KasaHareketi> Harcamalar { get; set; } = new List<KasaHareketi>();
}