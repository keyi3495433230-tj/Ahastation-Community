namespace CardReview;

public sealed class EbbinghausPageData
{
    public List<EbbinghausWordData> Words { get; set; } = new();
}

public sealed class EbbinghausWordData
{
    public string Word { get; set; } = "";
    public string Meaning { get; set; } = "";
    public bool[] Completed { get; set; } = new bool[11];
    public DateTimeOffset? FirstWordEnteredAt { get; set; }
    public DateTimeOffset? LastWordModifiedAt { get; set; }
    public DateTimeOffset?[] CompletedAt { get; set; } = new DateTimeOffset?[11];
}
