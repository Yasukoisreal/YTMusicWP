using System;
using System.Diagnostics;

namespace YTMusicWP.Services.ListenTogether
{
    /// <summary>
    /// Maps the server's wall clock onto this device's monotonic clock from ping/pong round trips.
    /// Ported from SimpMusic / Metrolist ServerClock algorithm.
    /// </summary>
    public class ServerClock
    {
        private readonly object _lock = new object();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        private double? _serverOffsetMs;
        private long _bestRoundTripMs = long.MaxValue;

        private const long MaxSampleAgeMs = 60000L;
        private const long GoodSampleMarginMs = 50L;

        /// <summary>
        /// Monotonic elapsed milliseconds on this device.
        /// </summary>
        public long ElapsedRealtime => _stopwatch.ElapsedMilliseconds;

        public void Reset()
        {
            lock (_lock)
            {
                _serverOffsetMs = null;
                _bestRoundTripMs = long.MaxValue;
            }
        }

        /// <summary>
        /// Records one pong sample and refines the clock offset estimate.
        /// Returns true only on the FIRST accepted sample, signifying the clock is now ready.
        /// </summary>
        public bool RecordPong(long clientTime, long serverReceiveTime, long serverSendTime)
        {
            lock (_lock)
            {
                long receivedAt = ElapsedRealtime;
                if (clientTime <= 0 || clientTime > receivedAt || (receivedAt - clientTime) > MaxSampleAgeMs)
                {
                    return false;
                }
                if (serverReceiveTime <= 0 || serverSendTime < serverReceiveTime)
                {
                    return false;
                }

                long roundTrip = receivedAt - clientTime;
                long serverProcessing = serverSendTime - serverReceiveTime;
                long networkRoundTrip = Math.Max(0L, roundTrip - serverProcessing);
                double sampleOffset = serverSendTime + (networkRoundTrip / 2.0) - receivedAt;
                double? previousOffset = _serverOffsetMs;

                if (networkRoundTrip < _bestRoundTripMs)
                {
                    _bestRoundTripMs = networkRoundTrip;
                }

                double weight = (networkRoundTrip <= _bestRoundTripMs + GoodSampleMarginMs) ? 0.25 : 0.05;
                _serverOffsetMs = previousOffset.HasValue
                    ? previousOffset.Value + weight * (sampleOffset - previousOffset.Value)
                    : sampleOffset;

                return !previousOffset.HasValue;
            }
        }

        /// <summary>
        /// Current server wall time in milliseconds, or null if uncalibrated.
        /// </summary>
        public long? Now()
        {
            lock (_lock)
            {
                if (!_serverOffsetMs.HasValue) return null;
                return (long)(ElapsedRealtime + _serverOffsetMs.Value);
            }
        }

        /// <summary>
        /// Corrects a playback position for transit delay while playing.
        /// </summary>
        public long PositionAt(long position, long effectiveAtServerTime, bool isPlaying)
        {
            if (!isPlaying || effectiveAtServerTime <= 0)
            {
                return position;
            }
            long? serverNow = Now();
            if (!serverNow.HasValue)
            {
                return position;
            }
            return position + Math.Max(0L, serverNow.Value - effectiveAtServerTime);
        }
    }
}
