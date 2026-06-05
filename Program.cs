using Microsoft.EntityFrameworkCore;
using SantiyeAPI.Data;
using SantiyeAPI.Services;
using SantiyeAPI.Middlewares;
using System.Text.Json.Serialization;
using FluentValidation.AspNetCore;
using FluentValidation;
using SantiyeAPI.Validators;
using AutoMapper;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using SantiyeApp.Services;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;



var builder = WebApplication.CreateBuilder(args);
// 🚀 ZIRH 4: Uygulama portunu koda gömerek sabitliyoruz
builder.WebHost.UseUrls("http://localhost:5095");

// 🚀 ZIRH 1: DDOS ve Çift Tıklama Koruması
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("PuantajLimiter", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromSeconds(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 2;
    });
});

builder.Services.AddScoped<IIsciService, IsciService>();
builder.Services.AddScoped<IKasaService, KasaService>();
builder.Services.AddScoped<SiteService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("BabaninEkrani", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var hatalar = context.ModelState.Values
                .Where(v => v.Errors.Count > 0)
                .Select(v => v.Errors.First().ErrorMessage)
                .ToList();
            return new BadRequestObjectResult(new { mesaj = string.Join("<br><br>", hatalar) });
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAutoMapper(typeof(Program).Assembly);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"), o =>
        o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

builder.Services.AddMemoryCache();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddScoped<IsciCreateDtoValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<IsciCreateDtoValidator>();


var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("BabaninEkrani");
app.UseRateLimiter(); // DDOS korumasını çalıştırır

// 🚀 ZIRH 2: DİKKAT! Yorum satırlarını sildik ki Babanın bilgisayarında HTML dosyaları (arayüz) açılsın!
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();



// 🚀 ZIRH 3: Veritabanı yoksa oluştur ve Şirketi "0 Jeton" ile ekle!
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "Data"));

        dbContext.Database.EnsureCreated();
        string dbTamYol = Path.Combine(AppContext.BaseDirectory, "Data", "SantiyeDB.db");
        var dbDosyasi = new FileInfo(dbTamYol);
        if (dbDosyasi.Exists)
            dbDosyasi.Attributes = FileAttributes.Hidden;

        // 🚀 SQLITE TURBO MODU
        dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        dbContext.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");

        // 👑 ESKİ KRAL GERİ DÖNDÜ: Program açılırken 1 kere çalışır, kimseyle yarışmaz!
        bool sirketVarMi = dbContext.Companies.Any();
        if (!sirketVarMi)
        {
            dbContext.Database.ExecuteSqlRaw("INSERT INTO Companies (Name, AllowedActiveSiteCount, DonanimKimligi, SonIslemTarihi) VALUES ('Bizim Şantiye A.Ş.', 0, 'BEKLIYOR', datetime('now'))");
            Console.WriteLine("✅ Şantiye veritabanı kuruldu ve lisans sistemi hazırlandı!");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"DB Hatası: {ex.Message}");
    }
}

// 🚀 ZIRH 6: KALP ATIŞI (HEARTBEAT) PROTOKOLÜ 
DateTime? sonKalpAtisi = null;
DateTime sunucuAcilisZamani = DateTime.Now;

// Babanın ekranından (JS) sürekli bu adrese ping gelecek
// OLMASI GEREKEN
app.MapGet("/api/kalpatisi", () =>
{
    sonKalpAtisi = DateTime.Now; // ← Bu satır eksikti!
    return Results.Ok();
})
.RequireCors("BabaninEkrani");

// 🚀 ZIRH 5 & 6 BİRLEŞİMİ: Tarayıcıyı Açma ve Ölüm Kontrolü
app.Lifetime.ApplicationStarted.Register(() =>
{
    // 1. Görev: Tarayıcıyı Edge ile aç
    Task.Run(async () =>
    {
        var url = "http://localhost:5095";
        using var http = new HttpClient();
        for (int i = 0; i < 10; i++) 
        {
            try { await http.GetAsync(url); break; }
            catch { await Task.Delay(500); }
        }
        bool launched = false;
        if (OperatingSystem.IsWindows())
        {
            launched = TryLaunch("msedge", $"--app={url}");
        }
        else if (OperatingSystem.IsMacOS())
        {
            launched = TryLaunch("open", $"-a \"Microsoft Edge\" --args --app={url}");
            if (!launched) launched = TryLaunch("open", url);
        }
        if (!launched) TryLaunch(url, null);
    });

    // 2. Görev: Çarpıya basıldığını (Sinyalin kesildiğini) denetle
    Task.Run(async () =>
    {
        while (true)
        {
            await Task.Delay(3000); // 3 saniyede bir durumu kontrol et

            // Senaryo A: Uygulama açıldı ama babanın bilgisayarında tarayıcı hiç açılamadı. 
            // Arkada sonsuza kadar açık kalmasın, 15 saniye sonra sistemi kapat.
            if (sonKalpAtisi == null && (DateTime.Now - sunucuAcilisZamani).TotalSeconds > 180)
            {
                app.Lifetime.StopApplication();
                break;
            }

            // Senaryo B: Tarayıcı açıktı ama sinyal kesildi (Baban çarpıya bastı).
            // 6 saniye tahammül et (sayfayı yenilerse yani F5 atarsa yanlışlıkla kapanmasın diye),
            // hala ses yoksa arkadaki hayalet API'yi tamamen öldür!
            if (sonKalpAtisi != null && (DateTime.Now - sonKalpAtisi.Value).TotalSeconds > 1200)
            {
                Console.WriteLine("Çarpıya basıldı, arka plan temizlenip kapanıyor...");
                app.Lifetime.StopApplication();
                break;
            }
        }
    });
});

app.Run();

// --- TryLaunch metodu olduğu gibi kalacak ---



// ---

static bool TryLaunch(string fileName, string? args)
{
    try
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args ?? "",
            UseShellExecute = true
        });
        return true;
    }
    catch { return false; }
}