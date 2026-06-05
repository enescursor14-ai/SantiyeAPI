using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SantiyeAPI.Models;

namespace SantiyeAPI.Configuration;

public class KasaConfiguration : IEntityTypeConfiguration<Kasa>
{
    public void Configure(EntityTypeBuilder<Kasa> builder)
    {
        builder.Property(e => e.Ad).IsRequired().HasMaxLength(100);
    }
}