import React from 'react';
import { Cpu, Radio, AlignLeft, Users, DownloadCloud, Grid, Sparkles } from 'lucide-react';

const features = [
  {
    id: '01',
    icon: Cpu,
    tag: '512MB HARDWARE ENGINE',
    title: 'Engineered for 512MB RAM',
    desc: 'Aggressive memory reclamation, pooled BitmapImage decoders, and bounded IPC payloads allow full YouTube Music streaming on entry-level devices like Nokia Lumia 520.',
    meta: 'RAM USAGE: < 45MB PEAK',
    accent: '#1DB954'
  },
  {
    id: '02',
    icon: Radio,
    tag: 'SABR STREAMING',
    title: 'Continuous YouTube Live',
    desc: 'Proprietary zero-gap rolling audio buffer built on Google Server-side Adaptive Bitrate (SABR) and protobuf UMP parsing for seamless, stutter-free livestreams.',
    meta: 'BUFFER: 3000ms LATENCY',
    accent: '#FF0033'
  },
  {
    id: '03',
    icon: AlignLeft,
    tag: 'REAL-TIME TELEMETRY',
    title: 'Synchronized Scrolling Lyrics',
    desc: 'Real-time word-by-word and line-by-line synced lyrics with progressive opacity falloff, defocus blur, and multi-source redundancy (YouTube, LRCLIB, TTML).',
    meta: 'ACCURACY: ± 50ms',
    accent: '#00F0FF'
  },
  {
    id: '04',
    icon: Users,
    tag: 'NETWORK SYNC',
    title: 'Listen Together Rooms',
    desc: 'Join or host synchronized listening rooms with Metrolist and SimpMusic users. Shared queue, host playback broadcast, and guest seek synchronization.',
    meta: 'PROTOCOL: WEBSOCKET SYNC',
    accent: '#A855F7'
  },
  {
    id: '05',
    icon: DownloadCloud,
    tag: 'OFFLINE TAGGING',
    title: 'Smart Offline Downloads',
    desc: 'Download high-bitrate audio directly to your Lumia. Built-in M4A atom metadata injection embeds titles, artists, and HD cover artwork natively for offline music players.',
    meta: 'FORMAT: M4A &bull; 128/256KBPS',
    accent: '#F59E0B'
  },
  {
    id: '06',
    icon: Grid,
    tag: 'METRO DESIGN',
    title: 'Iconic Metro Live Tiles',
    desc: 'Bring your Start Screen alive. Now Playing Flip Tiles flip in real time, accompanied by People-Hub style artist mosaic tiles and pinned quick-launch playlists.',
    meta: 'TEMPLATES: FLIP + MOSAIC',
    accent: '#0078D7'
  }
];

export default function FeatureShowcase() {
  return (
    <section id="features" className="py-28 border-b border-white/[0.08] relative">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        
        {/* Section Heading */}
        <div className="flex flex-col md:flex-row md:items-end justify-between mb-16 gap-6">
          <div>
            <div className="inline-flex items-center gap-2 font-mono text-xs text-[#00F0FF] tracking-widest uppercase mb-3">
              <Sparkles className="w-3.5 h-3.5" />
              <span>[02_CAPABILITIES]</span>
            </div>
            <h2 className="font-display font-extrabold text-4xl sm:text-5xl lg:text-6xl text-white tracking-tight">
              DEEP ENGINEERING. <br />
              <span className="text-slate-400 font-light">EXQUISITE CRAFT.</span>
            </h2>
          </div>
          <div className="font-mono text-xs text-slate-500 max-w-xs">
            Every subsystem was designed from scratch to overcome hardware constraints of the Windows Phone 8.1 architecture.
          </div>
        </div>

        {/* Editorial 1px Grid Cards */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {features.map((feat) => {
            const Icon = feat.icon;
            return (
              <div
                key={feat.id}
                className="group relative p-8 rounded-2xl glass-panel hover:bg-white/[0.06] transition-all duration-300 border border-white/[0.08] hover:border-white/20 flex flex-col justify-between"
              >
                {/* Top index and icon */}
                <div>
                  <div className="flex items-center justify-between mb-6">
                    <span className="font-mono text-xs text-slate-500 group-hover:text-white transition-colors">
                      [{feat.id}]
                    </span>
                    <div className="w-10 h-10 rounded-xl bg-white/[0.04] border border-white/10 flex items-center justify-center transition-transform group-hover:scale-110">
                      <Icon className="w-5 h-5 text-white" style={{ color: feat.accent }} />
                    </div>
                  </div>

                  <span className="inline-block font-mono text-[10px] tracking-widest text-[#00F0FF] mb-2 uppercase">
                    {feat.tag}
                  </span>

                  <h3 className="font-display font-bold text-xl text-white mb-3 group-hover:text-[#00F0FF] transition-colors">
                    {feat.title}
                  </h3>

                  <p className="text-slate-400 text-sm font-light leading-relaxed">
                    {feat.desc}
                  </p>
                </div>

                {/* Bottom technical metadata hairline */}
                <div className="mt-8 pt-4 border-t border-white/[0.06] flex items-center justify-between font-mono text-[11px] text-slate-500">
                  <span>{feat.meta}</span>
                  <span className="text-white/20 group-hover:text-white transition-colors">&rarr;</span>
                </div>
              </div>
            );
          })}
        </div>

      </div>
    </section>
  );
}
