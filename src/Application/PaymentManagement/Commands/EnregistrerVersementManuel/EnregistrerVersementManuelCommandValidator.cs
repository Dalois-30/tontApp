namespace Application.PaymentManagement.Commands.EnregistrerVersementManuel;

using FluentValidation;

public sealed class EnregistrerVersementManuelCommandValidator
    : AbstractValidator<EnregistrerVersementManuelCommand>
{
    public EnregistrerVersementManuelCommandValidator()
    {
        RuleFor(x => x.TontineId)
            .NotEmpty().WithMessage("TontineId est requis.");

        RuleFor(x => x.TourId)
            .NotEmpty().WithMessage("TourId est requis.");

        RuleFor(x => x.MembreId)
            .NotEmpty().WithMessage("MembreId est requis.");

        RuleFor(x => x.Montant)
            .GreaterThan(0m)
            .WithMessage("Le montant doit être supérieur à 0 FCFA.");

        RuleFor(x => x.DescriptionPreuve)
            .NotEmpty().WithMessage("La description de la preuve est requise.");

        RuleFor(x => x.Devise)
            .NotEmpty().WithMessage("La devise est requise.");
    }
}
