using System;

namespace SantiyeAPI.Models
{
    public class SatinAlmaGecmisi
    {
        public int Id { get; set; }
        public int CompanyId { get; set; } // Hangi firma aldı?
        public DateTime Tarih { get; set; } // Ne zaman aldı?
        public int AlinanJetonSayisi { get; set; } // Kaç tane aldı?
        public decimal OdenenTutar { get; set; } // Kaç para ödedi?
    }
}