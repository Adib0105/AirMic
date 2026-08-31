namespace AirMic.Core.Security;

public sealed class SequenceWindow
{
    private const int Window = 64;
    private uint _highest;
    private ulong _seen;
    private bool _initialized;

    public bool TryAccept(uint sequence)
    {
        if (!_initialized)
        {
            _initialized = true;
            _highest = sequence;
            _seen = 1;
            return true;
        }

        if (sequence > _highest)
        {
            var shift = sequence - _highest;
            _seen = shift >= Window ? 1UL : (_seen << (int)shift) | 1UL;
            _highest = sequence;
            return true;
        }

        var age = _highest - sequence;
        if (age >= Window) return false;
        var mask = 1UL << (int)age;
        if ((_seen & mask) != 0) return false;
        _seen |= mask;
        return true;
    }
}
