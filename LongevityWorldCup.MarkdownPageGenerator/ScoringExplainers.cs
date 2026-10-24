using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

// Wordless, scroll-driven explainers beside the paragraphs that say how the
// competition scores: an age-gap gauge, a season timeline, a ranking ladder and
// a division grid. The paragraphs carry the words; the pictures only show them.
// Each figure is rendered in its finished state, so without scripts or with
// reduced motion it reads as a static diagram. The script rewinds a figure and
// lets the reader's scroll draw it, and hover or tap plays with it.
internal static partial class ScoringExplainers
{
    private static int _instance;

    public static bool AppliesTo(string canonicalPath) => canonicalPath is "/about" or "/ruleset";

    public static string Inject(string documentHtml, string canonicalPath)
    {
        if (canonicalPath == "/about")
        {
            documentHtml = InsertAfterParagraph(documentHtml, "ranks athletes by Age reduction", Gauge());
            documentHtml = InsertAfterParagraph(documentHtml, "The competition has tracks, seasons, leagues", Season());
        }
        else if (canonicalPath == "/ruleset")
        {
            documentHtml = InsertAfter(documentHtml, "final rankings lock for that season.</li>", "</ul>", Season());
            documentHtml = InsertAfter(documentHtml, "still compete within your generation league.</li>", "</ul>", Ladder() + "\n" + Grid());
        }

        return documentHtml;
    }

    private static string InsertAfterParagraph(string html, string marker, string figure)
    {
        var at = html.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
        {
            return html;
        }

        var end = html.IndexOf("</p>", at, StringComparison.Ordinal);
        return end < 0 ? html : html.Insert(end + 4, "\n" + figure);
    }

    private static string InsertAfter(string html, string marker, string closing, string figure)
    {
        var at = html.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
        {
            return html;
        }

        var end = html.IndexOf(closing, at, StringComparison.Ordinal);
        return end < 0 ? html : html.Insert(end + closing.Length, "\n" + figure);
    }

    private static string F(double value) => Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- gauge
    // A dial of age. The hollow ring is chronological age; the needle is
    // biological age; the lit arc between them is the Age reduction.
    private const double GaugeCx = 180, GaugeCy = 186, GaugeR = 140, GaugeChrono = 0.72, GaugeBio = 0.42;

    private static (double X, double Y) GaugePoint(double v, double radius) =>
        (GaugeCx - radius * Math.Cos(v * Math.PI), GaugeCy - radius * Math.Sin(v * Math.PI));

    private static string Gauge()
    {
        var arc = $"M {F(GaugeCx - GaugeR)} {F(GaugeCy)} A {F(GaugeR)} {F(GaugeR)} 0 0 1 {F(GaugeCx + GaugeR)} {F(GaugeCy)}";
        var zoneRadius = GaugeR - 26;
        var zoneEnd = GaugePoint(GaugeChrono, zoneRadius);
        var zone = $"M {F(GaugeCx - zoneRadius)} {F(GaugeCy)} A {F(zoneRadius)} {F(zoneRadius)} 0 0 1 {F(zoneEnd.X)} {F(zoneEnd.Y)}";
        var svg = new StringBuilder();
        svg.Append($"<svg class=\"gauge-svg\" viewBox=\"0 0 360 206\" focusable=\"false\">");
        svg.Append($"<path class=\"gauge-zone\" d=\"{zone}\"/>");
        for (var k = 0; k <= 36; k++)
        {
            var v = k / 36.0;
            var inner = GaugePoint(v, GaugeR + 13);
            var outer = GaugePoint(v, GaugeR + (k % 6 == 0 ? 25 : 19));
            var lit = v >= GaugeBio && v <= GaugeChrono ? " is-lit" : string.Empty;
            svg.Append($"<line class=\"gauge-tick{lit}\" data-v=\"{F(v)}\" x1=\"{F(inner.X)}\" y1=\"{F(inner.Y)}\" x2=\"{F(outer.X)}\" y2=\"{F(outer.Y)}\"/>");
        }

        svg.Append($"<path class=\"gauge-track\" d=\"{arc}\" pathLength=\"100\"/>");
        svg.Append($"<path class=\"gauge-gap\" d=\"{arc}\" pathLength=\"100\" stroke-dasharray=\"{F((GaugeChrono - GaugeBio) * 100)} 200\" stroke-dashoffset=\"{F(-GaugeBio * 100)}\"/>");
        var chrono = GaugePoint(GaugeChrono, GaugeR);
        svg.Append($"<line class=\"gauge-chrono-line\" x1=\"{F(GaugeCx)}\" y1=\"{F(GaugeCy)}\" x2=\"{F(chrono.X)}\" y2=\"{F(chrono.Y)}\"/>");
        var tip = GaugePoint(GaugeChrono, GaugeR + 30);
        var left = GaugePoint(GaugeChrono - 0.022, GaugeR + 42);
        var right = GaugePoint(GaugeChrono + 0.022, GaugeR + 42);
        svg.Append($"<path class=\"gauge-chrono-notch\" d=\"M {F(tip.X)} {F(tip.Y)} L {F(left.X)} {F(left.Y)} L {F(right.X)} {F(right.Y)} Z\"/>");
        svg.Append($"<circle class=\"gauge-chrono\" cx=\"{F(chrono.X)}\" cy=\"{F(chrono.Y)}\" r=\"10\"/>");
        svg.Append($"<g class=\"gauge-needle\" transform=\"rotate({F(GaugeBio * 180)} {F(GaugeCx)} {F(GaugeCy)})\">");
        svg.Append($"<line x1=\"{F(GaugeCx)}\" y1=\"{F(GaugeCy)}\" x2=\"{F(GaugeCx - GaugeR + 14)}\" y2=\"{F(GaugeCy)}\"/>");
        svg.Append($"<circle class=\"gauge-needle-glow\" cx=\"{F(GaugeCx - GaugeR)}\" cy=\"{F(GaugeCy)}\" r=\"17\"/>");
        svg.Append($"<circle class=\"gauge-needle-tip\" cx=\"{F(GaugeCx - GaugeR)}\" cy=\"{F(GaugeCy)}\" r=\"9\"/>");
        svg.Append("</g>");
        svg.Append($"<circle class=\"gauge-hub\" cx=\"{F(GaugeCx)}\" cy=\"{F(GaugeCy)}\" r=\"13\"/>");
        svg.Append($"<circle class=\"gauge-hub-dot\" cx=\"{F(GaugeCx)}\" cy=\"{F(GaugeCy)}\" r=\"5\"/>");
        svg.Append("</svg>");
        return Figure("gauge", svg.ToString(), $" data-chrono=\"{F(GaugeChrono)}\" data-bio=\"{F(GaugeBio)}\" data-cx=\"{F(GaugeCx)}\" data-cy=\"{F(GaugeCy)}\"");
    }

    // ---------------------------------------------------------------- season
    // Results drop onto the calendar year as the playhead passes; the best
    // (lowest) one so far steps down; a result before January 1 does not count;
    // at the mid-January close the lock snaps shut and the final mark turns gold.
    // Below, the all-time lane never closes.
    private const double SeasonStart = 44, SeasonEnd = 296, SeasonClose = 318, SeasonBar = 150;

    private static readonly (double X, double Y, bool Valid)[] SeasonResults =
    [
        (28, 112, false), (60, 64, true), (101, 82, true), (143, 74, true), (184, 98, true), (226, 90, true), (262, 114, true), (288, 106, true)
    ];

    private static readonly double[] AllTimeResults = [22, 64, 108, 152, 196, 240, 284, 330];

    private static string Season()
    {
        var id = ++_instance;
        var svg = new StringBuilder();
        svg.Append("<svg class=\"season-svg\" viewBox=\"0 0 360 230\" focusable=\"false\">");
        svg.Append($"<defs><clipPath id=\"season-clip-{id}\"><rect class=\"season-clip\" x=\"0\" y=\"0\" width=\"{F(SeasonClose + 2)}\" height=\"230\"/></clipPath>");
        svg.Append($"<linearGradient id=\"season-fade-{id}\" gradientUnits=\"userSpaceOnUse\" x1=\"6\" x2=\"354\" y1=\"0\" y2=\"0\"><stop offset=\"0\" stop-color=\"currentColor\" stop-opacity=\"0\"/><stop offset=\"0.12\" stop-color=\"currentColor\" stop-opacity=\"1\"/><stop offset=\"0.88\" stop-color=\"currentColor\" stop-opacity=\"1\"/><stop offset=\"1\" stop-color=\"currentColor\" stop-opacity=\"0\"/></linearGradient></defs>");
        svg.Append($"<rect class=\"season-window\" x=\"{F(SeasonStart)}\" y=\"36\" width=\"{F(SeasonEnd - SeasonStart)}\" height=\"{F(SeasonBar - 30)}\" rx=\"8\"/>");
        svg.Append($"<text class=\"season-year\" x=\"{F(SeasonStart + 8)}\" y=\"28\">2026</text>");
        for (var m = 0; m <= 12; m++)
        {
            var x = SeasonStart + m * (SeasonEnd - SeasonStart) / 12;
            svg.Append($"<line class=\"season-month{(m % 3 == 0 ? " is-quarter" : string.Empty)}\" x1=\"{F(x)}\" y1=\"{F(SeasonBar - (m % 3 == 0 ? 9 : 6))}\" x2=\"{F(x)}\" y2=\"{F(SeasonBar + (m % 3 == 0 ? 9 : 6))}\"/>");
        }

        svg.Append($"<line class=\"season-before\" x1=\"14\" y1=\"{F(SeasonBar)}\" x2=\"{F(SeasonStart - 6)}\" y2=\"{F(SeasonBar)}\"/>");
        svg.Append($"<rect class=\"season-bar\" x=\"{F(SeasonStart)}\" y=\"{F(SeasonBar - 3.5)}\" width=\"{F(SeasonEnd - SeasonStart)}\" height=\"7\" rx=\"3.5\"/>");
        svg.Append($"<line class=\"season-grace\" x1=\"{F(SeasonEnd + 4)}\" y1=\"{F(SeasonBar)}\" x2=\"{F(SeasonClose - 9)}\" y2=\"{F(SeasonBar)}\"/>");

        // The best (lowest) result so far: a staircase that only ever steps down.
        var path = new StringBuilder();
        var best = double.NegativeInfinity;
        foreach (var result in SeasonResults.Where(r => r.Valid))
        {
            if (path.Length == 0)
            {
                path.Append($"M {F(result.X)} {F(result.Y)}");
                best = result.Y;
            }
            else if (result.Y > best)
            {
                path.Append($" H {F(result.X)} V {F(result.Y)}");
                best = result.Y;
            }
        }

        path.Append($" H {F(SeasonClose)}");
        svg.Append($"<g clip-path=\"url(#season-clip-{id})\"><rect class=\"season-bar-fill\" x=\"{F(SeasonStart)}\" y=\"{F(SeasonBar - 3.5)}\" width=\"{F(SeasonEnd - SeasonStart)}\" height=\"7\" rx=\"3.5\"/><path class=\"season-best\" d=\"{path}\"/></g>");
        for (var i = 0; i < SeasonResults.Length; i++)
        {
            var (x, y, valid) = SeasonResults[i];
            svg.Append($"<g class=\"season-result{(valid ? string.Empty : " is-void")} is-in\" data-x=\"{F(x)}\"><line class=\"season-stem\" x1=\"{F(x)}\" y1=\"{F(y + 7)}\" x2=\"{F(x)}\" y2=\"{F(SeasonBar - 6)}\"/><circle cx=\"{F(x)}\" cy=\"{F(y)}\" r=\"6.5\"/>");
            if (!valid)
            {
                svg.Append($"<path class=\"season-void\" d=\"M {F(x - 4)} {F(y - 4)} L {F(x + 4)} {F(y + 4)} M {F(x + 4)} {F(y - 4)} L {F(x - 4)} {F(y + 4)}\"/>");
            }

            svg.Append("</g>");
        }

        svg.Append($"<circle class=\"season-final\" cx=\"{F(SeasonClose)}\" cy=\"{F(best)}\" r=\"8\"/>");
        svg.Append($"<g class=\"season-lock\" transform=\"translate({F(SeasonClose)} {F(SeasonBar)})\"><path class=\"season-lock-shackle\" d=\"M -5 -3 V -8 A 5 5 0 0 1 5 -8 V -3\"/><rect class=\"season-lock-body\" x=\"-8\" y=\"-4\" width=\"16\" height=\"13\" rx=\"3\"/></g>");
        svg.Append($"<line class=\"season-playhead\" x1=\"0\" y1=\"34\" x2=\"0\" y2=\"{F(SeasonBar + 12)}\" transform=\"translate({F(SeasonClose)} 0)\"/>");

        // All time: the lane runs off both edges and never locks.
        const double lane = 204;
        svg.Append($"<line class=\"alltime-lane\" x1=\"6\" y1=\"{F(lane)}\" x2=\"354\" y2=\"{F(lane)}\" stroke=\"url(#season-fade-{id})\"/>");
        for (var i = 0; i < AllTimeResults.Length; i++)
        {
            var x = AllTimeResults[i];
            svg.Append($"<circle class=\"alltime-result{(i == 2 ? " is-best" : string.Empty)} is-in\" data-x=\"{F(Math.Min(x, SeasonClose - 4))}\" cx=\"{F(x)}\" cy=\"{F(lane)}\" r=\"{(i == 2 ? "6.5" : "4.5")}\"/>");
        }

        svg.Append($"<circle class=\"alltime-runner\" cx=\"0\" cy=\"{F(lane)}\" r=\"3\"/>");
        svg.Append("</svg>");
        return Figure("season", svg.ToString(), $" data-start=\"14\" data-close=\"{F(SeasonClose)}\" data-locked=\"true\"");
    }

    // ---------------------------------------------------------------- ladder
    // Five athletes. Hollow ring: chronological age. Small dots: every test.
    // Solid dot: the best (lowest). The lit bar between is the reduction, and
    // the rows sort by it. Two equal bars break the tie by the older athlete.
    private static readonly (double Chrono, double Best, double[] Tests, int Start, int Rank)[] LadderRows =
    [
        (62, 49, [55, 52.5], 2, 0),
        (70, 58, [64, 61], 4, 1),
        (55, 47, [50.5], 3, 2),
        (45, 37, [42, 39.5], 0, 3),
        (38, 36, [41], 1, 4)
    ];

    private static double LadderX(double age) => 66 + (age - 30) * 6;

    private static string Ladder()
    {
        var svg = new StringBuilder();
        svg.Append("<svg class=\"ladder-svg\" viewBox=\"0 0 360 222\" focusable=\"false\">");
        for (var age = 35; age <= 70; age += 5)
        {
            svg.Append($"<line class=\"ladder-grid{(age % 10 == 0 ? " is-major" : string.Empty)}\" x1=\"{F(LadderX(age))}\" y1=\"6\" x2=\"{F(LadderX(age))}\" y2=\"212\"/>");
        }

        for (var i = 0; i < LadderRows.Length; i++)
        {
            var (chrono, best, tests, start, rank) = LadderRows[i];
            var medal = rank switch { 0 => "gold", 1 => "silver", 2 => "bronze", _ => "none" };
            svg.Append($"<g class=\"ladder-row{(i == 2 ? " is-elder" : string.Empty)}{(i is 2 or 3 ? " is-tied" : string.Empty)}\" data-row=\"{i}\" style=\"--start:{start};--rank:{rank}\">");
            svg.Append("<rect class=\"ladder-hit\" x=\"0\" y=\"-19\" width=\"360\" height=\"38\" rx=\"8\"/>");
            svg.Append($"<circle class=\"ladder-medal\" data-medal=\"{medal}\" cx=\"24\" cy=\"0\" r=\"8\"/>");
            svg.Append($"<line class=\"ladder-track\" x1=\"{F(LadderX(30))}\" y1=\"0\" x2=\"{F(LadderX(chrono))}\" y2=\"0\"/>");
            for (var t = 0; t < tests.Length; t++)
            {
                svg.Append($"<circle class=\"ladder-test\" style=\"--i:{t}\" cx=\"{F(LadderX(tests[t]))}\" cy=\"0\" r=\"4\"/>");
            }

            svg.Append($"<rect class=\"ladder-gap\" x=\"{F(LadderX(best))}\" y=\"-5\" width=\"{F(LadderX(chrono) - LadderX(best))}\" height=\"10\" rx=\"5\"/>");
            svg.Append($"<circle class=\"ladder-best\" style=\"--i:{tests.Length}\" cx=\"{F(LadderX(best))}\" cy=\"0\" r=\"6.5\"/>");
            svg.Append($"<circle class=\"ladder-chrono\" cx=\"{F(LadderX(chrono))}\" cy=\"0\" r=\"6\"/>");
            svg.Append("</g>");
        }

        svg.Append($"<path class=\"ladder-tie\" d=\"M 12 {F(LadderY(2))} q -8 0 -8 8 V {F(LadderY(3) - 8)} q 0 8 8 8\"/>");
        svg.Append("</svg>");
        return Figure("ladder", svg.ToString(), string.Empty);
    }

    private static double LadderY(int slot) => 26 + slot * 42;

    // ---------------------------------------------------------------- grid
    // Everyone on one global line, then the same athletes in their division
    // (columns) and generation (rows) leagues, each with its own leader.
    private static readonly string[] Divisions = ["fa-mars", "fa-venus", "fa-robot"];
    private static readonly string[] Generations = ["fa-radio", "fa-tv", "fa-desktop", "fa-laptop", "fa-mobile-screen-button", "fa-rocket"];

    private static readonly int[,] GridCounts =
    {
        { 1, 0, 0 },
        { 3, 2, 0 },
        { 4, 3, 1 },
        { 5, 3, 1 },
        { 2, 1, 1 },
        { 0, 0, 1 }
    };

    private static string Grid()
    {
        var athletes = new List<(int Row, int Col)>();
        for (var r = 0; r < Generations.Length; r++)
        {
            for (var c = 0; c < Divisions.Length; c++)
            {
                for (var n = 0; n < GridCounts[r, c]; n++)
                {
                    athletes.Add((r, c));
                }
            }
        }

        // A fixed shuffle stands in for the global ranking.
        var seed = 129u;
        var ranked = athletes
            .Select(a => (Athlete: a, Key: seed = seed * 1103515245u + 12345u))
            .OrderBy(a => a.Key)
            .Select(a => a.Athlete)
            .ToList();

        var html = new StringBuilder();
        html.Append("<div class=\"grid-strip\">");
        for (var i = 0; i < ranked.Count; i++)
        {
            var medal = i switch { 0 => "gold", 1 => "silver", 2 => "bronze", _ => "none" };
            html.Append($"<span class=\"grid-dot\" data-cell=\"{ranked[i].Row}-{ranked[i].Col}\" data-medal=\"{medal}\" style=\"--i:{i}\"></span>");
        }

        html.Append("</div>");
        html.Append("<div class=\"grid-board\">");
        html.Append("<span class=\"grid-corner\"><i class=\"fa-solid fa-earth-americas\"></i></span>");
        for (var c = 0; c < Divisions.Length; c++)
        {
            html.Append($"<span class=\"grid-head grid-col-head\" data-col=\"{c}\"><i class=\"fa-solid {Divisions[c]}\"></i></span>");
        }

        var cellIndex = 0;
        for (var r = 0; r < Generations.Length; r++)
        {
            html.Append($"<span class=\"grid-head grid-row-head\" data-row=\"{r}\"><i class=\"fa-solid {Generations[r]}\"></i></span>");
            for (var c = 0; c < Divisions.Length; c++)
            {
                var members = ranked.Select((a, i) => (a, i)).Where(x => x.a.Row == r && x.a.Col == c).ToList();
                html.Append($"<span class=\"grid-cell{(members.Count == 0 ? " is-empty" : string.Empty)}\" data-row=\"{r}\" data-col=\"{c}\" data-cell=\"{r}-{c}\" style=\"--c:{cellIndex++}\">");
                for (var m = 0; m < members.Count; m++)
                {
                    html.Append($"<span class=\"grid-dot{(m == 0 ? " is-lead" : string.Empty)}\" data-medal=\"{(m == 0 ? "gold" : "none")}\" style=\"--i:{m}\"></span>");
                }

                html.Append("</span>");
            }
        }

        html.Append("</div>");
        return Figure("grid", html.ToString(), string.Empty);
    }

    private static string Figure(string kind, string inner, string attributes) =>
        $"<figure class=\"scoring-explainer scoring-{kind}\" data-scoring=\"{kind}\" data-step=\"final\" aria-hidden=\"true\"{attributes}><div class=\"scoring-plate\">{inner}</div></figure>";
}
