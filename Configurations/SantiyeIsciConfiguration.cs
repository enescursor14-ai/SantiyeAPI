using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SantiyeAPI.Models;

namespace SantiyeAPI.Configurations;

public class SantiyeIsciConfiguration : IEntityTypeConfiguration<SantiyeIsci>
{
    public void Configure(EntityTypeBuilder<SantiyeIsci> builder)
    {
        // 🛡️ SENIOR DOKUNUŞU 2: COMPOSITE KEY (Bileşik Anahtar)
        // Bir işçi aynı şantiyeye aynı anda iki kere kaydedilemez.
        builder.HasKey(si => new { si.IsciId, si.SantiyeId });
        builder.HasIndex(si => si.SantiyeId);
        builder.HasIndex(si => new { si.IsciId, si.AktifMi });
        // ✅ YENI: Global Query Filter - Soft delete
        builder.HasQueryFilter(si => !si.IsDeleted);

        builder.HasOne(si => si.Isci)
               .WithMany(i => i.SantiyeIsciler)
               .HasForeignKey(si => si.IsciId)
               .IsRequired(false);


        builder.HasOne(si => si.Santiye)
               .WithMany(s => s.SantiyeIsciler)
               .HasForeignKey(si => si.SantiyeId);
    }
}