using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SantiyeAPI.Models;

namespace SantiyeAPI.Configuration;

public class HarcamaKategoriConfiguration : IEntityTypeConfiguration<HarcamaKategori>
{
    public void Configure(EntityTypeBuilder<HarcamaKategori> builder)
    {
        builder.Property(e => e.Ad).IsRequired().HasMaxLength(100);
    }
}