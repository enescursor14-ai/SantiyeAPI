namespace SantiyeAPI.Models;

public class KasaHareketi
{
    public int Id { get; set; }

    public int KasaId { get; set; }
    public Kasa? Kasa { get; set; }
    public int? SantiyeId { get; set; } // Bunu ekle!
    public virtual Santiye? Santiye { get; set; }

    public decimal Tutar { get; set; } // Negatif olamaz! (Fluent API ile koruyacağız)

    public KasaIslemYonu Yon { get; set; }
    public KasaHareketTipi HareketTipi { get; set; }

    // 🚀 SENIOR DOKUNUŞU: Polimorfik Referans (Hangi tablonun hangi satırından geldi?)
    public string? ReferansTabloAdi { get; set; } // Örn: "Avanslar"
    public int? ReferansId { get; set; }          // Örn: 15 (Avansın ID'si)

    // 🚀 SENIOR DOKUNUŞU: Çift Taraflı Muhasebe (Transferleri bağlayan mühür)
    public Guid? TransferGrupId { get; set; }

    // 🚀 SENIOR DOKUNUŞU: Ters İşlem (Reversal) Bağlantısı
    public int? IptalEdilenIslemId { get; set; }
    public KasaHareketi? IptalEdilenIslem { get; set; }

    //public int? HarcamaKategoriId { get; set; }
    //public HarcamaKategori? Kategori { get; set; }

    public string? Aciklama { get; set; }

    // 🚀 ZAMAN DİSİPLİNİ: Her zaman UTC atılacak!
    public DateTime IslemTarihi { get; set; }

    // 🚀 SOFT DELETE VE AUDIT (Denetim) İZLERİ
    public bool IsDeleted { get; set; } = false;


    //public DateTime OlusturulmaTarihi { get; set; } = DateTime.UtcNow;
    public int? PatronId { get; set; } // Bu harcamayı/ödemeyi hangi patron üstlendi?
    public Patron? Patron { get; set; }

    public bool SifirlamaFisiMi { get; set; } = false;
}