using System.Drawing;

public class ChatTellerTests
{
    static readonly DateTimeOffset T0 = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    static Bericht B(int minuut, string van = "ander", bool isBericht = true) => new(T0.AddMinutes(minuut), van, isBericht);

    [Fact]
    public void Basislijn_is_de_laatste_van_ack_en_teams_gelezen()
    {
        Assert.Equal(T0.AddMinutes(5), ChatTeller.Basislijn(T0.AddMinutes(5), T0));
        Assert.Equal(T0.AddMinutes(5), ChatTeller.Basislijn(T0, T0.AddMinutes(5)));
        Assert.Equal(T0, ChatTeller.Basislijn(null, T0));
        Assert.Equal(T0, ChatTeller.Basislijn(T0, null));
        Assert.Null(ChatTeller.Basislijn(null, null));
    }

    [Fact]
    public void Telt_alleen_echte_berichten_van_anderen_na_de_basislijn()
    {
        var t = ChatTeller.Tel([B(5), B(4, isBericht: false), B(3), B(2), B(1)], T0.AddMinutes(2), "ik");

        Assert.Equal(2, t.Aantal);
        Assert.Equal(T0.AddMinutes(5), t.Nieuwste);
        Assert.Null(t.EigenBericht);
    }

    [Fact]
    public void Eigen_bericht_stopt_de_telling_en_geeft_auto_ack()
    {
        var t = ChatTeller.Tel([B(5), B(4, "ik"), B(3)], T0, "ik");

        Assert.Equal(1, t.Aantal);
        Assert.Equal(T0.AddMinutes(4), t.EigenBericht);
    }

    [Fact]
    public void Niets_nieuws_geeft_nul()
    {
        var t = ChatTeller.Tel([B(1)], T0.AddMinutes(1), "ik");

        Assert.Equal(0, t.Aantal);
        Assert.Null(t.Nieuwste);
    }
}

public class PlaatsingTests
{
    static readonly Rectangle Wa = new(0, 0, 1000, 800);

    [Fact]
    public void BottomRight_stapelt_omhoog()
    {
        var p = Plaatsing.Bereken(Position.BottomRight, Wa, [new Size(200, 100), new Size(200, 50)]);

        Assert.Equal([new Point(790, 690), new Point(790, 630)], p);
    }

    [Fact]
    public void MiddleRight_centreert_de_stapel_verticaal()
    {
        var p = Plaatsing.Bereken(Position.MiddleRight, Wa, [new Size(300, 100), new Size(300, 100)]);

        Assert.Equal([new Point(690, 295), new Point(690, 405)], p);
    }

    [Fact]
    public void TopLeft_stapelt_omlaag()
    {
        var p = Plaatsing.Bereken(Position.TopLeft, Wa, [new Size(200, 100), new Size(200, 100)]);

        Assert.Equal([new Point(10, 10), new Point(10, 120)], p);
    }
}
