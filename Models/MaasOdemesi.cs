using System;

namespace SantiyeAPI.Models;

public class MaasOdemesi
{
    public int Id { get; set; }
    
    public int IsciId { get; set; }
    public Isci? Isci { get; set; }

    public int KasaId { get; set; }
    public Kasa? Kasa { get; set; }

    public int? SantiyeId { get; set; }
    public Santiye? Santiye { get; set; }

    public decimal Tutar { get; set; }
    
    // 🚀 SENIOR KURALI 3: Sunucu nerede olursa olsun zaman kayması yaşanmaz!
    public DateTime IslemTarihi { get; set; } 
    public string? Aciklama { get; set; }

    // 🛡️ AUDIT TRAIL (Soft Delete)
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}