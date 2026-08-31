namespace AirMic.Core.Audio;

public sealed class AdaptiveJitterBuffer
{
    private readonly SortedDictionary<uint, AudioFrame> _frames = [];
    private readonly object _gate = new();
    private uint? _expected;
    private bool _started;
    private int _targetPackets;
    private int _latePackets;
    private int _totalPackets;

    public AdaptiveJitterBuffer(int targetPackets = 3)
    {
        _targetPackets = Math.Clamp(targetPackets, 2, 10);
    }

    public int BufferedPackets { get { lock (_gate) return _frames.Count; } }
    public int TargetPackets { get { lock (_gate) return _targetPackets; } }

    public void Push(AudioFrame frame)
    {
        lock (_gate)
        {
            _totalPackets++;
            if (_started && _expected is not null && frame.Header.Sequence < _expected)
            {
                _latePackets++;
                TuneUnsafe();
                return;
            }
            _frames.TryAdd(frame.Header.Sequence, frame);
            _expected = _expected is null || (!_started && frame.Header.Sequence < _expected) ? frame.Header.Sequence : _expected;
            TuneUnsafe();
        }
    }

    public bool TryRead(out AudioFrame? frame, out bool packetMissing)
    {
        lock (_gate)
        {
            frame = null;
            packetMissing = false;
            if (_expected is null || _frames.Count < _targetPackets) return false;
            _started = true;
            var sequence = _expected.Value;
            if (_frames.Remove(sequence, out frame))
            {
                _expected = sequence + 1;
                return true;
            }

            var first = _frames.Keys.First();
            if (first > sequence)
            {
                packetMissing = true;
                _expected = sequence + 1;
                return true;
            }
            return false;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _frames.Clear();
            _expected = null;
            _started = false;
            _latePackets = 0;
            _totalPackets = 0;
            _targetPackets = 3;
        }
    }

    private void TuneUnsafe()
    {
        if (_totalPackets < 100) return;
        var lateRate = (double)_latePackets / _totalPackets;
        if (lateRate > 0.02) _targetPackets = Math.Min(10, _targetPackets + 1);
        else if (lateRate < 0.002) _targetPackets = Math.Max(2, _targetPackets - 1);
        _latePackets = 0;
        _totalPackets = 0;
    }
}
