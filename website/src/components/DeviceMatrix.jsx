import React from 'react';
import { Smartphone, CheckCircle2, ShieldAlert, Cpu } from 'lucide-react';

const devices = [
  {
    tier: '512MB RAM LOW-END',
    badge: 'OPTIMIZED',
    badgeColor: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20',
    headline: 'Ultra-Smooth & Lightweight',
    ramLimit: '512 MB',
    models: ['Lumia 520', 'Lumia 530', 'Lumia 620', 'Lumia 625', 'Lumia 630', 'Lumia 635', 'Lumia 720'],
    notes: 'Protected by aggressive BitmapImage pooling and bounded 100-item ValueSet serialization.'
  },
  {
    tier: '1GB+ MID-RANGE & FLAGSHIPS',
    badge: 'RECOMMENDED',
    badgeColor: 'bg-[#00F0FF]/10 text-[#00F0FF] border-[#00F0FF]/20',
    headline: 'Flawless Apple Music Backdrop',
    ramLimit: '1 GB – 2 GB',
    models: ['Lumia 525', 'Lumia 535', 'Lumia 730 / 735', 'Lumia 820', 'Lumia 830', 'Lumia 920 / 925', 'Lumia 930', 'Lumia 1020', 'Lumia 1520', 'Lumia Icon'],
    notes: 'Full support for real-time Lumia Imaging SDK 2.0 blurred backdrop and 60fps spring transitions.'
  },
  {
    tier: 'WINDOWS 10 MOBILE',
    badge: 'COMPATIBLE',
    badgeColor: 'bg-[#0078D7]/10 text-[#0078D7] border-[#0078D7]/20',
    headline: 'Native Sideloading Support',
    ramLimit: '1 GB – 3 GB',
    models: ['Lumia 550', 'Lumia 640 / 640 XL', 'Lumia 650', 'Lumia 950 / 950 XL', 'HP Elite x3', 'Alcatel Idol 4S'],
    notes: 'Install directly via Developer Mode and File Explorer with full Windows 10 Mobile Live Tiles.'
  }
];

export default function DeviceMatrix() {
  return (
    <section id="devices" className="py-28 border-b border-white/[0.08] relative bg-[#07070C]">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        
        {/* Section Header */}
        <div className="flex flex-col md:flex-row md:items-end justify-between mb-16 gap-6">
          <div>
            <div className="inline-flex items-center gap-2 font-mono text-xs text-[#00F0FF] tracking-widest uppercase mb-3">
              <Smartphone className="w-3.5 h-3.5" />
              <span>[04_DEVICE_COMPATIBILITY]</span>
            </div>
            <h2 className="font-display font-extrabold text-4xl sm:text-5xl lg:text-6xl text-white tracking-tight">
              FROM LUMIA 520 <br />
              <span className="text-slate-400 font-light">TO 1520 & W10M.</span>
            </h2>
          </div>
          <div className="font-mono text-xs text-slate-500 max-w-sm">
            Benchmarked across real Snapdragon S4 Plus, Snapdragon 400, and Snapdragon 800 Windows Phone test units.
          </div>
        </div>

        {/* 3 Columns Tier Grid */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          {devices.map((dev, idx) => (
            <div
              key={idx}
              className="p-8 rounded-3xl glass-panel border border-white/[0.08] flex flex-col justify-between"
            >
              <div>
                <div className="flex items-center justify-between mb-4">
                  <span className="font-mono text-xs text-slate-500">[TIER_0{idx + 1}]</span>
                  <span className={`px-2.5 py-0.5 rounded-full text-[10px] font-mono border ${dev.badgeColor}`}>
                    {dev.badge}
                  </span>
                </div>

                <h3 className="font-display font-bold text-xl text-white mb-1">
                  {dev.tier}
                </h3>
                <p className="text-xs text-[#00F0FF] font-mono mb-6">{dev.headline}</p>

                {/* Model Pill Badges */}
                <div className="flex flex-wrap gap-2 mb-8">
                  {dev.models.map((m, mIdx) => (
                    <span
                      key={mIdx}
                      className="px-2.5 py-1 rounded-lg bg-white/[0.04] border border-white/[0.08] text-xs font-mono text-slate-300"
                    >
                      {m}
                    </span>
                  ))}
                </div>
              </div>

              <div className="pt-4 border-t border-white/[0.06] flex items-center justify-between font-mono text-xs">
                <span className="text-slate-500">MEMORY ALLOCATION</span>
                <span className="text-white font-semibold">{dev.ramLimit}</span>
              </div>
            </div>
          ))}
        </div>

      </div>
    </section>
  );
}
