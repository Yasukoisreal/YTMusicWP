import React, { useState } from 'react';
import { Sliders, Sparkles, Zap, Layers, Eye } from 'lucide-react';

export default function InteractivePlayerPreview() {
  const [activeMode, setActiveMode] = useState('apple'); // 'apple' or 'spotify'

  return (
    <section className="py-24 border-b border-white/[0.08] relative overflow-hidden bg-[#07070D]">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        
        {/* Section Header */}
        <div className="flex flex-col md:flex-row md:items-end justify-between mb-16 gap-6">
          <div>
            <div className="inline-flex items-center gap-2 font-mono text-xs text-[#00F0FF] tracking-widest uppercase mb-3">
              <Sliders className="w-3.5 h-3.5" />
              <span>[01_VISUAL_ARCHITECTURE]</span>
            </div>
            <h2 className="font-display font-extrabold text-4xl sm:text-5xl lg:text-6xl text-white tracking-tight">
              TWO ICONIC WORLDS. <br />
              <span className="text-slate-400 font-light">ZERO COMPROMISES.</span>
            </h2>
          </div>
          <p className="text-slate-400 font-light max-w-md text-sm sm:text-base leading-relaxed">
            Switch effortlessly between modern Apple Music frosted elegance and classic Spotify AMOLED dark mode — engineered with native Lumia hardware acceleration.
          </p>
        </div>

        {/* Interactive Mode Toggle */}
        <div className="flex justify-center mb-12">
          <div className="p-1.5 rounded-full bg-white/[0.04] border border-white/10 flex items-center gap-2 backdrop-blur-xl">
            <button
              onClick={() => setActiveMode('apple')}
              className={`flex items-center gap-2 px-6 py-2.5 rounded-full text-xs font-mono font-semibold transition-all duration-300 ${
                activeMode === 'apple'
                  ? 'bg-gradient-to-r from-[#00F0FF] to-[#0078D7] text-black shadow-lg shadow-[#00F0FF]/30'
                  : 'text-slate-400 hover:text-white'
              }`}
            >
              <Sparkles className="w-3.5 h-3.5" />
              <span>APPLE MUSIC MODE</span>
            </button>
            <button
              onClick={() => setActiveMode('spotify')}
              className={`flex items-center gap-2 px-6 py-2.5 rounded-full text-xs font-mono font-semibold transition-all duration-300 ${
                activeMode === 'spotify'
                  ? 'bg-[#1DB954] text-black shadow-lg shadow-[#1DB954]/30'
                  : 'text-slate-400 hover:text-white'
              }`}
            >
              <Zap className="w-3.5 h-3.5" />
              <span>SPOTIFY DARK MODE</span>
            </button>
          </div>
        </div>

        {/* Interactive Comparison Stage */}
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-center">
          
          {/* Left: Device Viewport */}
          <div className="lg:col-span-6 flex justify-center">
            <div className="relative w-full max-w-[340px] aspect-[9/16] bg-[#0E0E14] rounded-[36px] p-3 border border-white/15 shadow-[0_25px_80px_-15px_rgba(0,0,0,0.9)] overflow-hidden transition-all duration-500">
              
              {/* Dynamic screen image transition */}
              <div className="relative w-full h-full rounded-[28px] overflow-hidden bg-black">
                <img
                  src={activeMode === 'apple' ? './Pictures/11.png' : './Pictures/08.png'}
                  alt={activeMode === 'apple' ? 'Apple Music blurred backdrop' : 'Spotify classic dark player'}
                  className="w-full h-full object-cover transition-opacity duration-500"
                />

                {/* Badge Overlay */}
                <div className="absolute top-4 right-4 px-3 py-1 rounded-full bg-black/60 backdrop-blur-md border border-white/15 text-[10px] font-mono text-white flex items-center gap-1.5">
                  <span className={`w-1.5 h-1.5 rounded-full ${activeMode === 'apple' ? 'bg-[#00F0FF]' : 'bg-[#1DB954]'}`} />
                  <span>{activeMode === 'apple' ? 'LUMIA IMAGING SDK' : 'AMOLED BLACK'}</span>
                </div>
              </div>
            </div>
          </div>

          {/* Right: Technical Specs & Engineering Details */}
          <div className="lg:col-span-6 flex flex-col justify-center gap-6">
            <div className="p-6 rounded-2xl glass-panel border border-white/10 flex flex-col gap-4">
              <div className="flex items-center justify-between">
                <span className="font-mono text-xs text-[#00F0FF] uppercase tracking-wider">
                  {activeMode === 'apple' ? 'ENGINE: LUMIA BLUR SHADER' : 'ENGINE: DIRECT XAML RENDER'}
                </span>
                <span className="font-mono text-[11px] text-slate-500">[01/02]</span>
              </div>
              
              <h3 className="font-display font-bold text-2xl text-white">
                {activeMode === 'apple' 
                  ? 'Hardware-Accelerated Backdrop Blur' 
                  : 'Pitch-Black AMOLED Power Saver'}
              </h3>

              <p className="text-sm text-slate-300 font-light leading-relaxed">
                {activeMode === 'apple'
                  ? 'Powered by Lumia Imaging SDK 2.0. The player renders a 120x200 downsampled artwork canvas with an 80-pixel Gaussian blur kernel, generating an ethereal Apple Music backdrop in under 16ms without dropping frames on 512MB RAM devices.'
                  : 'Pure #000000 black background tailored for Lumia ClearBlack AMOLED displays (like Lumia 730, 820, 925, 930). Zero battery drain on dark pixels, accompanied by Spotify signature green playback telemetry.'}
              </p>

              {/* Technical Metrics Table */}
              <div className="grid grid-cols-3 gap-3 pt-4 border-t border-white/10 font-mono text-xs">
                <div>
                  <span className="text-slate-500 text-[10px] block">FRAME BUDGET</span>
                  <span className="text-white font-semibold">{activeMode === 'apple' ? '16.6ms (60 FPS)' : '4.2ms (Zero Load)'}</span>
                </div>
                <div>
                  <span className="text-slate-500 text-[10px] block">RAM OVERHEAD</span>
                  <span className="text-emerald-400 font-semibold">&lt; 1.2 MB</span>
                </div>
                <div>
                  <span className="text-slate-500 text-[10px] block">SHADER ACCEL</span>
                  <span className="text-[#00F0FF] font-semibold">{activeMode === 'apple' ? 'GPU Kernel 80' : 'Direct UI'}</span>
                </div>
              </div>
            </div>

            {/* Lyrics Feature Callout */}
            <div className="p-6 rounded-2xl bg-white/[0.02] border border-white/[0.06] flex items-center justify-between gap-4">
              <div className="flex items-center gap-4">
                <div className="w-12 h-12 rounded-xl bg-white/[0.04] border border-white/10 flex items-center justify-center shrink-0">
                  <Layers className="w-6 h-6 text-[#00F0FF]" />
                </div>
                <div>
                  <h4 className="font-semibold text-white text-sm">Synced Karaoke Lyrics</h4>
                  <p className="text-xs text-slate-400 font-light">With progressive opacity falloff & live Mini Lyric marquee.</p>
                </div>
              </div>
              <a href="#features" className="text-xs font-mono text-[#00F0FF] hover:underline shrink-0">
                VIEW DETAILS &rarr;
              </a>
            </div>

          </div>

        </div>

      </div>
    </section>
  );
}
