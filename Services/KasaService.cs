using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SantiyeAPI.Models;
using SantiyeAPI.DTOs;
using SantiyeAPI.Data;
using SantiyeAPI.Exceptions;
using SantiyeAPI.Helpers;
using Microsoft.Extensions.Caching.Memory;

namespace SantiyeAPI.Services;

public class KasaService : IKasaService
{
    private readonly AppDbContext _context;
    private readonly ILogger<KasaService> _logger;
    private readonly IMemoryCache _cache;
    private const string CACHE_KEY = "KasaBakiyeleriCache";

    public KasaService(AppDbContext context, ILogger<KasaService> logger, IMemoryCache cache)
    {
        _context = context;
        _logger = logger;
        _cache = cache;
    }

    // 🚀 Cache Güncelleme Helper
    // 🚀 Cache Güncelleme Helper (Senkron - Hızlı versiyon)
    private void InvalidateCache()
    {
        _cache.Remove(CACHE_KEY);
        _logger.LogInformation("Kasa bakiye cache temizlendi.");
    }

    public async Task<decimal> GetKasaBakiyeAsync(int kasaId)
    {
        return await _context.KasaHareketleri
            .Where(k => k.KasaId == kasaId && !k.IsDeleted)
            .SumAsync(k => k.Yon == KasaIslemYonu.Giris ? k.Tutar : -k.Tutar);
    }

    private async Task KasaBakiyeKontrolEtAsync(int kasaId, decimal istenenTutar)
    {
        var mevcutBakiye = await GetKasaBakiyeAsync(kasaId);

    }

    public async Task<int> AvansVerAsync(AvansVerRequest request)
    {
        if (request.Tutar <= 0)
            throw new BusinessException("Patron, avans tutarı 0 veya eksi bir rakam olamaz!");

        var isci = await _context.Isciler.FindAsync(request.IsciId);
        if (isci == null) throw new NotFoundException("İşçi", request.IsciId);

        decimal aylikMaksimumAvansLimiti = isci.GunlukUcret * 30;
        if (request.Tutar > aylikMaksimumAvansLimiti)
            throw new BusinessException($"Hop Patron! {isci.Ad} Ustanın bir aylık hak edişi {aylikMaksimumAvansLimiti:N2} ₺. Bundan daha fazla tek seferlik avans verilemez.");

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var simdiTurkiye = ZamanMotoru.SimdiTurkiye();
            DateTime hedefTarih = request.Tarih.HasValue
                ? request.Tarih.Value.Date.Add(simdiTurkiye.TimeOfDay)
                : simdiTurkiye;

            DateTime hedefYarin = hedefTarih.Date.AddDays(1);

            var secilenGunVerilenAvansSayisi = await _context.Avanslar
                .CountAsync(a => a.IsciId == request.IsciId
                              && a.Tarih >= hedefTarih.Date
                              && a.Tarih < hedefYarin
                              && !a.IsDeleted);

            if (secilenGunVerilenAvansSayisi >= 2)
                throw new BusinessException($"Bu ustaya {hedefTarih:dd.MM.yyyy} tarihinde zaten {secilenGunVerilenAvansSayisi} kere avans girilmiş!");

            var patron = await _context.Patronlar
                .Where(p => p.Id == request.PatronId)
                .Select(p => new { p.Id, p.KasaId })
                .FirstOrDefaultAsync();

            if (patron == null) throw new BusinessException("Seçilen patron bulunamadı!");
            if (!patron.KasaId.HasValue) throw new BusinessException("Bu patronun adına tanımlı bir kasa yok!");

            int islemYapilacakKasaId = patron.KasaId.Value;
            await KasaBakiyeKontrolEtAsync(islemYapilacakKasaId, request.Tutar);

            var yeniAvans = new Avans
            {
                IsciId = request.IsciId,
                KasaId = islemYapilacakKasaId,
                SantiyeId = request.SantiyeId,
                Tutar = request.Tutar,
                Tarih = hedefTarih,
                OdemeTuru = request.OdemeTuru,
                Aciklama = StringHelper.ToTitleCaseTr(request.Aciklama),
                OdendiMi = false,
                IsDeleted = false
            };

            await _context.Avanslar.AddAsync(yeniAvans);
            await _context.SaveChangesAsync();

            var kasaHareketi = new KasaHareketi
            {
                KasaId = islemYapilacakKasaId,
                PatronId = patron.Id,
                SantiyeId = request.SantiyeId,
                Tutar = request.Tutar,
                Yon = KasaIslemYonu.Cikis,
                HareketTipi = KasaHareketTipi.Avans,
                ReferansTabloAdi = "Avanslar",
                ReferansId = yeniAvans.Id,

                // ✅ YENİ DOKUNUŞ: Sistemin kayıt anını değil, babanın takvimden seçtiği hedef tarihi kullan!
                IslemTarihi = hedefTarih,

                Aciklama = $"{isci.Ad} {isci.Soyad} isimli işçiye avans verildi."
            };

            await _context.KasaHareketleri.AddAsync(kasaHareketi);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            InvalidateCache();
            return yeniAvans.Id;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    public async Task<bool> AvansIptalEtAsync(int avansId, string iptalEdenKullaniciId)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var avans = await _context.Avanslar
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.Id == avansId);

            if (avans == null || avans.IsDeleted)
                throw new NotFoundException("Avans", avansId);

            if (avans.OdendiMi)
                throw new BusinessException("Bu avans zaten hesaba mahsup edilmiş, iptal edilemez!");

            avans.IsDeleted = true;
            avans.DeletedAt = ZamanMotoru.SimdiTurkiye();
            avans.DeletedBy = iptalEdenKullaniciId;

            var orijinalHareket = await _context.KasaHareketleri
                .FirstOrDefaultAsync(k =>
                    k.HareketTipi == KasaHareketTipi.Avans &&
                    k.ReferansId == avansId &&
                    !k.IsDeleted);

            if (avans.KasaId > 0)
            {
                int? patronId = await _context.Patronlar
                    .Where(p => p.KasaId == avans.KasaId)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync();

                var telafiGiris = new KasaHareketi
                {
                    KasaId = avans.KasaId,
                    PatronId = patronId,
                    SantiyeId = avans.SantiyeId,
                    Tutar = avans.Tutar,
                    Yon = KasaIslemYonu.Giris,
                    HareketTipi = KasaHareketTipi.IadeTersKayit,
                    ReferansTabloAdi = "Avanslar",
                    ReferansId = avans.Id,
                    IslemTarihi = ZamanMotoru.SimdiTurkiye(),
                    Aciklama = $"İptal/İade: {avans.Tarih:dd.MM.yyyy} tarihli avans tutarı kasaya iade edildi.",
                    IsDeleted = false,
                    IptalEdilenIslemId = orijinalHareket?.Id
                };

                await _context.KasaHareketleri.AddAsync(telafiGiris);
            }

            if (orijinalHareket != null)
            {
                orijinalHareket.Aciklama = "[İPTAL EDİLDİ - İADESİ ALINDI] " + orijinalHareket.Aciklama;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // 🚀 CACHE INVALIDATE
            InvalidateCache();

            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> SermayeEkleAsync(SermayeEkleRequest request)
    {
        if (request.Tutar <= 0)
            throw new BusinessException("Patron, kasaya 0 veya eksi sermaye giremezsin!");

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var patronBilgisi = await _context.Patronlar
                .AsNoTracking()
                .Where(p => p.Id == request.PatronId)
                .Select(p => new { p.KasaId, p.Ad })
                .FirstOrDefaultAsync();

            if (patronBilgisi == null)
                throw new BusinessException("Sisteme kayıtlı böyle bir patron bulunamadı!");

            if (patronBilgisi.KasaId == null)
                throw new BusinessException($"{patronBilgisi.Ad} isimli ortağın tanımlı bir kasası yok!");

            if (request.SantiyeId.HasValue && request.SantiyeId.Value > 0)
            {
                var santiyeVarMi = await _context.Santiyeler.AnyAsync(s => s.Id == request.SantiyeId.Value);
                if (!santiyeVarMi) throw new BusinessException("Seçilen şantiye sistemde bulunamadı!");
            }
            else
            {
                request.SantiyeId = null; // Sıfır bile gelse veritabanına tertemiz 'Null' (Genel Şirket hesabı) olarak yazdırıyoruz.
            }

            var simdiTurkiye = ZamanMotoru.SimdiTurkiye();
            DateTime islemTarihi = request.IslemTarihi.HasValue
                ? request.IslemTarihi.Value.Date.Add(simdiTurkiye.TimeOfDay)
                : simdiTurkiye;

            var hareket = new KasaHareketi
            {
                KasaId = patronBilgisi.KasaId.Value,
                PatronId = request.PatronId,
                SantiyeId = request.SantiyeId,
                Tutar = request.Tutar,
                Yon = KasaIslemYonu.Giris,
                HareketTipi = KasaHareketTipi.ManuelGelir,
                IslemTarihi = islemTarihi,
                Aciklama = string.IsNullOrWhiteSpace(request.Aciklama)
                    ? $"{patronBilgisi.Ad} tarafından kasaya para eklendi."
                    : StringHelper.ToTitleCaseTr(request.Aciklama.Trim()),
            };

            await _context.KasaHareketleri.AddAsync(hareket);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // 🚀 CACHE INVALIDATE
            InvalidateCache();

            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> ManuelGiderEkleAsync(ManuelGiderRequest request)
    {
        if (request.Tutar <= 0)
            throw new BusinessException("Patron, masraf çıkışı 0 veya eksi bir tutar olamaz!");

        await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var patronBilgisi = await _context.Patronlar
                .Where(p => p.KasaId == request.KasaId)
                .Select(p => new { p.Id, p.KasaId })
                .FirstOrDefaultAsync();

            if (patronBilgisi == null || !patronBilgisi.KasaId.HasValue)
                throw new BusinessException("Bu kasa hiçbir patrona ait değil!");

            var simdiTurkiye = ZamanMotoru.SimdiTurkiye();
            DateTime islemTarihi = request.IslemTarihi.HasValue
                ? request.IslemTarihi.Value.Date.Add(simdiTurkiye.TimeOfDay)
                : simdiTurkiye;

            var hareket = new KasaHareketi
            {
                KasaId = patronBilgisi.KasaId.Value,
                PatronId = patronBilgisi.Id,
                SantiyeId = request.SantiyeId,
                Tutar = request.Tutar,
                Yon = KasaIslemYonu.Cikis,
                HareketTipi = KasaHareketTipi.ManuelGider,
                IslemTarihi = islemTarihi,
                Aciklama = string.IsNullOrWhiteSpace(request.Aciklama)
                    ? "Genel Masraf"
                    : StringHelper.ToTitleCaseTr(request.Aciklama)
            };

            await _context.KasaHareketleri.AddAsync(hareket);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // 🚀 CACHE INVALIDATE
            InvalidateCache();

            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> KasaTransferYapAsync(KasaTransferRequest request)
    {
        if (request.GonderenKasaId == request.AliciKasaId)
            throw new BusinessException("Aynı kasaya transfer yapamazsın!");

        await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var gonderenKasaVarMi = await _context.Kasalar.AnyAsync(k => k.Id == request.GonderenKasaId);
            var aliciKasaVarMi = await _context.Kasalar.AnyAsync(k => k.Id == request.AliciKasaId);
            if (!gonderenKasaVarMi || !aliciKasaVarMi)
                throw new BusinessException("Gönderen veya alıcı kasa bulunamadı!");

            await KasaBakiyeKontrolEtAsync(request.GonderenKasaId, request.Tutar);

            var simdiOffset = ZamanMotoru.SimdiTurkiye();
            var transferMuhuru = Guid.NewGuid();

            var cikis = new KasaHareketi
            {
                KasaId = request.GonderenKasaId,
                Tutar = request.Tutar,
                Yon = KasaIslemYonu.Cikis,
                HareketTipi = KasaHareketTipi.Transfer,
                TransferGrupId = transferMuhuru,
                IslemTarihi = simdiOffset,
                Aciklama = $"Transfer Gönderimi. Not: {StringHelper.ToTitleCaseTr(request.Aciklama)}"
            };

            var giris = new KasaHareketi
            {
                KasaId = request.AliciKasaId,
                Tutar = request.Tutar,
                Yon = KasaIslemYonu.Giris,
                HareketTipi = KasaHareketTipi.Transfer,
                TransferGrupId = transferMuhuru,
                IslemTarihi = simdiOffset,
                Aciklama = $"Transfer Alımı. Not: {StringHelper.ToTitleCaseTr(request.Aciklama)}"
            };

            await _context.KasaHareketleri.AddRangeAsync(cikis, giris);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // 🚀 CACHE INVALIDATE
            InvalidateCache();

            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}