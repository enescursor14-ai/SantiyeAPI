using System;

namespace SantiyeAPI.DTOs;

public class PatronKasaOzetDto
{
    public int PatronId { get; set; }
    public string PatronAd { get; set; } = string.Empty;
    public int KasaId { get; set; }
    public decimal Bakiye { get; set; }
}
