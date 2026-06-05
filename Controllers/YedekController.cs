using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;

namespace SantiyeAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class YedekController : ControllerBase
    {
        [HttpGet("indir")]
        public IActionResult VeritabaniniIndir([FromQuery] string securityKey)
        {
            // 🛡️ ASMA KİLİT: Şifre "Patron1453" değilse kapıdan çevir!
            // Bu şifreyi kimse bilmeyecek, sadece HTML (Javascript) bilecek.
            if (string.IsNullOrWhiteSpace(securityKey) || securityKey != "Patron1453")
            {
                return Unauthorized(new { mesaj = "Hop! Yetkisiz erişim. Güvenlik anahtarı geçersiz." });
            }

            try
            {
                // ⚠️ DİKKAT: Kendi veritabanı dosyanın adını buraya yaz
                string dbDosyaAdi = "SantiyeDB.db";
                string dbPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", dbDosyaAdi);

                if (!System.IO.File.Exists(dbPath))
                {
                    return NotFound(new { mesaj = "Veritabanı dosyası bulunamadı! Lütfen dosya adını kontrol edin." });
                }

                byte[] fileBytes;

                // 🚀 SENİOR ZIRHI: Dosya o an kullanımda olsa bile çökmeden kopyasını alır!
                using (var fileStream = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        fileStream.CopyTo(memoryStream);
                        fileBytes = memoryStream.ToArray();
                    }
                }

                string indirilecekAd = $"Santiye_Yedek_{DateTime.Now:dd_MM_yyyy_HHmm}.db";

                // Dosyayı tarayıcıya zorla indirtiyoruz
                return File(fileBytes, "application/octet-stream", indirilecekAd);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mesaj = "Yedekleme sırasında hata oluştu: " + ex.Message });
            }
        }
    }
}