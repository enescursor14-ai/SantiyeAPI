using AutoMapper;
using SantiyeAPI.DTOs;
using SantiyeAPI.Models;

namespace SantiyeAPI.Mappings; // Kendi proje yapına göre namespace'i ayarla

public class IsciMappingProfile : Profile
{
    public IsciMappingProfile()
    {
        // --- OKUMA İŞLEMLERİ İÇİN (Veritabanından -> DTO'ya) ---
        // AutoMapper Profil Dosyanın İçi

        CreateMap<Isci, IsciListDto>()
    .ForMember(dest => dest.SantiyeAdlari,
               opt => opt.MapFrom(src => src.SantiyeIsciler
                                            .Where(si => si.AktifMi)
                                            .Select(si => si.Santiye != null ? si.Santiye.Ad : "")  // ✅ Null check
                                            .Where(ad => !string.IsNullOrEmpty(ad))  // ✅ Boş olanları filtrele
                                            .ToList()));

CreateMap<Isci, IsciDetailDto>()
    .ForMember(dest => dest.SantiyeAdlari,
               opt => opt.MapFrom(src => src.SantiyeIsciler
                                            .Where(si => si.AktifMi == true) 
                                            .Select(si => si.Santiye != null ? si.Santiye.Ad : "")  // ✅ Null check
                                            .Where(ad => !string.IsNullOrEmpty(ad))  // ✅ Boş olanları filtrele
                                            .ToList())); // ✅ ToList unutulmamış!
        CreateMap<Santiye, SantiyeBasitDto>();
        CreateMap<Avans, AvansBasitDto>();
        CreateMap<GunlukKayit, GunlukKayitBasitDto>();

        // --- YAZMA İŞLEMLERİ İÇİN (DTO'dan -> Veritabanına) ---
        CreateMap<IsciCreateDto, Isci>();
        CreateMap<IsciUpdateDto, Isci>();
    }
}