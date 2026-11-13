using FluentValidation;
using YAZTEK.API.DTOs;

namespace YAZTEK.API.Validators;

public class CreateStudentDtoValidator : AbstractValidator<CreateStudentDto>
{
    public CreateStudentDtoValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("Öğrenci adı boş geçilemez.")
            .MaximumLength(50).WithMessage("Öğrenci adı en fazla 50 karakter olabilir.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Öğrenci soyadı boş geçilemez.");

        RuleFor(x => x.StudentNumber)
            .NotEmpty().WithMessage("Öğrenci numarası boş geçilemez.");

        RuleFor(x => x.GradeLevel)
            .InclusiveBetween(1, 12).WithMessage("Sınıf seviyesi 1 ile 12 arasında olmalıdır.");
    }
}