using System;

namespace SantiyeAPI.Models
{
    public class SantiyeNotu
    {
        public int Id { get; set; }
        public int SantiyeId { get; set; }
        public DateTime Tarih { get; set; }
        public Santiye? Santiye { get; set; }  // ✅ Navigation property eklendi
        public string NotMetni { get; set; } = string.Empty;
        public bool IsDeleted { get; set; } = false;
    }
}