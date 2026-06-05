using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SantiyeAPI.Models;

namespace SantiyeAPI.Configurations;

public class MaasOdemesiConfiguration : IEntityTypeConfiguration<MaasOdemesi>
{
    public void Configure(EntityTypeBuilder<MaasOdemesi> builder)
    {
        builder.ToTable("MaasOdemeleri");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Tutar).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(m => m.Aciklama).HasMaxLength(500);

        builder.HasIndex(m => m.IsciId);
        builder.HasIndex(m => m.KasaId);

        builder.HasOne(m => m.Isci).WithMany().HasForeignKey(m => m.IsciId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Kasa).WithMany().HasForeignKey(m => m.KasaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Santiye).WithMany().HasForeignKey(m => m.SantiyeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(m => !m.IsDeleted); // Silinmişleri otomatik gizle
    }
}