namespace SantiyeAPI.Models;

public class HarcamaKategori
{
    public int Id { get; set; }
    public string Ad { get; set; } = string.Empty; // Nalburiye, Yemek, Yakıt vb.
    public bool AktifMi { get; set; } = true;
}