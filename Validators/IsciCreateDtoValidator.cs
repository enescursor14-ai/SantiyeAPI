using FluentValidation;
using SantiyeAPI.DTOs; // Senin DTO'larının olduğu yer



namespace SantiyeAPI.Validators;

public class IsciCreateDtoValidator : AbstractValidator<IsciCreateDto>
{
    public IsciCreateDtoValidator()
    {
        // TC Zırhı: Boş olamaz, tam 11 hane olmalı, SADECE RAKAM olmalı
        RuleFor(x => x.TcNo)
            .NotEmpty().WithMessage("TC Kimlik boş bırakılamaz usta.")
            .Length(11).WithMessage("TC Kimlik tam 11 haneli olmalıdır.")
            .Matches(@"^\d{11}$").WithMessage("TC Kimlik sadece rakamlardan oluşabilir! Sembol veya harf giremezsin.");

        // 2. AD VE SOYAD KONTROLLERİ
        RuleFor(x => x.Ad)
            .NotEmpty().WithMessage("İşçinin adı boş olamaz usta.")
            .MaximumLength(50).WithMessage("Ad çok uzun, en fazla 50 karakter gir ustam.");

        RuleFor(x => x.Soyad)
            .NotEmpty().WithMessage("Soyadı boş bırakma patron.")
            .MaximumLength(50).WithMessage("Soyad 50 karakteri geçemez.");

        // 3. GÜNLÜK ÜCRET (YEVMİYE) KONTROLÜ - Eksi bakiye girilmesini önler!
        RuleFor(x => x.GunlukUcret)
            .LessThanOrEqualTo(10000)
            .WithMessage("Yevmiye 10.000 ₺'yi geçemez!")
            .GreaterThan(0).WithMessage("Usta, bedavaya adam mı çalıştırıyoruz? Yevmiye 0'dan büyük olmalı.");

        // 4. TELEFON NUMARASI KONTROLÜ (Eğer girildiyse çalışır)
        RuleFor(x => x.Telefon)
            .MaximumLength(20).WithMessage("Telefon numarası 20 karakteri geçemez usta.")
            // Eğer telefon boş değilse bu kuralı işlet: Sadece rakam, artı (+) ve boşluk olabilir
            .Matches(@"^[0-9\+\s]+$").When(x => !string.IsNullOrWhiteSpace(x.Telefon))
            .WithMessage("Telefonda garip karakterler var usta, sadece rakam ve + kullanabilirsin.");

        // 5. MESLEK KONTROLÜ (İsteğe bağlı)
        RuleFor(x => x.Meslek)
            .MaximumLength(50).WithMessage("Meslek adı çok uzun patron, kısaltalım.");
    }
}