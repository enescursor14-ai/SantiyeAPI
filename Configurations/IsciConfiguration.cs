using System;

namespace SantiyeAPI.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SantiyeAPI.Models;

public class IsciConfiguration : IEntityTypeConfiguration<Isci>
{
    public void Configure(EntityTypeBuilder<Isci> builder)
    {
        // 1. Tablo ve Anahtar Ayarları
        builder.ToTable("Isciler");
        builder.HasKey(i => i.Id);

        // 2. Kolon Özellikleri (Uzunlukları sınırlayarak veritabanını yormuyoruz)
        builder.Property(i => i.Ad).IsRequired().HasMaxLength(50);
        builder.Property(i => i.Soyad).IsRequired().HasMaxLength(50);
        builder.Property(i => i.TcNo).IsRequired().HasMaxLength(11).IsFixedLength();
        

        builder.Property(i => i.Meslek).HasMaxLength(50);
        builder.Property(i => i.Telefon).HasMaxLength(20);

        // Para birimi hassasiyeti (Senior kuralı: decimal her zaman 18,2)
        builder.Property(i => i.GunlukUcret).HasColumnType("decimal(18,2)");

        // 🛡️ SENIOR DOKUNUŞU 1: GLOBAL QUERY FILTER
        // Sistemin hiçbir yerinde "Silinmişleri getirme" yazmana gerek kalmaz.
        builder.HasQueryFilter(i => !i.IsDeleted);

        // 3. 🛡️ GÜVENLİK VE PERFORMANS ZIRHLARI

        // TC KİMLİK BENZERSİZ OLMALI: İşte senin istediğin kritik nokta!
        // Hem aynı TC'den iki kayıt girilemez (hata fırlatır), hem de TC ile arama yapmak ışık hızında olur.
        builder.HasIndex(i => i.TcNo).IsUnique();

        builder.HasIndex(i => new { i.Ad, i.Soyad }); // Ad Soyad kombinasyonuna göre de hızlı arama yapabilmek için
    }

}
