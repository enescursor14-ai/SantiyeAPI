using System.ComponentModel.DataAnnotations;

namespace SantiyeAPI.DTOs;

public class MaasOdeRequest
{
    [Required]
    public int IsciId { get; set; }
    
    [Required]
    public int KasaId { get; set; }
    
    [Required]
    public int SantiyeId { get; set; } 
    
    [Range(0.01, 500000, ErrorMessage = "Tutar 0'dan büyük olmalıdır.")]
    public decimal Tutar { get; set; }
    
    public string? Aciklama { get; set; }
}