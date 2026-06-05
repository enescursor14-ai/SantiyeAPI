using System;

namespace SantiyeAPI.Models;

public class Santiye
{
    public int Id { get; set; }
    public string Ad { get; set; } = string.Empty;
    public string Konum { get; set; } = string.Empty;
    public bool AktifMi { get; set; } = true;
    public bool IsDeleted { get; set; } = false;

    
    // 🚀 YENİ EKLENEN LİSANS VE FİRMA ALANLARI
    public int? CompanyId { get; set; } 
    public DateTime? LisansBitisTarihi { get; set; }
    public Company? Company { get; set; }




    public ICollection<SantiyeIsci> SantiyeIsciler { get; set; } = new List<SantiyeIsci>();
    public ICollection<SantiyeNotu> Notlari { get; set; } = new List<SantiyeNotu>(); 

}
