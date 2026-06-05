using FluentValidation;
using SantiyeAPI.Controllers;
using SantiyeAPI.DTOs;

namespace SantiyeAPI.Validators;

public class PatronCreateDtoValidator : AbstractValidator<PatronCreateDto>
{
    public PatronCreateDtoValidator()
    {
        RuleFor(x => x.Ad)
            .NotEmpty().WithMessage("Ortak/Patron adı boş bırakılamaz.")
            .MaximumLength(50).WithMessage("Ad alanı en fazla 50 karakter olabilir.");

        RuleFor(x => x.Soyad)
            .NotEmpty().WithMessage("Patronun soyadı boş olamaz.");

        RuleFor(x => x.Telefon)
            .NotEmpty().WithMessage("Telefon numarası zorunludur.")
            .Matches(@"^\d{11}$").WithMessage("Telefon 11 haneli ve sadece rakamdan oluşmalıdır. (Örn: 05XX...)");

        RuleFor(x => x.Unvan)
            .NotEmpty().WithMessage("Ünvan boş bırakılamaz. (Örn: Finansör, Şantiye Şefi)")
            .MaximumLength(50).WithMessage("Ünvan en fazla 50 karakter olabilir.");
    }
}