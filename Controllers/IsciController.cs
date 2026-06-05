namespace SantiyeAPI.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SantiyeAPI.Data;
using SantiyeAPI.Models; // SantiyeIsci ara tablosunu tanım
using SantiyeAPI.DTOs;
using SantiyeAPI.Services;

[ApiController]
[Route("api/[controller]")]
public class IsciController : ControllerBase
{
    private readonly IIsciService _isciService;
    private readonly AppDbContext _context;

    public IsciController(IIsciService isciService, AppDbContext context)
    {
        _isciService = isciService;
        _context = context;
    }

    /// <summary>
    /// İşçileri sayfalayarak hafif DTO formatında listeler.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] string? aramaKelimesi,
        [FromQuery] string? santiyeFiltre, // 🚀 YENİ EKLENDİ
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken cancellationToken = default)
    {
        // Service'e santiyeFiltre'yi de yolluyoruz
        var result = await _isciService.GetAllAsync(aramaKelimesi, santiyeFiltre, pageNumber, pageSize, cancellationToken);
        return Ok(result);
    }


    /// <summary>
    /// Belirtilen ID'ye sahip işçinin tüm detaylarını (Avans, Puantaj vb.) getirir.
    /// </summary>
    [HttpGet("{id:int:min(1)}", Name = "GetIsciById")] // 🛡️ Sadece 1 ve üzeri ID'lere izin ver
    public async Task<ActionResult<IsciDetailDto>> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        // Zaten 'min(1)' sayesinde negatif ID'ler buraya kadar gelemez ama temiz kod olsun.
        var isci = await _isciService.GetByIdAsync(id, cancellationToken);

        if (isci == null)
        {
            return NotFound(new { Mesaj = $"Aradığınız işçi bulunamadı veya sistemden silinmiş." });
        }

        return Ok(isci);
    }

    /// <summary>
    /// Yeni bir işçi kaydı oluşturur.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<IsciDetailDto>> CreateAsync(
        [FromBody] IsciCreateDto createDto,
        CancellationToken cancellationToken)
    {
        var olusturulanIsci = await _isciService.CreateAsync(createDto, cancellationToken);

        // 201 Created döner ve Response Header'a yeni kaynağın URI adresini ekler.
        // Artık o isimlendirdiğimiz sarsılmaz adresi çağırıyoruz
        return CreatedAtRoute("GetIsciById", new { id = olusturulanIsci.Id }, olusturulanIsci);
    }

    /// <summary>
    /// Mevcut bir işçinin bilgilerini günceller.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAsync(
        int id,
        [FromBody] IsciUpdateDto updateDto,
        CancellationToken cancellationToken)
    {
        var sonuc = await _isciService.UpdateAsync(id, updateDto, cancellationToken);
        if (!sonuc) return NotFound(new { Mesaj = $"Güncellenecek işçi bulunamadı (ID: {id})." });

        return NoContent(); // 204 Başarılı ama dönülecek veri yok
    }



    /*[HttpPost("{isciId}/hesap-kapat")]
    public async Task<IActionResult> HesapKapat(int isciId, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Servisteki mühürleme ustasını çağır
            await _isciService.HesapKapatAsync(isciId, cancellationToken);

            // 2. Babana şık bir mesaj dön
            return Ok(new
            {
                Mesaj = "Tüm açık hesaplar mahsuplaşıldı ve defter mühürlendi! Artık ustayı silebilirsiniz."
            });
        }
        catch (Exception ex)
        {
            // Bir arıza çıkarsa panik yok
            return StatusCode(500, new { Mesaj = "Hesap mühürlenirken bir kaza çıktı.", Detay = ex.Message });
        }
    }
    */

    /// <summary>
    /// Belirtilen işçiyi sistemden siler.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            // 1. İşlemi servise yolla
            var sonuc = await _isciService.DeleteAsync(id, cancellationToken);

            // 2. Eğer her şey tamamsa ama silinecek satır bulunamadıysa
            if (!sonuc) return NotFound(new { Mesaj = $"Silinecek işçi bulunamadı (ID: {id})." });

            // 3. Başarılıysa babana güzel haberi ver (NoContent yerine Ok dönüyoruz ki mesaj gitsin)
            return Ok(new { Mesaj = "Usta başarıyla sistemden silindi ve şantiyelerden düşürüldü." });
        }
        // 🛡️ 4. SENIOR ŞOK EMİCİSİ: Servisten fırlatılan o özel "İtiraf" mesajlarını yakala!
        catch (InvalidOperationException ex)
        {
            // 400 BadRequest ile servisin "Neden silemediğini" ekrana (Postman'e/Arayüze) basıyoruz.
            return BadRequest(new { Mesaj = ex.Message });
        }
        catch (Exception ex)
        {
            // Gerçek bir teknik arıza varsa buraya düşer
            return StatusCode(500, new { Mesaj = "Beklenmedik bir hata oluştu.", Detay = ex.Message });
        }
    }

    /// <summary>
    /// 🚑 BÜTÜN İŞÇİLERİ HAYATA DÖNDÜRME BUTONU (TEK SEFERLİK ŞOK)
    /// </summary>

    [HttpPost("{id}/MaasZam")]
    public async Task<IActionResult> MaasZamYap(int id, [FromBody] MaasZamDto dto, CancellationToken cancellationToken)
    {
        var sonuc = await _isciService.MaasZamYapAsync(id, dto, cancellationToken);

        if (!sonuc)
        {
            // Örneğin: Ücret aynıysa "herhangi bir değişiklik yapılmadı" mesajı dönebilirsin
            return BadRequest(new { Mesaj = "Bu işçinin maaşı zaten belirtilen ücrete ayarlı. Herhangi bir değişiklik yapılmadı." });
        }

        return Ok(new { Mesaj = "Maaş zammı başarıyla işlendi." });
    }

}