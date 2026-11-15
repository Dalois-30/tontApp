namespace Application.TontineManagement.Commands.RejoindreParCode;

using Application.Common;

/// <summary>
/// Joins a tontine with an invitation code. Draft tontine: immediate membership ("actif").
/// Active tontine: membership request pending the gestionnaire's approval ("en_attente").
/// </summary>
public sealed record RejoindreParCodeCommand(
    string Code,
    string MemberName,
    Guid UtilisateurId) : ICommand<string>;
