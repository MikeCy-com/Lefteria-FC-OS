namespace LefteriaFc.Application.Trials;

/// <summary>
/// Source-grounded First Team trials display settings. This policy does not
/// enable a public write endpoint, athlete data collection, or an admin grant.
/// </summary>
public sealed record FirstTeamTrialsSettings(
    bool Open,string Headline,string Subtitle,string ButtonText,string ClosedMessage);

public static class FirstTeamTrialsSettingsPolicy
{
    public static FirstTeamTrialsSettings Default => new(
        Open:false,
        Headline:"Ετοιμος να Ξεκινησεις το Ταξιδι σου;",
        Subtitle:"Γίνε μέλος της πρώτης ομάδας LEFTERIA FC και κάνε το πρώτο βήμα για να γίνεις επαγγελματίας ποδοσφαιριστής.",
        ButtonText:"Δηλωσε Συμμετοχη για Δοκιμαστικα",
        ClosedMessage:"Οι εγγραφές για δοκιμαστικά είναι προσωρινά κλειστές. Παρακαλούμε δοκιμάστε ξανά σύντομα.");

    // An explicit 'open' value in an admin command is not sufficient to
    // authorize the actual form or its private writes. A separately gated
    // storage, admin capability and public API must all be verified first.
    public static bool TryValidate(FirstTeamTrialsSettings? settings,out string? error)
    {
        error=null;
        if(settings is null){error="settings_required";return false;}
        if(!ValidText(settings.Headline,180))
            {error="invalid_headline";return false;}
        if(!ValidText(settings.Subtitle,500))
            {error="invalid_subtitle";return false;}
        if(!ValidText(settings.ButtonText,100))
            {error="invalid_button";return false;}
        if(!ValidText(settings.ClosedMessage,500))
            {error="invalid_closed_message";return false;}
        return true;
    }

    private static bool ValidText(string? value,int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length<=max &&
        value==value.Trim() &&
        !value.Any(c=>char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
}
