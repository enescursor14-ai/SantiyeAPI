using System;

namespace SantiyeAPI.DTOs;

using System.ComponentModel.DataAnnotations;

public class IsciCreateDto
{
    [Required(ErrorMessage = "Ad alanı zorunludur.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Ad en az 2, en fazla 50 karakter olmalıdır.")]
    public string Ad { get; set; } = string.Empty;

    [Required(ErrorMessage = "Soyad alanı zorunludur.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Soyad en az 2, en fazla 50 karakter olmalıdır.")]
    public string Soyad { get; set; } = string.Empty;

    [Required(ErrorMessage = "TC No zorunludur.")]
    [StringLength(11, MinimumLength = 11, ErrorMessage = "TC No 11 haneli olmalıdır.")]
    public string TcNo { get; set; } = string.Empty;
    public string Meslek { get; set; } = string.Empty;

    public string Telefon { get; set; } = string.Empty;

    [Range(0, 100000, ErrorMessage = "Geçerli bir yevmiye ücreti giriniz.")]
    public decimal GunlukUcret { get; set; }
}

public class IsciUpdateDto
{
    public string Ad { get; set; } = string.Empty;
    public string Soyad { get; set; } = string.Empty;
    public string TcNo { get; set; } = string.Empty;
    public string Meslek { get; set; } = string.Empty;
    public string Telefon { get; set; } = string.Empty;
}

public class IsciListDto
{
    public int Id { get; set; }
    public string Ad { get; set; } = string.Empty;
    public string Soyad { get; set; } = string.Empty;
    public string TcNo { get; set; } = string.Empty; // 🚀 YENİ EKLENDİ

    // SENİN TASARIMIN: Sadece Meslek ve Telefon geliyor!
    public string Meslek { get; set; } = string.Empty;
    public string Telefon { get; set; } = string.Empty;
    public decimal GunlukUcret { get; set; }
    public List<string> SantiyeAdlari { get; set; } = new List<string>();

}

public class IsciDetailDto
{
    public int Id { get; set; }
    public string Ad { get; set; } = string.Empty;
    public string Soyad { get; set; } = string.Empty;
    public string TcNo { get; set; } = string.Empty;
    public string Meslek { get; set; } = string.Empty;
    public string Telefon { get; set; } = string.Empty;
    public decimal GunlukUcret { get; set; }


    // İlişkili verilerin DTO'ları (Entity sızdırmıyoruz)
    public List<string> SantiyeAdlari { get; set; } = new List<string>();
    public List<SantiyeBasitDto> Santiyeler { get; set; } = new();
    public List<AvansBasitDto> Avanslar { get; set; } = new();
    public List<GunlukKayitBasitDto> GunlukKayitlar { get; set; } = new();
}
public class MaasZamDto
{
    // Yeni günlük ücret (zorunlu, > 0 olmalı)
    [Range(0.01, 300000, ErrorMessage = "Günlük ücret 0'dan büyük olmalı.")]
    public decimal YeniGunlukUcret { get; set; }

    // Zamın hangi tarihten itibaren geçerli olacağı
    [DataType(DataType.Date)]
    public DateTime ZamBaslangicTarihi { get; set; }

    // Opsiyonel açıklama (örneğin: "Şubat 2026 zammı", "Performans zammı" vb.)
    public string? Aciklama { get; set; }

}

// İlişkili Alt DTO'lar
public record SantiyeBasitDto(int Id, string Ad);
public record AvansBasitDto(int Id, DateTime Tarih, decimal Tutar, string OdemeTuru);
public record GunlukKayitBasitDto(int Id, DateTime Tarih, decimal CalismaKatsayisi, decimal Yevmiye);
