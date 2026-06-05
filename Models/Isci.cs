using System;
using System.Collections.Generic;

namespace SantiyeAPI.Models;

public class Isci
{
    public int Id { get; set; }
    public string Ad { get; set; } = string.Empty;
    public string Soyad { get; set; } = string.Empty;
    public string TcNo { get; set; } = string.Empty;
    public string Meslek { get; set; } = string.Empty;
    public string Telefon { get; set; } = string.Empty;

    public decimal GunlukUcret { get; set; }

    // 🎯 SADECE ŞİRKETTEN TAMAMEN SİLMEK İÇİN (Soft Delete)
    public bool IsDeleted { get; set; } = false;
    

    // 🚨 DİKKAT: AktifMi burdan SİLİNDİ! Artık SantiyeIsci ara tablosunda tutulacak.

    // 🎯 SADECE ARA TABLOYU ÇAĞIRIYORUZ (EF Core'un kafası karışmasın diye tek bağlantı)
    // Eğer bir işçinin şantiyelerini bulmak istersen: isci.SantiyeIsciler.Select(x => x.Santiye) yapacaksın.
    public ICollection<SantiyeIsci> SantiyeIsciler { get; set; } = new List<SantiyeIsci>();

    // DİĞER BAĞLANTILAR (1'e Çok İlişkiler - Bunlar kusursuz, aynen kalıyor)
    public ICollection<GunlukKayit> GunlukKayitlar { get; set; } = new List<GunlukKayit>();
    public ICollection<Avans> Avanslar { get; set; } = new List<Avans>();
    public ICollection<MaasGecmisi> MaasGecmisleri { get; set; } = new List<MaasGecmisi>();
}