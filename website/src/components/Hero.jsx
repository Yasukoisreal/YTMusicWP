import React, { useState, useRef } from 'react';
import { Download, ShoppingBag, Play, Pause, Disc, ArrowUpRight, Cpu, Radio, ShieldCheck } from 'lucide-react';
import GithubIcon from './GithubIcon';

export default function Hero() {
  const [isPlaying, setIsPlaying] = useState(true);
  const [mousePos, setMousePos] = useState({ x: 0, y: 0 });
  const cardRef = useRef(null);

  const handleMouseMove = (e) => {
    if (!cardRef.current) return;
    const rect = cardRef.current.getBoundingClientRect();
    const x = (e.clientX - rect.left) / rect.width - 0.5;
    const y = (e.clientY - rect.top) / rect.height - 0.5;
    setMousePos({ x, y });
  };

  const handleMouseLeave = () => {
    setMousePos({ x: 0, y: 0 });
  };

  return (
    <section className="relative min-h-screen pt-28 pb-20 flex flex-col justify-center overflow-hidden border-b border-white/[0.08]">
      {/* Ambient background glows */}
      <div className="absolute top-1/4 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[600px] bg-gradient-to-tr from-[#0078D7]/20 via-[#00F0FF]/15 to-[#FF0033]/15 rounded-full blur-[120px] pointer-events-none -z-10 animate-pulse-glow" />
      <div className="absolute top-10 right-10 w-96 h-96 bg-[#00F0FF]/10 rounded-full blur-[100px] pointer-events-none -z-10" />

      {/* Blueprint Grid Lines & Corner Crosshairs */}
      <div className="absolute inset-0 pointer-events-none -z-10 bg-[linear-gradient(to_right,#ffffff05_1px,transparent_1px),linear-gradient(to_bottom,#ffffff05_1px,transparent_1px)] bg-[size:4rem_4rem]" />

      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 w-full">
        {/* Top Blueprint Status Bar */}
        <div className="flex flex-wrap items-center justify-between gap-4 py-3 border-y border-white/[0.08] font-mono text-[11px] text-slate-400 mb-10 tracking-widest uppercase">
          <div className="flex items-center gap-2">
            <span className="inline-block w-2 h-2 rounded-full bg-[#1DB954] animate-pulse" />
            <span>[PROJECT_CODE: YTMUSICWP]</span>
            <span className="text-white/20">/</span>
            <span className="text-slate-300">WPA81 &bull; W10M</span>
          </div>
          <div className="flex items-center gap-6">
            <span className="hidden sm:inline-block text-[#00F0FF]">SYSTEM: 512MB RAM OPTIMIZED</span>
            <span className="text-white/20 hidden sm:inline-block">/</span>
            <span>BUILD: v2.3.0 PROD</span>
          </div>
        </div>

        {/* Hero Editorial Grid */}
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-12 lg:gap-8 items-center">
          
          {/* Left Column: Extreme Typography & Manifesto */}
          <div className="lg:col-span-7 flex flex-col justify-center">
            <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-white/[0.04] border border-white/10 text-xs font-mono text-[#00F0FF] mb-6 w-fit backdrop-blur-md">
              <Cpu className="w-3.5 h-3.5" />
              <span>THE SOUND REVOLUTION FOR LEGACY LUMIA</span>
            </div>

            <h1 className="font-display font-extrabold text-5xl sm:text-6xl md:text-7xl xl:text-8xl tracking-tighter leading-[0.95] text-white mb-6">
              BREATHE <br />
              <span className="text-transparent bg-clip-text bg-gradient-to-r from-white via-[#00F0FF] to-[#0078D7]">
                NEW LIFE
              </span> <br />
              INTO LUMIA.
            </h1>

            <p className="text-lg sm:text-xl text-slate-300 max-w-xl font-light leading-relaxed mb-8">
              A high-precision, native YouTube Music client crafted for Windows Phone 8.1 and Windows 10 Mobile. Direct audio streaming, synchronized lyrics, Apple Music blur aesthetics, and iconic Live Tiles.
            </p>

            {/* Micro Feature Badges */}
            <div className="grid grid-cols-3 gap-3 max-w-lg mb-10 font-mono text-xs text-slate-300">
              <div className="p-3 rounded-lg bg-white/[0.02] border border-white/[0.06] flex flex-col">
                <span className="text-slate-500 text-[10px]">MEMORY POOL</span>
                <span className="font-semibold text-white mt-1">512MB RAM</span>
                <span className="text-[10px] text-[#1DB954]">Zero-Leak Tested</span>
              </div>
              <div className="p-3 rounded-lg bg-white/[0.02] border border-white/[0.06] flex flex-col">
                <span className="text-slate-500 text-[10px]">AUDIO ENGINE</span>
                <span className="font-semibold text-white mt-1">SABR &bull; AAC</span>
                <span className="text-[10px] text-[#00F0FF]">Zero-Gap Live</span>
              </div>
              <div className="p-3 rounded-lg bg-white/[0.02] border border-white/[0.06] flex flex-col">
                <span className="text-slate-500 text-[10px]">TEST SUITE</span>
                <span className="font-semibold text-white mt-1">43 Verified</span>
                <span className="text-[10px] text-emerald-400">CI Passing 100%</span>
              </div>
            </div>

            {/* Action CTA Buttons */}
            <div className="flex flex-wrap items-center gap-4">
              <a
                href="https://github.com/Yasukoisreal/YTMusicWP/releases"
                target="_blank"
                rel="noreferrer"
                className="group relative inline-flex items-center gap-3 px-7 py-4 text-sm font-semibold text-black bg-[#00F0FF] hover:bg-white rounded-full shadow-[0_0_30px_-5px_#00F0FF] hover:shadow-[0_0_40px_0px_#ffffff] transition-all duration-300 active:scale-95"
              >
                <Download className="w-4 h-4 transition-transform group-hover:translate-y-0.5" />
                <span>DOWNLOAD .APPX (v2.3.0)</span>
              </a>

              <a
                href="https://store.live.net.co/app/447"
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center gap-2.5 px-6 py-4 text-sm font-semibold text-white bg-white/[0.05] hover:bg-white/[0.1] border border-white/10 hover:border-white/20 rounded-full transition-all duration-300 backdrop-blur-md active:scale-95"
              >
                <ShoppingBag className="w-4 h-4 text-amber-300" />
                <span>GET ON LIVE STORE</span>
                <ArrowUpRight className="w-3.5 h-3.5 text-slate-400" />
              </a>

              <a
                href="https://github.com/Yasukoisreal/YTMusicWP"
                target="_blank"
                rel="noreferrer"
                className="p-4 text-slate-400 hover:text-white bg-white/[0.03] hover:bg-white/[0.08] border border-white/[0.08] rounded-full transition-all"
                title="View Source on GitHub"
              >
                <GithubIcon className="w-5 h-5" />
              </a>
            </div>
          </div>

          {/* Right Column: 3D Tactile Lumia Mockup + Holographic Spinning Vinyl Record */}
          <div className="lg:col-span-5 flex justify-center items-center relative">
            <div 
              ref={cardRef}
              onMouseMove={handleMouseMove}
              onMouseLeave={handleMouseLeave}
              className="relative w-full max-w-[420px] aspect-[9/16] max-h-[640px] flex items-center justify-center transition-transform duration-200 ease-out"
              style={{
                perspective: '1200px',
              }}
            >
              {/* Spinning Holographic Vinyl Disc peaking behind phone */}
              <div 
                className={`absolute -right-12 sm:-right-20 top-16 w-64 h-64 sm:w-72 sm:h-72 rounded-full vinyl-grooves p-3 border-2 border-white/10 shadow-2xl transition-transform duration-700 cursor-pointer ${isPlaying ? 'animate-spin-slow' : ''}`}
                style={{
                  transform: `translate3d(${mousePos.x * 25}px, ${mousePos.y * 25}px, -40px)`,
                  boxShadow: '0 25px 60px -15px rgba(0,0,0,0.9), 0 0 40px -10px rgba(0,240,255,0.3)',
                }}
                onClick={() => setIsPlaying(!isPlaying)}
                title="Click to toggle vinyl rotation"
              >
                {/* Center Record Label */}
                <div className="w-full h-full rounded-full flex items-center justify-center relative">
                  <div className="w-24 h-24 rounded-full bg-gradient-to-tr from-[#00F0FF] via-[#0078D7] to-[#FF0033] p-1 shadow-inner flex items-center justify-center">
                    <div className="w-full h-full rounded-full bg-[#06060A] flex flex-col items-center justify-center text-center p-1">
                      <span className="font-display text-[9px] font-bold text-white tracking-widest">YTMUSIC</span>
                      <span className="font-mono text-[7px] text-[#00F0FF]">512MB RAM</span>
                      <div className="w-3 h-3 rounded-full bg-[#06060A] border border-white/40 mt-0.5" />
                    </div>
                  </div>
                </div>
              </div>

              {/* Lumia 3D Device Frame with Real App Screenshot */}
              <div 
                className="relative z-10 w-full h-full max-w-[340px] max-h-[580px] bg-[#121216] rounded-[36px] p-3 border border-white/20 shadow-[0_30px_90px_-20px_rgba(0,0,0,0.95)] overflow-hidden transition-transform duration-150 ease-out"
                style={{
                  transform: `rotateY(${mousePos.x * 24}deg) rotateX(${-mousePos.y * 24}deg)`,
                  transformStyle: 'preserve-3d',
                }}
              >
                {/* Dynamic Glass Glare following cursor */}
                <div 
                  className="absolute inset-0 pointer-events-none rounded-[36px] z-30 transition-opacity duration-300 opacity-30"
                  style={{
                    background: `radial-gradient(circle at ${(mousePos.x + 0.5) * 100}% ${(mousePos.y + 0.5) * 100}%, rgba(255,255,255,0.4) 0%, transparent 60%)`,
                  }}
                />

                {/* Inner Screen */}
                <div className="relative w-full h-full rounded-[28px] overflow-hidden bg-black flex flex-col">
                  {/* Real screenshot from Lumia device */}
                  <img 
                    src="./Pictures/02.png" 
                    alt="YTMusicWP Apple Music Mode on Lumia" 
                    className="w-full h-full object-cover object-top select-none pointer-events-none"
                  />

                  {/* Tactile Playback Pill Overlay */}
                  <div className="absolute bottom-4 left-4 right-4 p-3 rounded-2xl glass-panel-glow flex items-center justify-between z-20">
                    <div className="flex items-center gap-3 overflow-hidden">
                      <div className="w-10 h-10 rounded-lg bg-cover bg-center shrink-0 border border-white/20 relative" style={{ backgroundImage: "url('./Pictures/01.png')" }}>
                        <span className="absolute -top-1 -right-1 w-2.5 h-2.5 rounded-full bg-[#1DB954] border border-black" />
                      </div>
                      <div className="flex flex-col truncate">
                        <span className="text-xs font-semibold text-white truncate">Playing on Lumia</span>
                        <span className="text-[10px] text-slate-400 font-mono">Apple Music UI &bull; Blur</span>
                      </div>
                    </div>
                    <button 
                      onClick={() => setIsPlaying(!isPlaying)}
                      className="w-8 h-8 rounded-full bg-white text-black flex items-center justify-center shrink-0 hover:scale-105 active:scale-95 transition-transform"
                    >
                      {isPlaying ? <Pause className="w-4 h-4 fill-black" /> : <Play className="w-4 h-4 fill-black ml-0.5" />}
                    </button>
                  </div>
                </div>
              </div>

              {/* Holographic Reflection Glow underneath */}
              <div 
                className="absolute -bottom-6 w-3/4 h-8 bg-[#00F0FF]/30 blur-2xl rounded-full pointer-events-none"
                style={{
                  transform: `translateX(${mousePos.x * 20}px)`,
                }}
              />
            </div>
          </div>

        </div>
      </div>
    </section>
  );
}
