using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace SantiyeAPI.Models;

public class MaasGecmisi
{
    public int Id { get; set; }
    
    // Hangi ustanın maaş geçmişi?
    public int? IsciId { get; set; } // Zırh için nullable yapıyoruz (int?)
    public Isci? Isci { get; set; }
    
    // O dönemki yevmiyesi ne kadardı?
    public decimal Yevmiye { get; set; }
    
    // Bu maaş hangi tarihten itibaren geçerli oldu? (Örn: 24 Şubat'ta zam yapıldı)
    [Column(TypeName = "date")]
    public DateTime BaslangicTarihi { get; set; }
    public string? Aciklama { get; set; } // Yeni

}