using System;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Storage;

namespace AudioPlayerTask
{
    /// <summary>
    /// Records what was actually listened to, for the listening stats and Last.fm: one line per play appended to
    /// <see cref="FileName"/> in LocalFolder ("yyyy-MM-dd HH:mm:ss \t seconds \t videoId \t title \t artist \t duration",
    /// local time; duration 0 when unknown, e.g. a livestream).
    /// It lives in the audio task because the app is usually suspended while music plays. Only time spent in the Playing
    /// state counts, and plays shorter than <see cref="MinSeconds"/> (skips) are left out.
    /// </summary>
    internal sealed class ListeningLog
    {
        public const string FileName = "listening_history.txt";
        private const double MinSeconds = 30;

        private readonly object _lock = new object();
        private Task _writes = Task.FromResult(0);
        private string _videoId;
        private string _title;
        private string _artist;
        private DateTime _startedAt;
        private double _seconds;
        private double _duration;
        private DateTime? _playingSince;

        /// <summary>
        /// A track became current. The same track again without <see cref="TrackEnded"/> (a metadata refresh, a retry)
        /// continues the play.
        /// <paramref name="duration"/> is the player's, only trusted while it is playing (it may still hold the old track).
        /// </summary>
        public void TrackStarted(string videoId, string title, string artist, bool playing, double duration)
        {
            if (string.IsNullOrEmpty(videoId)) return;
            lock (_lock)
            {
                if (videoId == _videoId) return;
                WriteCurrentLocked();
                _videoId = videoId;
                _title = title;
                _artist = artist;
                _startedAt = DateTime.Now;
                _seconds = 0;
                _duration = playing ? duration : 0;
                _playingSince = playing ? (DateTime?)DateTime.Now : null;
            }
        }

        public void PlaybackChanged(bool playing, double duration)
        {
            lock (_lock)
            {
                if (playing)
                {
                    if (_playingSince == null) _playingSince = DateTime.Now;
                    if (_duration <= 0 && duration > 0) _duration = duration;
                }
                else if (_playingSince != null)
                {
                    _seconds += (DateTime.Now - _playingSince.Value).TotalSeconds;
                    _playingSince = null;
                }
            }
        }

        /// <summary>The track played to its end: the play is written, and the same track again (repeat one) is a new play.</summary>
        public void TrackEnded()
        {
            lock (_lock)
            {
                WriteCurrentLocked();
                _videoId = null;
            }
        }

        /// <summary>Writes the current play (the task is about to end) and waits for the file writes.</summary>
        public Task FlushAsync()
        {
            lock (_lock)
            {
                WriteCurrentLocked();
                _videoId = null;
                return _writes;
            }
        }

        private void WriteCurrentLocked()
        {
            if (_videoId == null) return;
            double seconds = _seconds;
            if (_playingSince != null) seconds += (DateTime.Now - _playingSince.Value).TotalSeconds;
            if (seconds < MinSeconds) return;

            string line = _startedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\t"
                + ((int)seconds).ToString(CultureInfo.InvariantCulture) + "\t"
                + Clean(_videoId) + "\t" + Clean(_title) + "\t" + Clean(_artist) + "\t"
                + ((int)Math.Round(_duration)).ToString(CultureInfo.InvariantCulture) + "\r\n";
            // Appends are chained so lines never interleave; a failed write loses one play, never the file
            _writes = _writes.ContinueWith(_ => AppendAsync(line)).Unwrap();
        }

        private static async Task AppendAsync(string line)
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FileName, CreationCollisionOption.OpenIfExists);
                await FileIO.AppendTextAsync(file, line);
            }
            catch { }
        }

        private static string Clean(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
