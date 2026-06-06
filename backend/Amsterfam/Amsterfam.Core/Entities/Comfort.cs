namespace Amsterfam.Core.Entities;

public class ComfortQuestionTemplate
{
    public int Id { get; set; }
    public required string Label { get; set; }
    public ComfortQuestionType Type { get; set; }
    public List<string> Options { get; set; } = [];
    public bool IsDefault { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }

    public ICollection<EventComfortQuestion> EventQuestions { get; set; } = [];
}

public class EventComfortQuestion
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public int TemplateId { get; set; }
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }

    public Event Event { get; set; } = null!;
    public ComfortQuestionTemplate Template { get; set; } = null!;
    public ICollection<ComfortAnswer> Answers { get; set; } = [];
}

public class ComfortAnswer
{
    public int Id { get; set; }
    public int EventComfortQuestionId { get; set; }
    public int UserId { get; set; }
    public required string AnswerValue { get; set; }

    public EventComfortQuestion Question { get; set; } = null!;
    public User User { get; set; } = null!;
}

public enum ComfortQuestionType
{
    SingleChoice,
    MultiChoice,
    FreeText,
}
