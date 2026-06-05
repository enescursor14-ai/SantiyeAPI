using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SantiyeAPI.Data;

namespace SantiyeAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AuthController(AppDbContext context)
        {
            _context = context;
        }

        public class LoginIstegi
        {
            public string KullaniciAdi { get; set; } = string.Empty;
            public string Sifre { get; set; } = string.Empty;
        }

        [HttpPost("Giris")]
        public async Task<IActionResult> GirisYapanAdamKim([FromBody] LoginIstegi form)
        {
            // Veritabanında bu isim ve şifreye sahip biri var mı bakıyoruz
            var adam = await _context.Kullanicilar
                .FirstOrDefaultAsync(u => u.KullaniciAdi == form.KullaniciAdi && u.Sifre == form.Sifre);

            if (adam == null)
            {
                return Unauthorized(new { mesaj = "Yanlış kullanıcı adı veya şifre girdin patron!" });
            }

            // Adamı bulduk! Kimliğini HTML tarafına gönderiyoruz.
            return Ok(new
            {
                mesaj = "Hoş geldin " + adam.AdSoyad,
                kullaniciId = adam.Id,
                adSoyad = adam.AdSoyad,
                rol = adam.Rol,
                firmaId = adam.Id 
            });
        }
    }
}