namespace SantiyeAPI.Models
{
    public class Kullanici
    {
        public int Id { get; set; }
        public string AdSoyad { get; set; } = string.Empty;
        public string KullaniciAdi { get; set; } = string.Empty;
        public string Sifre { get; set; } = string.Empty;
        public string Rol { get; set; } = "Sef"; // Otomatik Şef rolü
    }
}