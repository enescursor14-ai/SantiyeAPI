using System;

namespace SantiyeAPI.DTOs;

public class ManuelGiderRequest
{
    public int KasaId { get; set; }
    public int? KategoriId { get; set; }
    public decimal Tutar { get; set; }
    public string? Aciklama { get; set; }
    // 🚀 EĞER BU SATIR YOKSA SEÇTİĞİN ŞANTİYE ÇÖPE GİDER!
    public int? SantiyeId { get; set; }


    public DateTime? IslemTarihi { get; set; }
}