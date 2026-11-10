namespace CardReview;

public sealed class TextNoteInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "新建 文本文档.txt";
    public Guid? ParentId { get; set; }
    public bool IsExpanded { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore]
    public string Content { get; set; } = "";
}
