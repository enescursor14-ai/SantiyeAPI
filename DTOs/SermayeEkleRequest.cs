using System.ComponentModel.DataAnnotations;

namespace SantiyeAPI.DTOs;

public class SermayeEkleRequest
{

    [Required(ErrorMessage = "Parayı veren patron seçilmelidir!")]
    public int PatronId { get; set; }
    public int? SantiyeId { get; set; }

    [Required(ErrorMessage = "Tutar girilmelidir!")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Patron, sıfır veya eksi para giremezsin!")]
    public decimal Tutar { get; set; }
    public DateTime? IslemTarihi { get; set; }

    public string? Aciklama { get; set; } // Soru işareti (?) boş geçilebilir demek
}