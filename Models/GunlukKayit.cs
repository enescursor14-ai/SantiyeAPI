using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace SantiyeAPI.Models;

public class GunlukKayit
{
    public int Id { get; set; }

    [Column(TypeName = "date")]
    public DateTime Tarih { get; set; }
    public decimal CalismaKatsayisi { get; set; }
    public decimal Yevmiye { get; set; }
    public string? Aciklama { get; set; }
    public bool OdendiMi { get; set; } = false; // 🚀 SENIOR MÜHRÜ
    public bool IsDeleted { get; set; } = false;
    

    // KİM çalıştı?
    public int IsciId { get; set; }
    public Isci? Isci { get; set; }

    // YENİ EKLENDİ: HANGİ şantiyede çalıştı?
    public int SantiyeId { get; set; }
    public Santiye? Santiye { get; set; }
}
