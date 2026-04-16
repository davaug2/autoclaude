namespace AutoClaude.Core.Domain.Models;

public class SessionMemory
{
    public List<MemoryEntry> Persistent { get; set; } = new();
    public List<MemoryEntry> Temporary { get; set; } = new();

    public void AddPersistent(string question, string answer)
    {
        Persistent.Add(new MemoryEntry { Question = question, Answer = answer });
    }

    public void AddTemporary(string question, string answer)
    {
        Temporary.Add(new MemoryEntry { Question = question, Answer = answer });
    }

    public void Add(string question, string answer, bool persistent)
    {
        if (persistent)
            AddPersistent(question, answer);
        else
            AddTemporary(question, answer);
    }

    public void ClearTemporary()
    {
        Temporary.Clear();
    }

    public string ToPromptText()
    {
        var all = new List<string>();

        if (Persistent.Count > 0)
        {
            all.Add("\n\nMemoria persistente da sessao (valida para toda a sessao):");
            foreach (var e in Persistent)
                all.Add($"  P: {e.Question}\n  R: {e.Answer}");
        }

        if (Temporary.Count > 0)
        {
            all.Add("\n\nMemoria temporaria da fase atual:");
            foreach (var e in Temporary)
                all.Add($"  P: {e.Question}\n  R: {e.Answer}");
        }

        return all.Count > 0 ? string.Join("\n", all) : "";
    }
}

public class MemoryEntry
{
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
}
