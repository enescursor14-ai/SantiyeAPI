using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SantiyeAPI.Models;

namespace SantiyeAPI.Configurations;

public class MaasGecmisiConfiguration : IEntityTypeConfiguration<MaasGecmisi>
{
    public void Configure(EntityTypeBuilder<MaasGecmisi> builder)
    {
        builder.ToTable("MaasGecmisleri");
        builder.HasKey(m => m.Id);

        // 1. Muhasebe Kuralı: Para her zaman decimal(18,2) olur
        builder.Property(m => m.Yevmiye)
               .IsRequired()
               .HasColumnType("decimal(18,2)");

        builder.Property(m => m.BaslangicTarihi)
               .IsRequired();
       builder.Property(m => m.Aciklama)
       .HasMaxLength(250);

        // 2. 🚀 PERFORMANS ZIRHI (Sorgu Hızlandırıcı)
        // Geçmişe dönük puantaj girerken sistem sürekli "Bu adamın o tarihteki maaşı neydi?" diye arayacak.
        // Bu yüzden IsciId ve BaslangicTarihi üzerine Composite Index (Bileşik İndeks) atıyoruz. 
        // Arama ışık hızında olacak!
        builder.HasIndex(m => new { m.IsciId, m.BaslangicTarihi });

        // 3. 🔗 İLİŞKİ YAPILANDIRMASI (EF Core Susturucusu ile)
        builder.HasOne(m => m.Isci)
               .WithMany(i => i.MaasGecmisleri)
               .HasForeignKey(m => m.IsciId)
               .IsRequired(false) // 🛡️ EF Core'un [10622] uyarısını kesen zırh!
               .OnDelete(DeleteBehavior.Cascade); // İşçi tamamen silinirse (Hard Delete), maaş tarihçesi de silinsin, çünkü parayı veren yok, sadece log tutuyor.
    }
}