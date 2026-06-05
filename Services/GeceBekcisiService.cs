using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SantiyeAPI.Data;
using SantiyeAPI.Helpers;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SantiyeApp.Services
{
    public class GeceBekcisiService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GeceBekcisiService> _logger;

        public GeceBekcisiService(IServiceProvider serviceProvider, ILogger<GeceBekcisiService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Gece Bekçisi devriyeye başladı...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        DateTime trSimdi = ZamanMotoru.SimdiTurkiye();

                        var kapanacakSantiyeIdleri = await _context.Santiyeler
                            .Where(s => s.AktifMi == true && s.LisansBitisTarihi != null && s.LisansBitisTarihi < trSimdi)
                            .Select(s => s.Id)
                            .ToListAsync(stoppingToken);

                        if (kapanacakSantiyeIdleri.Any())
                        {
                            int kapananSantiyeSayisi = await _context.Santiyeler
                                .Where(s => kapanacakSantiyeIdleri.Contains(s.Id))
                                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AktifMi, false), stoppingToken);

                            _logger.LogInformation("[BEKÇİ] {SantiyeAdet} adet şantiye donduruldu.", kapananSantiyeSayisi);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[BEKÇİ HATA] Devriyede sorun oluştu!");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}