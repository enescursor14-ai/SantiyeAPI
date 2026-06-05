using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace SantiyeAPI.Models;

public class Avans
{
    public int Id { get; set; }
    
    [Column(TypeName = "date")]
    public DateTime Tarih { get; set; }
    public decimal Tutar { get; set; }
    public string OdemeTuru { get; set; } = "Nakit";
    public string? Aciklama { get; set; }
    public bool OdendiMi { get; set; } = false; // 🚀 SENIOR MÜHRÜ
    

    // KİME verildi?
    public int IsciId { get; set; }
    public Isci? Isci { get; set; }

    // YENİ EKLENDİ: HANGİ şantiyenin hesabına yazılacak?
    public int? SantiyeId { get; set; }
    public Santiye? Santiye { get; set; }
    // 🏗️ YENİ EKLENDİ: HANGİ KASADAN (Patrondan) ÇIKTI?
    public int KasaId { get; set; }
    public Kasa? Kasa { get; set; }

    // 🛡️ YENİ EKLENDİ: MUHASEBEDE SİLGİ KULLANILMAZ KURALI (Soft Delete)
    public bool IsDeleted { get; set; } = false;
     
    // 🚀 YENİ EKLENDİ: AUDIT TRAIL (Kim, ne zaman sildi?)
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    
}
