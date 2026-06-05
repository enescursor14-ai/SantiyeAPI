using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SantiyeAPI.Models;

namespace SantiyeAPI.Configuration;

public class KasaHareketiConfiguration : IEntityTypeConfiguration<KasaHareketi>
{
    public void Configure(EntityTypeBuilder<KasaHareketi> builder)
    {
        // 🚀 KURAL 1: Tutar formatı ve Negatif Giriş Yasağı (Check Constraint)
        builder.Property(e => e.Tutar).HasColumnType("decimal(18,2)");
        
        builder.ToTable("KasaHareketleri", t => t.HasCheckConstraint("CK_KasaHareketi_Tutar_Pozitif", "Tutar > 0"));

        // 🚀 KURAL 2: Global Query Filter (Silinenleri asla getirme!)
        builder.HasQueryFilter(e => !e.IsDeleted);

        // 🚀 KURAL 3: Self-Referencing (Kendi Kendine Bağlantı - İptal Edilen İşlem İçin)
        builder.HasOne(e => e.IptalEdilenIslem)
               .WithMany()
               .HasForeignKey(e => e.IptalEdilenIslemId)
               .OnDelete(DeleteBehavior.Restrict);
        

        // 🚀 KURAL 4: String uzunluk kısıtlamaları (Performans ve güvenlik için)
        builder.Property(e => e.ReferansTabloAdi).HasMaxLength(50);
        builder.Property(e => e.Aciklama).HasMaxLength(500);

        // 🚀 KURAL 5: Performans İndeksi (KasaId + IslemTarihi)
        // Rapor çekerken veya bakiye hesaplarken veritabanı uçak gibi çalışacak!
        builder.HasIndex(e => new { e.KasaId, e.IslemTarihi });


        //Avans iptal için
        builder
        .HasIndex(k => new 
        { 
            k.ReferansTabloAdi, 
            k.ReferansId, 
            k.IsDeleted 
        })
        .HasDatabaseName("IX_KasaHareket_Referans");
    }
}