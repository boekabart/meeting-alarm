/// <param name="IsBericht">messageType == "message" en niet verwijderd (dus geen systeemmelding).</param>
sealed record Bericht(DateTimeOffset Gemaakt, string? VanId, bool IsBericht);

/// <param name="Nieuwste">Tijdstip van het nieuwste getelde bericht: daar ackt "gezien" tot.</param>
/// <param name="EigenBericht">Tijdstip van mijn eigen bericht als dat de telling stopte: auto-ack tot daar.</param>
readonly record struct Telling(int Aantal, DateTimeOffset? Nieuwste, DateTimeOffset? EigenBericht);

static class ChatTeller
{
    /// <summary>Graph levert maximaal zoveel berichten per pagina; meer tonen we als "50+".</summary>
    public const int MaxBerichten = 50;

    /// <summary>
    /// Een bericht telt alleen als het ongelezen is in Teams ÉN nieuwer dan de laatste ack.
    /// Beide kunnen berichten alleen wegnemen, nooit toevoegen: dus de laatste van de twee wint.
    /// </summary>
    public static DateTimeOffset? Basislijn(DateTimeOffset? ackedAt, DateTimeOffset? gelezenInTeams) =>
        ackedAt is null ? gelezenInTeams
        : gelezenInTeams is null ? ackedAt
        : ackedAt > gelezenInTeams ? ackedAt : gelezenInTeams;

    /// <summary>
    /// Telt berichten van anderen die nieuwer zijn dan de basislijn (<paramref name="nieuwsteEerst"/> aflopend op tijd).
    /// Stopt bij de basislijn, of bij een eigen bericht: wie zelf iets stuurt, heeft de chat gelezen.
    /// </summary>
    public static Telling Tel(IEnumerable<Bericht> nieuwsteEerst, DateTimeOffset basislijn, string mijnId)
    {
        int aantal = 0;
        DateTimeOffset? nieuwste = null;
        foreach (var b in nieuwsteEerst)
        {
            if (b.Gemaakt <= basislijn) break;
            if (b.VanId == mijnId) return new(aantal, nieuwste, b.Gemaakt);
            if (!b.IsBericht) continue;
            aantal++;
            nieuwste ??= b.Gemaakt;
        }
        return new(aantal, nieuwste, null);
    }
}
