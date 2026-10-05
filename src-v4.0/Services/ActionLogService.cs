using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Configurator;

public sealed class ActionLogEntry
{
    public DateTime Time { get; init; } = DateTime.Now;
    public string Description { get; init; } = "";
    public Func<Task> Undo { get; init; }
    public override string ToString() => $"{Time:HH:mm:ss} · {Description}";
}

public sealed class ActionLogService
{
    private readonly LinkedList<ActionLogEntry> _entries = new();
    private readonly int _capacity;
    private ActionLogEntry _undoEntry;
    public event EventHandler Changed;

    public ActionLogService(int capacity = 100) => _capacity = Math.Max(10, capacity);
    public IReadOnlyList<ActionLogEntry> Entries => _entries.ToList();
    public string LastDescription => _entries.First?.Value.Description ?? "Готово";
    public bool CanUndo => _undoEntry?.Undo != null;

    public void Record(string description, Func<Task> undo = null)
    {
        var entry = new ActionLogEntry { Description = description, Undo = undo };
        _entries.AddFirst(entry);
        while (_entries.Count > _capacity) _entries.RemoveLast();
        if (undo != null)
            _undoEntry = entry;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Info(string description) => Record(description, null);

    public async Task<string> UndoAsync()
    {
        if (_undoEntry?.Undo == null) return "Нет действия для отмены";
        var entry = _undoEntry;
        await entry.Undo();
        _undoEntry = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return entry.Description;
    }
}
