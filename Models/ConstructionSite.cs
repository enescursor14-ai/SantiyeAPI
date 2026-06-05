public class ConstructionSite
{
    public int Id { get; set; }
    public required string  Name { get; set; }
    public int CompanyId { get; set; }
    
    // Şantiye şu an aktif mi (para ödenmiş mi) yoksa arşivde/süresi bitmiş mi?
    public bool IsActive { get; set; } = false;
    
    // Bu şantiyenin lisansı ne zaman bitiyor?
    public DateTime? LicenseEndDate { get; set; }

    public Company? Company { get; set; }
}