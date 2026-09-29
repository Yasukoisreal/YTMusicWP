using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;
using YTMusicWP.Services;

namespace YTMusicWP.Controls
{
    /// <summary>
    /// Full-screen listening stats (minutes, plays, listening clock, top songs and artists) for a period, built from the
    /// play log the audio task writes. The lists are built in code: the stats are plain fields and a handful of rows.
    /// </summary>
    public sealed partial class StatsView : UserControl
    {
        private const int TopCount = 5;
        private static readonly SolidColorBrush Accent = new SolidColorBrush(Color.FromArgb(0xFF, 0x1D, 0xB9, 0x54));
        private static readonly SolidColorBrush Dim = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
        private static readonly SolidColorBrush Bar = new SolidColorBrush(Color.FromArgb(0xFF, 0x2E, 0x2E, 0x2E));

        private List<PlayRecord> _plays = new List<PlayRecord>();
        private string _period = "month";

        /// <summary>The close button was pressed (MainPage hides the page so the back stack stays in one place).</summary>
        public event RoutedEventHandler CloseClick;

        /// <summary>A top song was tapped: MainPage plays it.</summary>
        public event EventHandler<YouTubeTrack> PlayRequested;

        public StatsView()
        {
            this.InitializeComponent();
        }

        public async void Open()
        {
            MotionHelper.ShowPage(this);
            LoadingBar.Visibility = Visibility.Visible;
            _plays = await LoadPlaysAsync();
            LoadingBar.Visibility = Visibility.Collapsed;
            Render();
        }

        public void Close()
        {
            MotionHelper.HidePage(this, () =>
            {
                // Nothing of the stats is needed until the page opens again
                _plays = new List<PlayRecord>();
                TopSongsPanel.Children.Clear();
                TopArtistsPanel.Children.Clear();
                ClockBars.Children.Clear();
            });
        }

        private static async Task<List<PlayRecord>> LoadPlaysAsync()
        {
            // The audio task may be appending right now: one retry covers the brief sharing violation
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var file = await ApplicationData.Current.LocalFolder.GetFileAsync(ListeningStatsCalculator.LogFileName);
                    var lines = await FileIO.ReadLinesAsync(file);
                    return await Task.Run(() => ListeningStatsCalculator.Parse(lines));
                }
                catch (System.IO.FileNotFoundException)
                {
                    return new List<PlayRecord>(); // nothing played long enough yet
                }
                catch
                {
                    if (attempt == 0) await Task.Delay(300);
                }
            }
            return new List<PlayRecord>();
        }

        private void Period_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            _period = button.Tag as string ?? "month";
            Render();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (CloseClick != null) CloseClick(sender, e);
        }

        private void Render()
        {
            foreach (var child in PeriodChips.Children)
            {
                var chip = child as Button;
                if (chip == null) continue;
                bool selected = (chip.Tag as string) == _period;
                chip.Background = new SolidColorBrush(selected ? Colors.White : Color.FromArgb(0xFF, 0x26, 0x26, 0x26));
                chip.Foreground = new SolidColorBrush(selected ? Color.FromArgb(0xFF, 0x0F, 0x0F, 0x0F) : Colors.White);
            }

            var now = DateTime.Now;
            var summary = ListeningStatsCalculator.Summarize(_plays, ListeningStatsCalculator.PeriodStart(_period, now), DateTime.MaxValue, TopCount);

            bool empty = summary.Plays == 0;
            EmptyCard.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            StatsContent.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            if (empty) return;

            MinutesText.Text = Minutes(summary.Seconds).ToString("N0", CultureInfo.CurrentCulture);
            PlaysText.Text = summary.Plays.ToString("N0", CultureInfo.CurrentCulture);
            SongsText.Text = summary.Songs.ToString("N0", CultureInfo.CurrentCulture);
            ArtistsText.Text = summary.Artists.ToString("N0", CultureInfo.CurrentCulture);
            BiggestDayText.Text = summary.BiggestDay.HasValue
                ? "Biggest day: " + summary.BiggestDay.Value.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture) + ", " + Minutes(summary.BiggestDaySeconds) + " min"
                : "";

            RenderClock(summary.SecondsByHour);
            RenderTopSongs(summary.TopSongs);
            RenderTopArtists(summary.TopArtists);
        }

        private void RenderClock(int[] secondsByHour)
        {
            ClockBars.Children.Clear();
            ClockBars.ColumnDefinitions.Clear();
            ClockLabels.Children.Clear();
            ClockLabels.ColumnDefinitions.Clear();

            int max = 0, peak = 0;
            for (int h = 0; h < 24; h++)
            {
                if (secondsByHour[h] > max) { max = secondsByHour[h]; peak = h; }
            }

            for (int h = 0; h < 24; h++)
            {
                ClockBars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                ClockLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                double share = max > 0 ? (double)secondsByHour[h] / max : 0;
                var bar = new Rectangle
                {
                    Height = Math.Max(3, share * ClockBars.Height),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(1.5, 0, 1.5, 0),
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = h == peak && max > 0 ? Accent : Bar
                };
                Grid.SetColumn(bar, h);
                ClockBars.Children.Add(bar);

                if (h % 6 == 0)
                {
                    var label = new TextBlock { Text = h + "h", FontSize = 11, Foreground = Dim };
                    Grid.SetColumn(label, h);
                    Grid.SetColumnSpan(label, 6);
                    ClockLabels.Children.Add(label);
                }
            }

            PeakHourText.Text = max > 0 ? "Most often around " + peak.ToString("00") + ":00" : "";
        }

        private void RenderTopSongs(List<StatsEntry> songs)
        {
            TopSongsPanel.Children.Clear();
            for (int i = 0; i < songs.Count; i++)
            {
                var song = songs[i];
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var rank = new TextBlock { Text = (i + 1).ToString(), FontSize = 15, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(rank);

                var cover = new Rectangle { Width = 48, Height = 48, RadiusX = 4, RadiusY = 4, Fill = Bar };
                if (!song.VideoId.StartsWith("LOCAL:"))
                {
                    // default.jpg is 120x90: small enough to decode five of them on a 512 MB phone
                    var image = new BitmapImage { DecodePixelWidth = 96 };
                    image.UriSource = new Uri("https://i.ytimg.com/vi/" + song.VideoId + "/default.jpg");
                    cover.Fill = new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill };
                }
                Grid.SetColumn(cover, 1);
                row.Children.Add(cover);

                var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = song.Title ?? "", FontSize = 14, Foreground = new SolidColorBrush(Colors.White), TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock
                {
                    Text = (string.IsNullOrEmpty(song.Artist) ? "" : song.Artist + " · ") + Minutes(song.Seconds) + " min · " + song.Plays + (song.Plays == 1 ? " play" : " plays"),
                    FontSize = 12,
                    Foreground = Dim,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                Grid.SetColumn(text, 2);
                row.Children.Add(text);

                var button = new Button
                {
                    Content = row,
                    Style = (Style)Application.Current.Resources["CategoryButtonStyle"],
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Tag = song
                };
                button.Click += TopSong_Click;
                TopSongsPanel.Children.Add(button);
            }
        }

        private void RenderTopArtists(List<StatsEntry> artists)
        {
            TopArtistsPanel.Children.Clear();
            for (int i = 0; i < artists.Count; i++)
            {
                var artist = artists[i];
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                row.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontSize = 15, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center });

                var name = new TextBlock { Text = artist.Artist, FontSize = 14, Foreground = new SolidColorBrush(Colors.White), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(name, 1);
                row.Children.Add(name);

                var minutes = new TextBlock { Text = Minutes(artist.Seconds) + " min", FontSize = 12, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                Grid.SetColumn(minutes, 2);
                row.Children.Add(minutes);

                TopArtistsPanel.Children.Add(row);
            }
        }

        private void TopSong_Click(object sender, RoutedEventArgs e)
        {
            var song = (sender as Button)?.Tag as StatsEntry;
            if (song == null || PlayRequested == null) return;
            PlayRequested(this, new YouTubeTrack
            {
                VideoId = song.VideoId,
                Title = song.Title,
                ChannelName = song.Artist,
                ThumbnailUrl = song.VideoId.StartsWith("LOCAL:") ? null : "https://i.ytimg.com/vi/" + song.VideoId + "/hqdefault.jpg"
            });
        }

        private static int Minutes(int seconds)
        {
            return (int)Math.Round(seconds / 60.0);
        }
    }
}
