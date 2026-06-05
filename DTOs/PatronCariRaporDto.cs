namespace SantiyeAPI.DTOs
{
    public class PatronCariRaporDto
    {
        public string PatronAd { get; set; } = string.Empty;
        
        // 🚀 İŞTE YENİ MUHASEBE MOTORUNUN İHTİYAÇ DUYDUĞU ALANLAR
        public decimal ToplamSermaye { get; set; } // Kasaya koyduğu para (Giriş)
        public decimal ToplamCikis { get; set; }   // Şantiyeye harcadığı (Çıkış)
        
        public decimal ToplamHarcama { get; set; } // Net Durumu (Giriş - Çıkış)
        
        // Patronun şantiye şantiye harcama dökümü
        public List<SantiyeHarcamaDto> SantiyeBazli { get; set; } = new();
    }

    public class SantiyeHarcamaDto
    {
        public string SantiyeAd { get; set; } = string.Empty;
        public decimal Harcama { get; set; } // O şantiyedeki Net Durum
    }
}