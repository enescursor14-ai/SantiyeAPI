using System;

namespace SantiyeAPI.Services;

using SantiyeAPI.DTOs;

public interface IIsciService
{
    // Eski Hali: Task<PagedResult<IsciDetailDto>> GetAllAsync...
    Task<PagedResult<IsciListDto>> GetAllAsync(string? aramaKelimesi, string? santiyeFiltre, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<IsciDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<IsciDetailDto> CreateAsync(IsciCreateDto createDto, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(int id, IsciUpdateDto updateDto, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
    // YENİ: Maaş zammı işlemi
    //Task<bool> HesapKapatAsync(int isciId, CancellationToken cancellationToken = default);
    Task<bool> MaasZamYapAsync(int isciId, MaasZamDto dto, CancellationToken cancellationToken);


}
