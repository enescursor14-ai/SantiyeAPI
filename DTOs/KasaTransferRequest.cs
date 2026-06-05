namespace SantiyeAPI.DTOs;

public class KasaTransferRequest
{
    public int GonderenKasaId { get; set; }
    public int AliciKasaId { get; set; }
    public decimal Tutar { get; set; }
    public string? Aciklama { get; set; }
}