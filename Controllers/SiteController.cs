using Microsoft.AspNetCore.Mvc;
using SantiyeApp.Services;
using System.Threading.Tasks;

namespace SantiyeApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SiteController : ControllerBase
    {
        private readonly SiteService _siteService;

        public SiteController(SiteService siteService)
        {
            _siteService = siteService;
        }

        [HttpGet("firma-durum/{firmaId}")]
        public async Task<IActionResult> FirmaDurumuGetir(int firmaId)
        {
            var sonuc = await _siteService.FirmaDurumGetirAsync(firmaId);

            if (!sonuc.GuvenliMi)
                return StatusCode(403, new { mesaj = sonuc.Hata });

            return Ok(new { jetonSayisi = sonuc.Jeton, firmaAdi = sonuc.FirmaAdi });
        }

        [HttpPost("ekle")]
        public async Task<IActionResult> YeniSantiyeEkle(int firmaId, string santiyeAdi, [FromQuery] string konum = "")
        {
            var result = await _siteService.SantiyeEkleAsync(firmaId, santiyeAdi, konum);

            if (!result.Basarili)
                return BadRequest(new { Mesaj = result.Mesaj });

            return Ok(new { Mesaj = result.Mesaj });
        }

        [HttpPost("lisans-uzat")]
        public async Task<IActionResult> LisansUzat(int firmaId, int santiyeId)
        {
            var result = await _siteService.LisansUzatAsync(firmaId, santiyeId);

            if (!result.Basarili)
                return BadRequest(new { Mesaj = result.Mesaj });

            return Ok(new { Mesaj = result.Mesaj });
        }

        [HttpPost("lisans-al")]
        public async Task<IActionResult> LisansAl(int firmaId, int lisansSayisi)
        {
            var result = await _siteService.LisansSatinAlAsync(firmaId, lisansSayisi);

            if (!result.Basarili)
                return BadRequest(new { Mesaj = result.Mesaj });

            return Ok(new { Mesaj = result.Mesaj });
        }

        [HttpPost("offline-jeton-yukle")]
        public async Task<IActionResult> OfflineJetonYukle([FromQuery] int firmaId, [FromQuery] string girilenKod)
        {
            var sonuc = await _siteService.OfflineJetonYukleAsync(firmaId, girilenKod);

            if (sonuc.Basarili)
                return Ok(new { mesaj = sonuc.Mesaj, yeniJetonSayisi = sonuc.YeniJeton });

            return BadRequest(new { detail = sonuc.Mesaj });
        }

        // WhatsApp mesajına Z kodunu gömmek için — eski sistem bunu kullanıyor
        [HttpGet("siber-zirh/{firmaId}")]
        public async Task<IActionResult> GetSiberZirh(int firmaId)
        {
            int zirh = await _siteService.SiberZirhKoduGetirAsync(firmaId);
            return Ok(new { zirhKodu = zirh });
        }

        // 🚀 SİSTEMİ KOMPLE KAPATMA APİSİ
        [HttpPost("sistemi-kapat")]
        public IActionResult SistemiKapat()
        {
            // C#'a kendini imha etmesi için 1 saniye süre veriyoruz ki cevabı HTML'e ulaştırabilsin
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                System.Environment.Exit(0);
            });
            
            return Ok(new { mesaj = "Sistem başarıyla kapatıldı." });
        }
    }
}