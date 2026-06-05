using System.ComponentModel.DataAnnotations; // 🚀 SENIOR KANTARI İÇİN GEREKLİ KÜTÜPHANE

namespace SantiyeAPI.DTOs // Kendi namespace'ine göre ayarla
{
    public class BordroOzetDto
    {
        public int Id { get; set; }
        public string AdSoyad { get; set; } = string.Empty;
        public string Meslek { get; set; } = string.Empty;
        public decimal Yevmiye { get; set; }

        // Excel bu kolonu okuyup "Selimiye, Kadıköy" diye parçalayacak.
        public string SantiyeAd { get; set; } = string.Empty;

        // --- GÜN BİLGİLERİ ---
        public decimal TamGun { get; set; }
        public decimal YarimGun { get; set; }

        // 🚀 BURALAR EKLENDİ (Şantiye Jargonu Sütunları İçin)
        public decimal MesailiGun { get; set; }
        public decimal CiftYevmiyeGun { get; set; }

        public decimal GelmediGun { get; set; }

        // --- HESAP KİTAP ---
        public decimal AldigiAvans { get; set; }
        public decimal Hakedis { get; set; }
        public int CalistigiSantiyeSayisi { get; set; }
        public List<SantiyeGunDetay> SantiyeDetaylari
        {
            get;
            set;
        } = new();
        // Hem BordroOzetDto içine, hem de HesapKesimDto içine ekle patron:
        public string? DonemBaslangic { get; set; }
        public string? DonemBitis { get; set; }
        public bool IsMuhurlu { get; set; }
    }

    // Hesap Kesmek İçin Gelecek Olan Paket
    public class HesapKesimDto
    {
        [Required(ErrorMessage = "İşçi ID boş gönderilemez!")]
        public int IsciId { get; set; }

        // 🚧 40 YILLIK SENIOR KANTARI: Format kesinlikle YYYY-MM olmak ZORUNDA!
        [Required(ErrorMessage = "Ay bilgisi boş gönderilemez!")]
        [RegularExpression(@"^\d{4}-\d{2}$", ErrorMessage = "Hop patron! Ay formatı hatalı. Sisteme sadece '2026-02' formatında veri gönderebilirsin.")]
        public string Ay { get; set; } = string.Empty; // Örn: "2026-02"
        [Required]
        public int PatronId { get; set; } // 🚀 YENİ: Parayı veren patron (Baban veya Ortağı)

        [Required]
        public int SantiyeId { get; set; } // Harcamanın yazılacağı şantiye

        // Hem BordroOzetDto içine, hem de HesapKesimDto içine ekle patron:
        public String? DonemBaslangic { get; set; }  // ✅ YENİ
        public String? DonemBitis { get; set; }       // ✅ YENİ


    }
    public class SantiyeGunDetay
    {
        public int? SantiyeId { get; set; } // 🚀 GERİ GELDİ! (JavaScript buraya bakıyor)
        public string SantiyeAd { get; set; } = string.Empty;
        public decimal Hakedis { get; set; }
        public int TamGun { get; set; }
        public int YarimGun { get; set; }
        public int MesailiGun { get; set; }
        public int CiftYevmiyeGun { get; set; }
        public int GelmediGun { get; set; }
        public decimal AldigiAvans { get; set; }
        public decimal OdenmemisHakedis { get; set; }
        public decimal OdenmemisAvans { get; set; }
        public decimal Bakiye { get; set; }
    }
}