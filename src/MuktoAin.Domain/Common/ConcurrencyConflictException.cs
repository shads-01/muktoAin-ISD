namespace MuktoAin.Domain.Common;

// AUD-4: thrown by repositories when a RowVersion check fails, i.e. another
// writer changed the row first. Keeps EF's DbUpdateConcurrencyException out of
// the Application layer, which has no EF dependency.
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception? inner = null)
        : base(message, inner) { }
}
