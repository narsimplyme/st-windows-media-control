namespace STMediaBridge;

// Small bounded local log; HTTP headers, tokens and track metadata are never logged.
public sealed class FileLog(string directory) : ILoggerProvider
{
    private readonly object gate = new();
    public ILogger CreateLogger(string categoryName) => new Writer(this, categoryName);
    public void Dispose() { }
    private void Write(string line)
    {
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "agent.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                File.Move(path, path + ".1", true);
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
    private sealed class Writer(FileLog owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information &&
            (!category.StartsWith("Microsoft.AspNetCore") || level >= LogLevel.Warning);
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            try { owner.Write($"{DateTimeOffset.UtcNow:O} {level} {category}: {formatter(state, exception)}"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
