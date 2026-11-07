using System.Collections.Concurrent;
using LefteriaFc.Application.ClubCalendar;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Identity.IntegrationTests;

// Test-only gate: the service has completed its profile preflight, but the
// repository's final authorization statement has not begun.
internal sealed class CalendarQueryGate
{
    private readonly ConcurrentQueue<Gate> gates=new();
    public Gate HoldNext(){var gate=new Gate();gates.Enqueue(gate);return gate;}
    public async Task WaitAsync(CancellationToken token)
    {
        if(!gates.TryDequeue(out var gate))return;
        gate.Entered.TrySetResult();await gate.Release.Task.WaitAsync(token);
    }
    internal sealed class Gate
    {
        public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

internal sealed class GatedCalendarRepository(IMobileCalendarRepository inner,CalendarQueryGate gate):IMobileCalendarRepository
{
    public async Task<CalendarMonthSnapshot> GetMonthAsync(MobileSession session,DateOnly month,string? playerId,int offset,CancellationToken token)
    {await gate.WaitAsync(token);return await inner.GetMonthAsync(session,month,playerId,offset,token);}
    public async Task<CalendarMonthSnapshot> GetDelegatedMonthAsync(
        MobileSession session,DateOnly month,int offset,CancellationToken token)
    {await gate.WaitAsync(token);return await inner.GetDelegatedMonthAsync(session,month,offset,token);}
    public async Task<CalendarWriteSnapshot> SubmitAvailabilityAsync(MobileSession session,MobileAvailabilityCommand command,CancellationToken token)
    {await gate.WaitAsync(token);return await inner.SubmitAvailabilityAsync(session,command,token);}
    public async Task<CalendarWriteSnapshot> SubmitDelegatedAvailabilityAsync(
        MobileSession session,MobileAvailabilityCommand command,CancellationToken token)
    {await gate.WaitAsync(token);return await inner.SubmitDelegatedAvailabilityAsync(session,command,token);}
    public async Task<CalendarAvailabilitySnapshot> GetOwnAvailabilityAsync(MobileSession session,CancellationToken token)
    {await gate.WaitAsync(token);return await inner.GetOwnAvailabilityAsync(session,token);}
    public async Task<CalendarResponsesSnapshot> GetResponsesAsync(MobileSession session,string eventId,string? kind,int offset,CancellationToken token)
    {await gate.WaitAsync(token);return await inner.GetResponsesAsync(session,eventId,kind,offset,token);}
}
