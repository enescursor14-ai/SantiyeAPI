using System;

namespace SantiyeAPI.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SantiyeAPI.Models;

public class GunlukKayitConfiguration : IEntityTypeConfiguration<GunlukKayit>
{
       public void Configure(EntityTypeBuilder<GunlukKayit> builder)
       {
              builder.ToTable("GunlukKayitlar");
              builder.HasKey(g => g.Id);

              // 1. Kolon Özellikleri
              builder.Property(g => g.CalismaKatsayisi)
                     .HasColumnType("decimal(3,2)"); // 1.00, 0.50 gibi değerler için

              builder.Property(g => g.Yevmiye)
                     .HasColumnType("decimal(18,2)");

              builder.Property(g => g.Aciklama)
                     .HasMaxLength(250);

              // 2. 🚀 PERFORMANS ZIRHLARI (Composite Index)
              // Baban sorgu yaparken: "Şu tarihte, şu şantiyedeki kayıtları getir" diyecek.
              // Tarih ve SantiyeId üzerine beraber indeks koyarsak raporlar ışık hızında gelir.
              builder.HasIndex(g => new { g.SantiyeId, g.Tarih, g.OdendiMi });

              // İşçi bazlı geçmişe bakarken de hızlı olsun
              builder.HasIndex(g => g.IsciId);

              // 🚀 ÇİFT KAYIT ZIRHI (Concurrency Protection)
              // Aynı işçiye, aynı şantiyede, aynı günde 2. bir kayıt açılmasını VERİTABANI seviyesinde yasaklıyoruz.
              builder.HasIndex(g => new { g.IsciId, g.SantiyeId, g.Tarih })
                     .IsUnique()
                     .HasFilter("[IsDeleted] = 0") // Sadece silinmemiş kayıtlar için benzersizlik ara
                     .HasDatabaseName("UX_GunlukKayit_TekKayitZirhi");
              
              builder.HasQueryFilter(k => !k.IsDeleted);

              // 3. 🔗 İLİŞKİ YAPILANDIRMASI

              // Bir işçinin birden fazla günlük kaydı olabilir
              builder.HasOne(g => g.Isci)
                     .WithMany(i => i.GunlukKayitlar)
                     .HasForeignKey(g => g.IsciId)
                     .IsRequired(false)
                     .OnDelete(DeleteBehavior.Restrict); // İşçi silinirse (ki biz pasife alıyoruz ama) kayıtları silinsin mi? Genelde Cascade veya Restrict seçilir.

              // Bir şantiyenin birden fazla günlük kaydı olabilir
              builder.HasOne(g => g.Santiye)
                     .WithMany() // Santiye tarafında List<GunlukKayit> yoksa boş bırakıyoruz
                     .HasForeignKey(g => g.SantiyeId)
                     .OnDelete(DeleteBehavior.Restrict); // Şantiye silinirse kayıtlar kalsın (hata versin), veriler kaybolmasın.
       }
}
