using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SantiyeAPI.Models;

namespace SantiyeAPI.Configurations;

public class SantiyeConfiguration : IEntityTypeConfiguration<Santiye>
{
    public void Configure(EntityTypeBuilder<Santiye> builder)
    {
        // 1. Tablo ismi
        builder.ToTable("Santiyeler");
        builder.HasKey(s => s.Id);
        builder.HasQueryFilter(s => !s.IsDeleted);

        // 2. Kolon Sınırları
        builder.Property(s => s.Ad)
            .IsRequired()
            .HasMaxLength(100); // Şantiye adları biraz uzun olabilir (Örn: "Kartal Konut Projesi Faz-2")

        builder.Property(s => s.Konum)
            .HasMaxLength(200);

        // 3. 🚀 Performans ve İlişki Ayarları
        
        // Şantiye adına göre filtreleme çok yapılacağı için indeks koyuyoruz
        builder.HasIndex(s => s.Ad);


        // ÇOKTAN ÇOĞA İLİŞKİ YAPILANDIRMASI (Many-to-Many)
        // EF Core bunu otomatik anlar ama biz Senior olarak kontrolü elimize alalım:
        
    }

    
}
