namespace LefteriaFc.Application.Trials;

/// <summary>
/// The only authorized runtime query is a fixed-source, closed display
/// projection. Never expose the private admin settings table or athlete data.
/// </summary>
public interface IFirstTeamTrialDisplayReader
{
    Task<FirstTeamTrialsSettings?> ReadAsync(CancellationToken ct);
}
