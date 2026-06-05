using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SantiyeAPI.Models;

namespace SantiyeAPI.Configurations;

public class SantiyeNotuConfiguration : IEntityTypeConfiguration<SantiyeNotu>
{
    public void Configure(EntityTypeBuilder<SantiyeNotu> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.NotMetni)
            .IsRequired()
            .HasMaxLength(1000);

        // ✅ Relationship tanımı
        builder.HasOne(s => s.Santiye)
            .WithMany(s => s.Notlari)
            .HasForeignKey(s => s.SantiyeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index
        builder.HasIndex(s => s.SantiyeId);
        builder.HasIndex(s => s.Tarih);
        builder.HasQueryFilter(sn => !sn.IsDeleted);

    }
}
