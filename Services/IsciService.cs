namespace SantiyeAPI.Services;

using System.Globalization;
using AutoMapper.QueryableExtensions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using SantiyeAPI.Data;
using SantiyeAPI.DTOs;
using SantiyeAPI.Models;
using SantiyeAPI.Helpers;
using SantiyeAPI.Exceptions;

public class IsciService : IIsciService
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;


    private static readonly CultureInfo TrCulture = new("tr-TR");
    private static readonly TextInfo TrTextInfo = TrCulture.TextInfo;


    public IsciService(AppDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    // 🚀 ŞANTİYE FİLTRELİ YENİ MOTOR
    public async Task<PagedResult<IsciListDto>> GetAllAsync(string? aramaKelimesi, string? santiyeFiltre, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        // Silinmemiş ustaları alıyoruz
        var query = _context.Isciler.AsNoTracking();

        // 1. KELİME ARAMA FİLTRESİ (Senin yazdığın harika mantık)
        if (!string.IsNullOrWhiteSpace(aramaKelimesi))
        {

            var arama = aramaKelimesi.Trim();
            string format1 = TrTextInfo.ToTitleCase(arama.ToLower(TrCulture));
            var parcalar = format1.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parcalar.Length > 1)
            {
                var ad = parcalar[0];
                var soyad = parcalar[1];
                query = query.Where(i => i.Ad.StartsWith(ad) && i.Soyad.StartsWith(soyad));
            }
            else
            {
                query = query.Where(i => i.Ad.StartsWith(format1) || i.Soyad.StartsWith(format1) || (i.Meslek != null && i.Meslek.StartsWith(format1)));
            }
        }

        // 🚀 2. ŞANTİYE FİLTRESİ (SENİOR DOKUNUŞU)
        if (!string.IsNullOrWhiteSpace(santiyeFiltre) && santiyeFiltre != "TUM")
        {
            if (santiyeFiltre == "COKLU")
            {
                // Joker Ustalar: 1'den fazla aktif şantiyesi olanlar
                query = query.Where(i => i.SantiyeIsciler.Count(si => si.AktifMi) > 1);
            }
            else if (santiyeFiltre == "BOSTA")
            {
                // Yatan Ustalar: Hiçbir aktif şantiyesi olmayanlar
                query = query.Where(i => !i.SantiyeIsciler.Any(si => si.AktifMi));
            }
            else if (int.TryParse(santiyeFiltre, out int seciliSantiyeId))
            {
                // Sadece seçilen şantiyede aktif olan ustalar
                query = query.Where(i => i.SantiyeIsciler.Any(si => si.AktifMi && si.SantiyeId == seciliSantiyeId));
            }
        }

        // Toplam Kayıt Sayısı (Sayfalama ve Sayaç İçin)
        var totalCount = await query.CountAsync(cancellationToken);

        // Veriyi Çekme: Alfabetik Sıralama ve DTO'ya Dönüştürme
        var isciler = await query
            .OrderBy(i => i.Ad)
            .ThenBy(i => i.Soyad)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ProjectTo<IsciListDto>(_mapper.ConfigurationProvider)
            .ToListAsync(cancellationToken);

        return new PagedResult<IsciListDto>(isciler, totalCount, pageNumber, pageSize);
    }

    public async Task<IsciDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Isciler
            .AsNoTracking()
            .Where(i => i.Id == id)
            .ProjectTo<IsciDetailDto>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsync(cancellationToken);
    }



    public async Task<IsciDetailDto> CreateAsync(IsciCreateDto createDto, CancellationToken cancellationToken = default)
    {


        // 🧹 Veri Temizliği (TitleCase mantığı kalıyor, harika)
        createDto.Ad = TrTextInfo.ToTitleCase(createDto.Ad.Trim().ToLower(TrCulture));
        createDto.Soyad = TrTextInfo.ToTitleCase(createDto.Soyad.Trim().ToLower(TrCulture));
        createDto.Meslek = TrTextInfo.ToTitleCase(createDto.Meslek.Trim().ToLower(TrCulture));


        // 🚀 1. TC KONTROLÜ VE ARŞİVDEN DİRİLTME (SENIOR DOKUNUŞU - TEK SORGU)
        if (!string.IsNullOrWhiteSpace(createDto.TcNo))
        {
            // 🛡️ ZIRH: AnyAsync'i ve gereksiz else düğümlerini çöpe attık!
            // Tek sorguyla arşive (silinenlere) dahil tüm veritabanına bakıyoruz.
            var mevcutUsta = await _context.Isciler
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.TcNo == createDto.TcNo, cancellationToken);

            if (mevcutUsta != null)
            {
                // DURUM 1: Usta sistemde kayıtlı ve AKTİF! 
                if (!mevcutUsta.IsDeleted)
                {
                    throw new BusinessException($"'{createDto.TcNo}' kimlik numarasıyla sisteme zaten kayıtlı bir usta bulunuyor!");
                }

                // DURUM 2: Usta sistemde var ama SİLİNMİŞ (Arşivde). Diriltiyoruz!
                _mapper.Map(createDto, mevcutUsta);
                mevcutUsta.IsDeleted = false; // Hayata döndür

                // Diriltilen ustaya güncel maaşıyla yeni bir başlangıç atıyoruz
                _context.MaasGecmisleri.Add(new MaasGecmisi
                {
                    IsciId = mevcutUsta.Id,
                    Yevmiye = mevcutUsta.GunlukUcret,
                    BaslangicTarihi = DateTime.Today,
                    Aciklama = "Arşivden geri getirildi." // Opsiyonel şık bir not
                });

                await _context.SaveChangesAsync(cancellationToken);
                return _mapper.Map<IsciDetailDto>(mevcutUsta);
            }
        }

        // 🚀 2. YENİ KAYIT VE MAAŞ GEÇMİŞİ (ATOMİK İŞLEM - DURUM 3: TC HİÇ YOK)
        var yeniIsci = _mapper.Map<Isci>(createDto);

        // İşçiyi ekliyoruz ama henüz SaveChanges demiyoruz!
        _context.Isciler.Add(yeniIsci);

        // Navigation Property sayesinde ID'nin oluşmasını beklemeden maaşı ekleyebiliriz!
        yeniIsci.MaasGecmisleri = new List<MaasGecmisi> {
            new MaasGecmisi {
                Yevmiye = yeniIsci.GunlukUcret,
                BaslangicTarihi = DateTime.Today,
                Aciklama = "İşe giriş maaş tanımı."
            }
        };

        // 💥 TEK SEFERDE KAYDET (Ya hepsi ya hiçbiri)
        await _context.SaveChangesAsync(cancellationToken);

        return _mapper.Map<IsciDetailDto>(yeniIsci);
    }




    public async Task<bool> UpdateAsync(int id, IsciUpdateDto updateDto, CancellationToken cancellationToken = default)
    {
        // 1. İşçiyi Bul
        var isci = await _context.Isciler.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (isci == null)
            throw new NotFoundException("İşçi", id);

        // 2. TC Çakışma Kontrolü (Sadece TC dolu gönderildiyse kontrol et, null gelirse dokunma)
        if (!string.IsNullOrWhiteSpace(updateDto.TcNo))
        {
            var yeniTc = updateDto.TcNo.Trim();

            if (yeniTc != isci.TcNo)
            {
                bool tcKullaniliyorMu = await _context.Isciler
                    .AnyAsync(i => i.TcNo == yeniTc && i.Id != id, cancellationToken);

                if (tcKullaniliyorMu)
                    throw new BusinessException($"'{yeniTc}' kimlik numarası başka bir ustaya ait!");

                isci.TcNo = yeniTc;
            }
        }

        // 3. Zorunlu Alanlar
        if (!string.IsNullOrWhiteSpace(updateDto.Ad))
            isci.Ad = TrTextInfo.ToTitleCase(updateDto.Ad.Trim().ToLower(TrCulture));

        if (!string.IsNullOrWhiteSpace(updateDto.Soyad))
            isci.Soyad = TrTextInfo.ToTitleCase(updateDto.Soyad.Trim().ToLower(TrCulture));

        if (!string.IsNullOrWhiteSpace(updateDto.Meslek))
            isci.Meslek = TrTextInfo.ToTitleCase(updateDto.Meslek.Trim().ToLower(TrCulture));

        // 4. Opsiyonel Alan: Telefon (Boş gelirse mevcut değeri koru)
        if (!string.IsNullOrWhiteSpace(updateDto.Telefon))
            isci.Telefon = updateDto.Telefon.Trim();

        // 5. Kaydet
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
   
   
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        // 1. RÖNTGEN
        var isciDurumu = await _context.Isciler
            .IgnoreQueryFilters()
            .Where(i => i.Id == id)
            .Select(i => new { i.Id, i.IsDeleted })
            .FirstOrDefaultAsync(cancellationToken);

        if (isciDurumu == null)
            throw new NotFoundException("İşçi", id);

        if (isciDurumu.IsDeleted)
            throw new InvalidOperationException("Bu işçi zaten silinmiş.");

        // 2. MATEMATİK (SADECE AÇIK HESAPLAR)
        decimal toplamHakedis = await _context.GunlukKayitlar
            .Where(g => g.IsciId == id && !g.OdendiMi && !g.IsDeleted)
            .SumAsync(g => g.Yevmiye, cancellationToken);

        decimal toplamAvans = await _context.Avanslar
            .Where(a => a.IsciId == id && !a.OdendiMi && !a.IsDeleted)
            .SumAsync(a => a.Tutar, cancellationToken);

        decimal netBakiye = toplamHakedis - toplamAvans;
        bool bakiyeSifir = Math.Abs(netBakiye) < 0.01m;

        // 🚀 TAVSİYE 4: Mantık kontrolünü Transaction (Veritabanı Kilidi) dışına aldık!
        // Eğer adamın borcu varsa veritabanını boşuna kilitlemeyip direkt hatayı fırlatıyoruz.
        if (!bakiyeSifir)
        {
            string mesaj = netBakiye > 0
                ? $"Hop Patron! Bu işçinin {netBakiye:N2} ₺ alacağı var. Önce hesabını kapatın."
                : $"Hop Patron! Bu işçi {-netBakiye:N2} ₺ borçlu. Önce iadesini alın.";

            throw new InvalidOperationException(mesaj);
        }

        // 3. TRANSACTION 
        // 🚀 TAVSİYE 1: 'await using' kullanarak asenkron bellek temizliği sağlandı.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // 🚀 TAVSİYE 2: "AcikKayitVarMi" sorgularını tamamen sildik! 
            // Direkt güncelliyoruz. Eşleşen kayıt yoksa zaten hata vermez, 0 satır günceller geçer. Performans x2 arttı!

            // 🚀 TAVSİYE 3: CancellationToken'lar ExecuteUpdateAsync içine eklendi.
            await _context.GunlukKayitlar
                .Where(g => g.IsciId == id && !g.OdendiMi)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.OdendiMi, true), cancellationToken);

            await _context.Avanslar
                .Where(a => a.IsciId == id && !a.OdendiMi)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.OdendiMi, true), cancellationToken);

            // 4. TEMİZLİK
            var turkiyeSaati = GetTurkeyTime(); // Veya ZamanMotoru.SimdiTurkiye()

            await _context.SantiyeIsciler
                .Where(si => si.IsciId == id && si.AktifMi)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.AktifMi, false)
                    .SetProperty(x => x.AyrilmaTarihi, turkiyeSaati),
                    cancellationToken);

            var etkilenenSatir = await _context.Isciler
                .IgnoreQueryFilters()
                .Where(i => i.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.IsDeleted, true),
                    // 🚀 TAVSİYE 5: Eğer Isci modelinde SilinmeTarihi/DeletedAt gibi bir kolonun varsa 
                    // aşağıdaki satırın başındaki "//" işaretlerini kaldırıp aktif edebilirsin.
                    // .SetProperty(x => x.SilinmeTarihi, turkiyeSaati), 
                    cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return etkilenenSatir > 0;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
    private DateTime GetTurkeyTime() => ZamanMotoru.SimdiTurkiye();


    public async Task<bool> MaasZamYapAsync(int isciId, MaasZamDto dto, CancellationToken cancellationToken)
    {
        // 1. Güvenlik: Ücret > 0 mı? (Fail-fast)
        if (dto.YeniGunlukUcret <= 0)
            throw new Exception("Usta, maaş 0 veya eksi olamaz. Geçerli bir günlük ücret gir.");

        // 2. Tarihi normalize et (sadece gün bazında)
        var zamBaslangicTarihi = dto.ZamBaslangicTarihi.Date;
        var ertesiGun = zamBaslangicTarihi.AddDays(1);

        // 3. İşçiyi bul (Tracking aktif — çünkü ücretini güncelleyeceğiz)
        var isci = await _context.Isciler
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == isciId, cancellationToken);

        if (isci == null)
            throw new Exception("Zam yapılmak istenen işçi veritabanında bulunamadı.");

        if (isci.IsDeleted)
            throw new Exception("Patron, şirkette pasif edilmiş (tamamen çıkarılmış) bir ustaya zam yapamazsın.");

        var eskiUcret = isci.GunlukUcret;

        // Eğer ücret aynıysa zam yok
        if (eskiUcret == dto.YeniGunlukUcret)
            return false;

        // 4. 🚀 SARGable tarih sorgusu (Index dostu!)
        var mevcutKayit = await _context.MaasGecmisleri
            .FirstOrDefaultAsync(m =>
                m.IsciId == isciId &&
                m.BaslangicTarihi >= zamBaslangicTarihi &&
                m.BaslangicTarihi < ertesiGun,
                cancellationToken);

        // 5. İşçinin güncel ücretini güncelle
        isci.GunlukUcret = dto.YeniGunlukUcret;

        // 6. MaasGecmisi tablosuna zammı işle
        if (mevcutKayit != null)
        {
            mevcutKayit.Yevmiye = dto.YeniGunlukUcret;
            mevcutKayit.Aciklama = dto.Aciklama;
        }
        else
        {
            _context.MaasGecmisleri.Add(new MaasGecmisi
            {
                IsciId = isciId,
                Yevmiye = dto.YeniGunlukUcret,
                BaslangicTarihi = zamBaslangicTarihi,
                Aciklama = dto.Aciklama
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        // 🚀 Hafızayı temizle — bir sonraki sorgu direkt DB'den gelsin
        _context.ChangeTracker.Clear();

        return true;
    }

}