using System.Threading.Tasks;
using SantiyeAPI.DTOs;

namespace SantiyeAPI.Services;

public interface IKasaService
{
    // 1. DTO ile zırhlandırılmış Avans İşlemi
    Task<int> AvansVerAsync(AvansVerRequest request);

    // 2. Audit Trail (Kim Sildi?) eklenmiş İptal İşlemi
    Task<bool> AvansIptalEtAsync(int avansId, string iptalEdenKullaniciId);

    // 3. Çift Taraflı Transfer
    // 🚀 DTO ile Yenilenenler
    Task<bool> KasaTransferYapAsync(KasaTransferRequest request);
    Task<bool> SermayeEkleAsync(SermayeEkleRequest request);
    Task<bool> ManuelGiderEkleAsync(ManuelGiderRequest request);


    Task<decimal> GetKasaBakiyeAsync(int kasaId);
   
}