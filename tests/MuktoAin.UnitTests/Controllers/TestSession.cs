using Microsoft.AspNetCore.Http;

namespace MuktoAin.UnitTests.Controllers;

// Minimal ISession stub for controller fixtures whose actions read the
// ASP.NET session (e.g. ChatController.SessionKey). DefaultHttpContext has no
// session feature by default, which throws "Session has not been configured
// for this application or request" the moment Session.GetString is touched.
// Only the string get/set surface is implemented — that is all the
// controllers under test use.
public sealed class TestSession : ISession
{
    private readonly Dictionary<string, byte[]> _values = new();

    public string Id { get; } = Guid.NewGuid().ToString();
    public bool IsAvailable => true;
    public IEnumerable<string> Keys => _values.Keys;

    public void Clear() => _values.Clear();

    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Remove(string key) => _values.Remove(key);

    public void Set(string key, byte[] value) => _values[key] = value;

    public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
}
