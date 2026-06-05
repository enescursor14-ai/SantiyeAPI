using SantiyeAPI.Models;

public class Company
{
    public int Id { get; set; }
    public required string Name { get; set; }

    // Satın aldığı toplam aktif şantiye kotası (Örn: 2 şantiye lisansı aldı)


    public int AllowedActiveSiteCount { get; set; } = 0;

    // 🚀 Artık ConstructionSite'a değil, senin Santiye tablona bakıyor!
    public ICollection<Santiye> Santiyeler { get; set; } = new List<Santiye>();
    public string? DonanimKimligi { get; set; } // Bilgisayarın fiziksel ID'si
    public DateTime? SonIslemTarihi { get; set; } // Zaman hilesini yakalamak için
    public string? DamgaHash { get; set; }
}