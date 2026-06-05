namespace SantiyeAPI.Models;

public enum KasaIslemYonu
{
    Giris = 1,  // Kasaya giren para
    Cikis = 2   // Kasadan çıkan para
}

public enum KasaHareketTipi
{
    Avans = 1,
    MaasOdemesi = 2,
    Transfer = 3,
    ManuelGider = 4,
    ManuelGelir = 5,     // Sermaye veya dış gelir
    IadeTersKayit = 6    // Silinen bir işlemin kasaya geri iadesi (Reversal)
}