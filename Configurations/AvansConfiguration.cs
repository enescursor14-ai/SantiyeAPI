using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SantiyeAPI.Models;

namespace SantiyeAPI.Configurations;

public class AvansConfiguration : IEntityTypeConfiguration<Avans>
{
       public void Configure(EntityTypeBuilder<Avans> builder)
       {
              builder.ToTable("Avanslar");
              builder.HasKey(a => a.Id);

              // 1. Kolon Özellikleri
              builder.Property(a => a.Tutar)
                     .IsRequired()
                     .HasColumnType("decimal(18,2)");

              builder.Property(a => a.OdemeTuru)
                     .IsRequired()
                     .HasMaxLength(30); // Nakit, Banka, Elden vb.

              builder.Property(a => a.Aciklama)
                     .HasMaxLength(500);

              // 2. 🚀 PERFORMANS VE RAPORLAMA İNDEKSLERİ
              builder.HasIndex(a => a.IsciId);
              builder.HasIndex(a => new { a.Tarih, a.OdendiMi });

              // 3. 🔗 İLİŞKİ YAPILANDIRMASI
              builder.HasOne(a => a.Isci)
                     .WithMany(i => i.Avanslar)
                     .HasForeignKey(a => a.IsciId)
                     .IsRequired(false)
                     .OnDelete(DeleteBehavior.Restrict);

              builder.HasOne(a => a.Santiye)
                     .WithMany()
                     .HasForeignKey(a => a.SantiyeId)
                     .OnDelete(DeleteBehavior.Restrict);
              // 🏗️ YENİ EKLENDİ: KASA İLİŞKİSİ
              builder.HasOne(a => a.Kasa)
                     .WithMany()
                     .HasForeignKey(a => a.KasaId)
                     .IsRequired() // 🚀 SENİN UYARINLA EKLENEN ÇELİK KİLİT! (Artık boş kasa olamaz)
                     .OnDelete(DeleteBehavior.Restrict);

            
             

              // 4. 🛡️ GÜVENLİK VE FİLTRELER (SENIOR DOKUNUŞU)

              // Veritabanı seviyesinde default olarak IsDeleted = false yapıyoruz.
              builder.Property(a => a.IsDeleted).HasDefaultValue(false);

              // 🚀 GLOBAL QUERY FILTER: 
              // Baban arayüzde "Tüm Avansları Getir" dediğinde, sistem arka planda 
              // silinmiş (IsDeleted = true) olanları OTOMATİK OLARAK gizler.
              // Kodun hiçbir yerine "Where(x => x.IsDeleted == false)" yazmana gerek kalmaz!
              builder.HasQueryFilter(a => !a.IsDeleted);
       }
}